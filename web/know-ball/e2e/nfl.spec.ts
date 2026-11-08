import { Page } from '@playwright/test';
import { Player, expect, guessPlayer, learnDaily, nflPlayers, test } from './support';

// The NFL editions of Scouting Report and Career Path, on the made-up league e2e/serve.sh loads.
let pool: Player[];
let career: { answer: Player; wrong: Player[] };
let scouting: { answer: Player; wrong: Player[] };

test.beforeAll(async ({ request }) => {
  pool = await nflPlayers(request);
  career = await learnDaily(request, 'career-path', pool, 'nfl');
  scouting = await learnDaily(request, 'scouting-report', pool, 'nfl');
});

async function openNfl(page: Page, game: 'career-path' | 'scouting-report') {
  await page.goto(`/${game}/nfl`);
  const leagues = page.getByRole('navigation', { name: 'League' });
  await expect(leagues.getByRole('link', { name: 'NFL' })).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('.meta')).toContainText('No. 1 ·');
  return leagues;
}

test('NFL Career Path is won with an NFL player and kept apart from the NBA daily', async ({ page }) => {
  const leagues = await openNfl(page, 'career-path');
  await expect(page.getByRole('heading', { name: 'Whose career is this?' })).toBeVisible();
  await expect(page.locator('ol.rail > li')).toHaveCount(3);

  await guessPlayer(page, career.wrong[0].fullName);
  await guessPlayer(page, career.answer.fullName);
  await expect(page.locator('.result')).toContainText('Solved in 2 guesses');
  await expect(page.locator('.result').getByText(career.answer.fullName, { exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Statistics' }).click();
  const stats = page.getByRole('dialog', { name: 'Statistics' });
  await expect(stats.locator('.stats div', { hasText: 'Played' }).locator('dd')).toHaveText('1');
  await stats.getByRole('button', { name: 'Close' }).click();

  // The NBA daily is its own puzzle, still to play.
  await leagues.getByRole('link', { name: 'NBA' }).click();
  await expect(page).toHaveURL(/\/career-path$/);
  await expect(leagues.getByRole('link', { name: 'NBA' })).toHaveAttribute('aria-current', 'page');
  await expect(page.getByRole('heading', { name: 'Whose career is this?' })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeEnabled();

  // The home page shows both editions on the one card.
  await page.goto('/');
  const card = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: 'Career Path' }) });
  await expect(card.getByRole('link', { name: 'Career Path NFL: Solved in 2' })).toHaveAttribute('href', '/career-path/nfl');
  await expect(card.getByRole('link', { name: /^Career Path NBA: Play No\. 1$/ })).toHaveAttribute('href', '/career-path');
});

test('NFL Career Path is lost after six misses, and its practice round stays in the NFL', async ({ page }) => {
  await openNfl(page, 'career-path');
  for (const player of career.wrong.slice(0, 6)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(career.answer.fullName, { exact: true })).toBeVisible();

  await result.getByRole('button', { name: 'Practice round' }).click();
  await expect(page).toHaveURL(/\/career-path\/puzzle\/\d+$/);
  await expect(page.locator('.meta')).toContainText('Practice round');
  await expect(page.getByRole('navigation', { name: 'League' }).getByRole('link', { name: 'NFL' })).toHaveAttribute('aria-current', 'page');
  await page.getByRole('combobox', { name: 'Guess a player' }).fill(career.answer.fullName.slice(0, 4));
  await expect(page.getByRole('option').filter({ hasText: career.answer.fullName })).toBeVisible();
});

test('NFL Scouting Report scores a guess with NFL clues and is won', async ({ page }) => {
  await openNfl(page, 'scouting-report');
  await expect(page.getByRole('dialog', { name: 'How to play' })).toHaveCount(0);

  await guessPlayer(page, scouting.wrong[0].fullName);
  const board = page.getByRole('table', { name: 'Your guesses, oldest first' });
  const row = board.locator('.row:not(.blank)').first();
  await expect(row.getByRole('cell')).toHaveCount(7);
  await expect(row.getByRole('cell').nth(1)).toHaveText(/AFC|NFC/);
  await expect(row.getByRole('cell').nth(2).locator('.value')).toHaveText(/^[NSEW]$/);

  await guessPlayer(page, scouting.answer.fullName);
  await expect(page.locator('.result')).toContainText('Solved in 2 guesses');
  await page.getByRole('button', { name: 'Past puzzles' }).click();
  await expect(page.getByRole('dialog', { name: 'Past puzzles' }).getByRole('link', { name: /No\. 1/ })).toContainText('Solved in 2');
});

test('NFL Scouting Report is lost after eight misses', async ({ page }) => {
  await openNfl(page, 'scouting-report');
  for (const player of scouting.wrong.slice(0, 8)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(scouting.answer.fullName, { exact: true })).toBeVisible();
});
