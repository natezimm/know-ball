import { png } from './images';
import { expect, test } from './support';

const ADMIN_KEY = process.env['E2E_ADMIN_KEY'] ?? 'e2e-admin-key';

test('author uploads, corrects, previews and schedules a photo puzzle through the admin form', async ({ page, request }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/admin');
  await page.getByLabel('Admin key', { exact: true }).fill(ADMIN_KEY);
  await page.getByRole('button', { name: 'Unlock', exact: true }).click();
  await expect(page.getByRole('heading', { name: "New Who's That Sportsman? puzzle" })).toBeVisible();

  const form = page.locator('form.new');
  await form.getByLabel('Photo', { exact: true }).setInputFiles({ name: 'photo.png', mimeType: 'image/png', buffer: png(false, 200, 800) });
  await form.getByLabel('Cut-out').setInputFiles({ name: 'cutout.png', mimeType: 'image/png', buffer: png(true, 200, 800) });
  await form.getByRole('combobox', { name: 'Player in the photo' }).fill('Tim Duncan');
  await form.getByRole('option').filter({ hasText: 'Tim Duncan' }).click();
  await form.getByLabel('Year', { exact: true }).fill('2000');
  await form.getByLabel('Team at the time').fill('San Antonio Spurs');
  await form.getByLabel('Caption, shown with the photo at the end').fill('A draft story');
  await form.getByLabel('Photo source URL').fill('https://example.com/duncan');
  await form.getByLabel('Photo credit').fill('Example photographer');
  const license = 'Used with permission — https://example.com/licenses/a-very-long-image-license-and-attribution-notice';
  await form.getByLabel('License or permission').fill(license);
  const created = page.waitForResponse(r => r.url().endsWith('/api/admin/whos-that/puzzles') && r.request().method() === 'POST');
  await form.getByRole('button', { name: 'Make draft' }).click();
  const response = await created;
  expect(response.status()).toBe(201);
  const draft = await response.json();
  const anon = await page.evaluate(() => localStorage.getItem('knowball.anonId')!);
  const headers = { 'X-Anon-Id': anon };
  const statsBefore = await (await request.get('/api/me/stats', { headers })).json();
  let scheduled = false;
  try {
    const row = page.locator('.puzzles li').filter({ has: page.getByText('Tim Duncan', { exact: true }) }).filter({ hasText: 'A draft story' });
    await row.getByRole('button', { name: 'Edit', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Edit draft', exact: true })).toBeVisible();
    await expect(form.getByLabel('Year', { exact: true })).toHaveValue('2000');
    await form.getByLabel('Year', { exact: true }).fill('2001');
    await form.getByLabel('Caption, shown with the photo at the end').fill('The corrected story');
    await form.getByRole('button', { name: 'Save draft' }).click();
    const edited = page.locator('.puzzles li').filter({ hasText: 'The corrected story' });
    await expect(edited).toContainText('2001');
    await edited.getByRole('button', { name: 'Preview', exact: true }).click();
    const preview = page.getByRole('region', { name: 'Player preview' });
    const checkPortraitFits = async () => {
      const image = preview.getByRole('img');
      await expect.poll(() => image.evaluate((img: HTMLImageElement) => img.complete && img.naturalWidth > 0)).toBe(true);
      const bounds = await image.evaluate(img => {
        const frame = img.parentElement!.getBoundingClientRect(), photo = img.getBoundingClientRect();
        return { ratioError: Math.abs(frame.height - frame.width * .75), fits:
          photo.left >= frame.left - 1 && photo.right <= frame.right + 1 &&
          photo.top >= frame.top - 1 && photo.bottom <= frame.bottom + 1 };
      });
      expect(bounds.ratioError).toBeLessThanOrEqual(1);
      expect(bounds.fits).toBe(true);
    };
    await expect(preview.getByRole('img', { name: 'Black outline of the mystery player' })).toBeVisible();
    await checkPortraitFits();
    await expect(preview).toContainText('Locked until 1 miss');
    await expect(preview.getByText('The corrected story')).toHaveCount(0);
    await preview.getByRole('button', { name: '1 miss', exact: true }).click();
    await expect(preview).toContainText('Year: 2001');
    await preview.getByRole('button', { name: '2 misses', exact: true }).click();
    await expect(preview).toContainText('Team: San Antonio Spurs');
    await preview.getByRole('button', { name: '3 misses', exact: true }).click();
    await expect(preview.getByRole('img', { name: 'The photo, heavily blurred' })).toBeVisible();
    await checkPortraitFits();
    await preview.getByRole('button', { name: 'Reveal', exact: true }).click();
    await expect(preview.getByRole('img', { name: 'The photo: Tim Duncan' })).toBeVisible();
    await checkPortraitFits();
    await expect(preview.getByText('The corrected story')).toBeVisible();
    await expect(preview).toContainText('Example photographer');
    await expect(preview).toContainText(license);
    for (const notice of await preview.locator('.credit > span').all()) {
      expect(await notice.evaluate(el => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
    }
    await expect(preview.getByRole('link', { name: 'Photo source' })).toHaveAttribute('href', 'https://example.com/duncan');
    expect(await (await request.get('/api/me/stats', { headers })).json()).toEqual(statsBefore);
    await preview.getByRole('button', { name: 'Silhouette', exact: true }).click();
    await expect(preview.getByRole('img', { name: 'Black outline of the mystery player' })).toBeVisible();
    await expect(preview.getByRole('link', { name: 'Photo source' })).toHaveCount(0);
    await preview.getByRole('button', { name: 'Close preview' }).click();
    // A distant date keeps this authoring test independent of today's gameplay tests.
    const date = new Date(Date.now() + 120 * 86400000).toISOString().slice(0, 10);
    await edited.getByLabel('Date for Tim Duncan').fill(date);
    await edited.getByRole('button', { name: 'Schedule', exact: true }).click();
    await expect(edited).toContainText('No.');
    scheduled = true;
    await expect(edited.getByRole('button', { name: 'Edit', exact: true })).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  } finally {
    const admin = { 'X-Admin-Key': ADMIN_KEY };
    if (scheduled) await request.post(`/api/admin/whos-that/puzzles/${draft.id}/unschedule`, { headers: admin });
    await request.delete(`/api/admin/whos-that/puzzles/${draft.id}`, { headers: admin });
  }
});
