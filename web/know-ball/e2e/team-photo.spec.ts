import { Player, asPlayer, expect, guessPlayer, players, test } from './support';

let starters: Player[];
let misses: Player[];

/**
 * Learns today's starting five, and four players who count as misses, by losing the daily through the API as a
 * throwaway player. A bench player is skipped, since he costs nothing.
 */
test.beforeAll(async ({ request }) => {
  const pool = await players(request, true);
  const api = await asPlayer(request);
  let state = await api.daily('team-photo');
  misses = [];
  for (const player of pool) {
    state = await api.guess('team-photo', state.puzzleId, player.id);
    const last = state.guesses[state.guesses.length - 1];
    if (!last.correct && last.miss !== false) misses.push(player);
    if (state.status !== 'playing') break;
  }
  expect(state.status).toBe('lost');
  starters = (state.answer as unknown as { slots: { playerId: number; name: string }[] }).slots.map((s) => ({ id: s.playerId, fullName: s.name }));
  expect(starters).toHaveLength(5);
});

test.beforeEach(async ({ page }) => {
  await page.goto('/team-photo');
  await expect(page.locator('.meta')).toContainText('No. 1 ·');
  await expect(page.locator('.plaque b')).toHaveText(/^\d{4}–\d\d .+/);
});

test('naming the five fills the photo, and a miss uses one dot', async ({ page }) => {
  await expect(page.locator('.spot')).toHaveCount(5);
  await expect(page.locator('.spot.found')).toHaveCount(0);
  await expect(page.getByText('0 of 5 in the photo')).toBeVisible();

  await guessPlayer(page, misses[0].fullName);
  await expect(page.getByRole('img', { name: '1 of 4 misses used' })).toBeVisible();
  await expect(page.locator('.note')).toContainText(`${misses[0].fullName} isn't in this photo`);

  for (const [i, starter] of starters.entries()) {
    await guessPlayer(page, starter.fullName);
    if (i < 4) await expect(page.getByText(`${i + 1} of 5 in the photo`)).toBeVisible();
  }
  await expect(page.locator('.spot.found')).toHaveCount(5);
  const result = page.locator('.result');
  await expect(result).toContainText('Every name, 1 miss');
  await expect(result.locator('strong')).toHaveText(await page.locator('.plaque b').innerText());
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toHaveCount(0);
});

test('four misses lose and show the missing faces', async ({ page }) => {
  await guessPlayer(page, starters[0].fullName);
  for (const player of misses.slice(0, 3)) await guessPlayer(page, player.fullName);
  await expect(page.getByRole('img', { name: '3 of 4 misses used' })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeVisible();
  await guessPlayer(page, misses[3].fullName);

  await expect(page.locator('.result')).toContainText('Out of misses');
  await expect(page.locator('.spot.found')).toHaveCount(1);
  await expect(page.locator('.spot.missed')).toHaveCount(4);
  await expect(page.getByRole('list', { name: 'Misses, newest first' }).getByRole('listitem')).toHaveCount(4);
});
