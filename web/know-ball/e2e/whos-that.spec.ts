import { crc32, deflateSync } from 'node:zlib';
import { Player, asPlayer, expect, guessPlayer, players, test } from './support';

const ADMIN_KEY = process.env['E2E_ADMIN_KEY'] ?? 'e2e-admin-key';

let answer: Player;
let wrong: Player[];

/**
 * Who's That has no generated puzzles: one is made through the admin API, as the admin page does,
 * from a drawn photo and cut-out, and put on today's calendar.
 */
test.beforeAll(async ({ request }) => {
  const pool = await players(request, true);
  answer = pool.find((p) => p.fullName === 'LeBron James')!;
  wrong = pool.filter((p) => p.id !== answer.id);

  const today = await request.get('/api/whos-that/daily', { headers: { 'X-Anon-Id': 'e2e-whos-that-check' } });
  if (today.ok()) return; // a reused server already has it

  const headers = { 'X-Admin-Key': ADMIN_KEY };
  const created = await request.post('/api/admin/whos-that/puzzles', {
    headers,
    multipart: {
      photo: { name: 'photo.png', mimeType: 'image/png', buffer: png(false) },
      cutout: { name: 'cutout.png', mimeType: 'image/png', buffer: png(true) },
      playerId: String(answer.id),
      year: '2016',
      team: 'Cleveland Cavaliers',
      caption: 'The block in Game 7',
    },
  });
  expect(created.status()).toBe(201);
  const { id } = await created.json();
  expect((await request.post(`/api/admin/whos-that/puzzles/${id}/schedule`, { headers, data: {} })).ok()).toBe(true);
  expect((await asPlayer(request).then((api) => api.daily('whos-that'))).status).toBe('playing');
});

test.beforeEach(async ({ page }) => {
  await page.goto('/whos-that');
  await expect(page.getByRole('img', { name: 'Black outline of the mystery player' })).toBeVisible();
});

test('misses release the year, the team and the blurred photo, and the answer wins', async ({ page }) => {
  // The silhouette is the first rung, open from the start.
  const opened = page.getByRole('list', { name: 'Hints' }).locator('li.done');
  await expect(opened).toHaveCount(1);
  await expect(page.getByRole('list', { name: 'Hints' })).toContainText('After 1 miss');

  await guessPlayer(page, wrong[0].fullName);
  await expect(opened).toHaveCount(2);
  await expect(opened.nth(1)).toContainText('2016');
  await guessPlayer(page, wrong[1].fullName);
  await expect(opened.nth(2)).toContainText('Cleveland Cavaliers');
  await guessPlayer(page, wrong[2].fullName);
  await expect(opened).toHaveCount(4);
  await expect(page.getByRole('img', { name: 'The photo, heavily blurred' })).toBeVisible();
  await page.getByRole('button', { name: 'Outline' }).click();
  await expect(page.getByRole('img', { name: 'Black outline of the mystery player' })).toBeVisible();

  await guessPlayer(page, answer.fullName);
  const result = page.locator('.result');
  await expect(result).toContainText('Solved in 4 guesses');
  await expect(result).toContainText('Cleveland Cavaliers, 2016');
  await expect(page.getByRole('img', { name: 'The photo: LeBron James' })).toBeVisible();
  await expect(page.getByText('The block in Game 7')).toBeVisible();
});

test('five misses lose and show the photo with its story', async ({ page }) => {
  for (const player of wrong.slice(0, 5)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText('LeBron James', { exact: true })).toBeVisible();
  await expect(page.getByRole('img', { name: 'The photo: LeBron James' })).toBeVisible();
  await expect(page.getByText('The block in Game 7')).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toHaveCount(0);
});

/** A 200 by 200 PNG: an orange figure on blue, or on a clear background for the cut-out. */
function png(clear: boolean): Buffer {
  const size = 200;
  const rows = Buffer.alloc(size * (size * 4 + 1));
  for (let y = 0, o = 0; y < size; y++) {
    rows[o++] = 0;
    for (let x = 0; x < size; x++, o += 4) {
      const inside = x >= 60 && x < 140 && y >= 30 && y < 180;
      rows.set(inside ? [220, 80, 40, 255] : clear ? [0, 0, 0, 0] : [70, 130, 180, 255], o);
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header.set([8, 6, 0, 0, 0], 8);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(rows)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

function chunk(type: string, data: Buffer): Buffer {
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const out = Buffer.alloc(body.length + 8);
  out.writeUInt32BE(data.length, 0);
  body.copy(out, 4);
  out.writeUInt32BE(crc32(body), body.length + 4);
  return out;
}
