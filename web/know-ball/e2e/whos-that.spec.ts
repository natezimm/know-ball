import { png } from './images';
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
