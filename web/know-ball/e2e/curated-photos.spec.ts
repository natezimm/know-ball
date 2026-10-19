import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { expect, repoRoot, test } from './support';

// Opt-in: the licensed media packs live outside git. The ordinary CI suite skips these tests.
const assets = process.env['E2E_CURATED_ASSETS'];
const admin = { 'X-Admin-Key': process.env['E2E_ADMIN_KEY'] ?? 'e2e-admin-key' };
const batches = ['iconic-pair', 'next-pair', 'wilt-100'];
interface Photo {
  key: string; player: string; year: number; team: string; caption: string;
  sourceUrl: string; credit: string; license: string; photo: string; cutout: string;
}
const photos: Photo[] = batches.flatMap(batch =>
  JSON.parse(readFileSync(resolve(repoRoot, `content/whos-that/${batch}.json`), 'utf8')).puzzles);
const ids = new Map<string, number>();

test.describe('prepared iconic photographs', () => {
  test.describe.configure({ mode: 'serial', timeout: 120_000 });
  test.skip(!assets, 'Extract all three media packs and set E2E_CURATED_ASSETS to their parent folder.');

  test.beforeAll(async ({ request, baseURL }) => {
    if (new URL(baseURL!).hostname !== '127.0.0.1') throw new Error('Photo review requires the disposable loopback server.');
    for (const batch of batches) {
      const manifest = resolve(repoRoot, `content/whos-that/${batch}.json`);
      const root = resolve(assets!, batch);
      const items: Photo[] = JSON.parse(readFileSync(manifest, 'utf8')).puzzles;
      const evidence = JSON.parse(readFileSync(resolve(repoRoot, `content/whos-that/${batch}-media-validation.json`), 'utf8'));
      // Refuse substitutes: the real-content gate applies to these exact reviewed inputs.
      for (const item of items) {
        const report = evidence.reports.find((r: { key: string }) => r.key === item.key);
        expect(report, `missing media evidence for ${item.key}`).toBeTruthy();
        for (const field of ['photo', 'cutout'] as const) {
          const hash = createHash('sha256').update(readFileSync(resolve(root, item[field]))).digest('hex');
          expect(hash, `${item.key} ${field} differs from the prepared input`).toBe(report[`${field}Sha256`]);
        }
      }
      const upload = () => execFileSync('python3', [resolve(repoRoot, 'scripts/curated-puzzles.py'),
        'upload', '--manifest', manifest, '--assets', root, '--base-url', baseURL!], {
        env: { ...process.env, KNOWBALL_ADMIN_KEY: admin['X-Admin-Key'] }, encoding: 'utf8', timeout: 60_000,
      }).trim().split('\n').map(line => JSON.parse(line));
      const first = upload();
      expect(first.map(row => row.status)).toEqual(items.map(() => 'created'));
      for (const row of first) { expect(row.state).toBe('draft'); ids.set(row.key, row.puzzleId); }
      const rerun = upload();
      expect(rerun.map(row => row.status)).toEqual(items.map(() => 'already-exists'));
      expect(rerun.map(row => row.puzzleId)).toEqual(first.map(row => row.puzzleId));
    }
    const response = await request.get('/api/admin/whos-that/puzzles', { headers: admin });
    expect(response.ok()).toBe(true);
    const drafts = await response.json();
    for (const item of photos) {
      const draft = drafts.find((p: { id: number }) => p.id === ids.get(item.key));
      expect(draft).toMatchObject({ state: 'draft', date: null, year: item.year, team: item.team,
        caption: item.caption, attribution: { sourceUrl: item.sourceUrl, credit: item.credit, license: item.license } });
    }
  });

  for (const width of [390, 1280]) for (const item of photos) {
    test(`${item.key}: preview and credit at ${width}px`, async ({ page, request }, testInfo) => {
      await page.setViewportSize({ width, height: 844 });
      await page.goto('/admin');
      await page.getByLabel('Admin key', { exact: true }).fill(admin['X-Admin-Key']);
      await page.getByRole('button', { name: 'Unlock', exact: true }).click();
      const row = page.locator('.puzzles li').filter({ hasText: item.caption });
      await row.getByRole('button', { name: 'Preview', exact: true }).click();
      const preview = page.getByRole('region', { name: 'Player preview' });
      const anon = await page.evaluate(() => localStorage.getItem('knowball.anonId')!);
      const stats = () => request.get('/api/me/stats', { headers: { 'X-Anon-Id': anon } }).then(r => r.json());
      const before = await stats();
      const imageReady = async () => {
        const image = preview.getByRole('img');
        await expect(image).toBeVisible();
        await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true);
        // Tall source photos must stay inside the shared 4:3 stage at either width.
        const bounds = await image.evaluate(img => {
          const frame = img.parentElement!.getBoundingClientRect(), photo = img.getBoundingClientRect();
          return { frameWidth: frame.width, frameHeight: frame.height,
            fits: photo.left >= frame.left - 1 && photo.right <= frame.right + 1 &&
              photo.top >= frame.top - 1 && photo.bottom <= frame.bottom + 1 };
        });
        expect(bounds.fits).toBe(true);
        expect(Math.abs(bounds.frameHeight - bounds.frameWidth * 3 / 4)).toBeLessThanOrEqual(1);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      };
      await imageReady();
      await expect(preview).toContainText('Locked until 1 miss');
      await expect(preview.getByRole('link', { name: 'Photo source' })).toHaveCount(0);
      await preview.getByRole('button', { name: '1 miss', exact: true }).click();
      await expect(preview).toContainText(`Year: ${item.year}`);
      await preview.getByRole('button', { name: '2 misses', exact: true }).click();
      await expect(preview).toContainText(`Team: ${item.team}`);
      for (const stage of ['3 misses', '4 misses']) {
        await preview.getByRole('button', { name: stage, exact: true }).click();
        await expect(preview.getByRole('img', { name: 'The photo, heavily blurred' })).toBeVisible();
        await imageReady();
      }
      await preview.getByRole('button', { name: 'Reveal', exact: true }).click();
      await expect(preview.getByRole('img', { name: `The photo: ${item.player}`, exact: true })).toBeVisible();
      await expect(preview).toContainText(item.caption);
      await expect(preview).toContainText(item.credit);
      await expect(preview).toContainText(item.license);
      // The stage clips overflow: a page-width check alone can miss a truncated license URL.
      for (const notice of await preview.locator('.credit > span').all()) {
        expect(await notice.evaluate(el => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
      }
      await expect(preview.getByRole('link', { name: 'Photo source' })).toHaveAttribute('href', item.sourceUrl);
      await imageReady();
      await preview.screenshot({ path: testInfo.outputPath('reveal.png') });
      await preview.getByRole('button', { name: 'Silhouette', exact: true }).click();
      await expect(preview.getByRole('img', { name: 'Black outline of the mystery player' })).toBeVisible();
      await expect(preview.getByRole('link', { name: 'Photo source' })).toHaveCount(0);
      await imageReady();
      await preview.screenshot({ path: testInfo.outputPath('silhouette.png') });
      expect(await stats()).toEqual(before);
      const draft = await (await request.get('/api/admin/whos-that/puzzles', { headers: admin })).json();
      expect(draft.find((p: { id: number }) => p.id === ids.get(item.key))).toMatchObject({ state: 'draft', date: null });
    });
  }
});
