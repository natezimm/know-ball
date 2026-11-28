import { Page } from '@playwright/test';
import { Player, expect, guessPlayer, learnDaily, nhlPlayers, test } from './support';

// The NHL editions of Scouting Report, Career Path and Back of the Card, on the made-up league e2e/serve.sh writes with nhl-fixtures.mjs.
let pool: Player[];
let career: { answer: Player; wrong: Player[] };
let scouting: { answer: Player; wrong: Player[] };
let card: { answer: Player; wrong: Player[] };

test.beforeAll(async ({ request }) => {
  pool = await nhlPlayers(request);
  career = await learnDaily(request, 'career-path', pool, 'nhl');
  scouting = await learnDaily(request, 'scouting-report', pool, 'nhl');
  card = await learnDaily(request, 'back-of-the-card', pool, 'nhl');
});

async function openNhl(page: Page, game: 'career-path' | 'scouting-report' | 'back-of-the-card') {
  await page.goto(`/${game}/nhl`);
  const leagues = page.getByRole('navigation', { name: 'League' });
  await expect(leagues.getByRole('link', { name: 'NHL' })).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('.meta')).toContainText('No. 1 ·');
  return leagues;
}

test('NHL Career Path is won with an NHL player and shows on the home card beside the NFL', async ({ page }) => {
  const leagues = await openNhl(page, 'career-path');
  await expect(page.getByRole('heading', { name: 'Whose career is this?' })).toBeVisible();
  expect(await page.locator('ol.rail > li').count()).toBeGreaterThanOrEqual(3);

  await guessPlayer(page, career.wrong[0].fullName);
  await guessPlayer(page, career.answer.fullName);
  await expect(page.locator('.result')).toContainText('Solved in 2 guesses');
  await expect(page.locator('.result').getByText(career.answer.fullName, { exact: true })).toBeVisible();

  // The NBA daily is its own puzzle, still to play.
  await leagues.getByRole('link', { name: 'NBA' }).click();
  await expect(page).toHaveURL(/\/career-path$/);
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeEnabled();

  await page.goto('/');
  const card = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: 'Career Path' }) });
  await expect(card.getByRole('link', { name: 'Career Path NHL: Solved in 2' })).toHaveAttribute('href', '/career-path/nhl');
  await expect(card.getByRole('link', { name: /^Career Path NFL: Play No\. 1$/ })).toHaveAttribute('href', '/career-path/nfl');
});

test('NHL Career Path is lost after six misses, and its practice round stays in the NHL', async ({ page }) => {
  await openNhl(page, 'career-path');
  for (const player of career.wrong.slice(0, 6)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(career.answer.fullName, { exact: true })).toBeVisible();

  await result.getByRole('button', { name: 'Practice round' }).click();
  await expect(page).toHaveURL(/\/career-path\/puzzle\/\d+$/);
  await expect(page.getByRole('navigation', { name: 'League' }).getByRole('link', { name: 'NHL' })).toHaveAttribute('aria-current', 'page');
});

test('NHL Scouting Report scores a guess with NHL conferences and divisions and is won', async ({ page }) => {
  await openNhl(page, 'scouting-report');

  await guessPlayer(page, scouting.wrong[0].fullName);
  const row = page.getByRole('table', { name: 'Your guesses, newest first' }).locator('.row:not(.blank)').first();
  await expect(row.getByRole('cell').nth(1)).toHaveText(/Eastern|Western|East|West/);
  await expect(row.getByRole('cell').nth(2).locator('.value')).toHaveText(/^(ATL|MET|CEN|PAC)$/);
  await expect(row.getByRole('cell').nth(3)).toHaveText(/\b(C|LW|RW|D|G)\b/);

  await guessPlayer(page, scouting.answer.fullName);
  await expect(page.locator('.result')).toContainText('Solved in 2 guesses');
});

test('NHL Scouting Report is lost after eight misses', async ({ page }) => {
  await openNhl(page, 'scouting-report');
  for (const player of scouting.wrong.slice(0, 8)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(scouting.answer.fullName, { exact: true })).toBeVisible();
});

test('NHL Back of the Card prints a skater or goalie line by two-year season and is won', async ({ page }) => {
  await openNhl(page, 'back-of-the-card');
  await expect(page.getByRole('heading', { name: 'Whose card is this?' })).toBeVisible();
  const headings = await page.locator('.back thead th').allTextContents();
  expect(headings.slice(0, 3)).toEqual(['Year', 'Club', 'G']);
  expect([['G', 'A', 'Pts', '+/-'], ['W', 'GAA', 'Sv%', 'SO']]).toContainEqual(headings.slice(3));
  await expect(page.locator('.back tbody tr td:first-child').first()).toHaveText(/^\d\d–\d\d$/);

  await guessPlayer(page, card.wrong[0].fullName);
  await guessPlayer(page, card.answer.fullName);
  await expect(page.locator('.result')).toContainText('Solved in 2 guesses');
  await expect(page.locator('.strip .name')).toHaveText(card.answer.fullName);

  await page.goto('/');
  const home = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: 'Back of the Card' }) });
  await expect(home.getByRole('link', { name: 'Back of the Card NHL: Solved in 2' })).toHaveAttribute('href', '/back-of-the-card/nhl');
});

test('NHL Back of the Card is lost after five misses', async ({ page }) => {
  await openNhl(page, 'back-of-the-card');
  for (const player of card.wrong.slice(0, 5)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(card.answer.fullName, { exact: true })).toBeVisible();
});
