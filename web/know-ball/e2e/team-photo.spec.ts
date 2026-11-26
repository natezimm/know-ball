import { APIRequestContext } from '@playwright/test';
import { Player, asPlayer, expect, guessPlayer, mlbPlayers, nflPlayers, players, test } from './support';

let starters: Player[];
let misses: Player[];

/**
 * Learns today's starters (five, or nine in baseball), and four players who count as misses, by losing the daily through the API as a throwaway
 * player. A bench player is skipped, since he costs nothing.
 */
async function learnPhoto(request: APIRequestContext, pool: Player[], league: 'nba' | 'nfl' | 'mlb', size = 5) {
  const api = await asPlayer(request);
  let state = await api.daily('team-photo', league);
  const wrong: Player[] = [];
  for (const player of pool) {
    state = await api.guess('team-photo', state.puzzleId, player.id);
    const last = state.guesses[state.guesses.length - 1];
    if (!last.correct && last.miss !== false) wrong.push(player);
    if (state.status !== 'playing') break;
  }
  expect(state.status).toBe('lost');
  const five = (state.answer as unknown as { slots: { playerId: number; name: string }[] }).slots.map((s) => ({ id: s.playerId, fullName: s.name }));
  expect(five).toHaveLength(size);
  return { five, wrong };
}

test.beforeAll(async ({ request }) => {
  ({ five: starters, wrong: misses } = await learnPhoto(request, await players(request, true), 'nba'));
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

test.describe('NFL edition', () => {
  let offense: Player[];
  let outsiders: Player[];

  test.beforeAll(async ({ request }) => {
    // The made-up league's players, last first: its Kansas City offense is listed first, and the misses come first here.
    const pool = (await nflPlayers(request)).reverse();
    const probe = await learnPhoto(request, pool, 'nfl');
    offense = probe.five;
    outsiders = probe.wrong;
  });

  test('a Super Bowl team\'s offense is named from its season totals and won', async ({ page }) => {
    await page.goto('/team-photo/nfl');
    await expect(page.getByRole('navigation', { name: 'League' }).getByRole('link', { name: 'NFL' })).toHaveAttribute('aria-current', 'page');
    await expect(page.locator('.meta')).toContainText('No. 1 ·');
    await expect(page.locator('.plaque b')).toHaveText(/^20\d\d Kansas City Chiefs$/);
    await expect(page.locator('.plaque small')).toHaveText(/^\d+–\d+ · (Won|Lost) Super Bowl [LXVI]+$/);
    await expect(page.locator('.row:not(.front) .pos')).toHaveText(['WR', 'TE', 'WR']);
    await expect(page.locator('.row.front .pos')).toHaveText(['QB', 'RB']);
    await expect(page.locator('.row.front .plate').first()).toContainText('pass yds');

    await guessPlayer(page, outsiders[0].fullName);
    await expect(page.getByRole('img', { name: '1 of 4 misses used' })).toBeVisible();
    for (const player of offense) await guessPlayer(page, player.fullName);
    await expect(page.locator('.spot.found')).toHaveCount(5);
    await expect(page.locator('.result')).toContainText('Every name, 1 miss');

    await page.goto('/');
    const home = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: 'Team Photo' }) });
    await expect(home.getByRole('link', { name: /^Team Photo NFL: / })).toHaveAttribute('href', '/team-photo/nfl');
  });
});

test.describe('MLB edition', () => {
  let nine: Player[];
  let outsiders: Player[];

  test.beforeAll(async ({ request }) => {
    // The made-up league lists its Dodgers last, so the misses come first.
    const probe = await learnPhoto(request, await mlbPlayers(request), 'mlb', 9);
    nine = probe.five;
    outsiders = probe.wrong;
  });

  test('a World Series team stands as on the field and is won', async ({ page }) => {
    await page.goto('/team-photo/mlb');
    await expect(page.getByRole('navigation', { name: 'League' }).getByRole('link', { name: 'MLB' })).toHaveAttribute('aria-current', 'page');
    await expect(page.locator('.meta')).toContainText('No. 1 ·');
    await expect(page.locator('.plaque b')).toHaveText(/^20\d\d Los Angeles Dodgers$/);
    await expect(page.locator('.plaque small')).toHaveText(/^\d+–\d+ · (Won|Lost) the World Series$/);
    await expect(page.getByRole('list', { name: 'Back row' }).locator('.pos')).toHaveText(['LF', 'CF', 'RF']);
    await expect(page.getByRole('list', { name: 'Middle row' }).locator('.pos')).toHaveText(['3B', 'SS', '2B', '1B']);
    await expect(page.getByRole('list', { name: 'Front row' }).locator('.pos')).toHaveText(['SP', 'C']);
    await expect(page.locator('.row.front .plate').first()).toContainText('ERA');
    await expect(page.locator('.row.middle .plate').first()).toContainText(/^\.\d{3} avg/);

    await guessPlayer(page, outsiders[0].fullName);
    await expect(page.getByRole('img', { name: '1 of 4 misses used' })).toBeVisible();
    for (const player of nine) await guessPlayer(page, player.fullName);
    await expect(page.locator('.spot.found')).toHaveCount(9);
    await expect(page.locator('.result')).toContainText('Every name, 1 miss');

    await page.goto('/');
    const home = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: 'Team Photo' }) });
    await expect(home.getByRole('link', { name: /^Team Photo MLB: / })).toHaveAttribute('href', '/team-photo/mlb');
  });
});
