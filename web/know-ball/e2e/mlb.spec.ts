import { Page } from '@playwright/test';
import { Player, expect, guessPlayer, learnDaily, mlbPlayers, test } from './support';

// The MLB editions of Scouting Report and Career Path, on the made-up league e2e/serve.sh loads.
let pool: Player[];
let career: { answer: Player; wrong: Player[] };
let scouting: { answer: Player; wrong: Player[] };

test.beforeAll(async ({ request }) => {
  pool = await mlbPlayers(request);
  career = await learnDaily(request, 'career-path', pool, 'mlb');
  scouting = await learnDaily(request, 'scouting-report', pool, 'mlb');
});

async function openMlb(page: Page, game: 'career-path' | 'scouting-report') {
  await page.goto(`/${game}/mlb`);
  const leagues = page.getByRole('navigation', { name: 'League' });
  await expect(leagues.getByRole('link', { name: 'MLB' })).toHaveAttribute('aria-current', 'page');
  await expect(page.locator('.meta')).toContainText('No. 1 ·');
  return leagues;
}

test('MLB Career Path is won with an MLB player and shown on the home card', async ({ page }) => {
  await openMlb(page, 'career-path');
  await expect(page.getByRole('heading', { name: 'Whose career is this?' })).toBeVisible();
  // Every made-up traveller ends this season with a club, so the last stop is still open.
  await expect(page.locator('ol.rail > li').last()).toContainText(/\d{4}– ·/);

  await guessPlayer(page, career.wrong[0].fullName);
  await guessPlayer(page, career.answer.fullName);
  await expect(page.locator('.result')).toContainText('Solved in 2 guesses');
  await expect(page.locator('.result').getByText(career.answer.fullName, { exact: true })).toBeVisible();

  // The home page shows all three editions on the one card.
  await page.goto('/');
  const card = page.getByRole('listitem').filter({ has: page.getByRole('heading', { name: 'Career Path' }) });
  await expect(card.getByRole('link', { name: 'Career Path MLB: Solved in 2' })).toHaveAttribute('href', '/career-path/mlb');
  await expect(card.getByRole('link', { name: /^Career Path NFL: / })).toHaveAttribute('href', '/career-path/nfl');
  await expect(card.getByRole('link', { name: /^Career Path NBA: / })).toHaveAttribute('href', '/career-path');
});

test('MLB Career Path is lost after six misses', async ({ page }) => {
  await openMlb(page, 'career-path');
  for (const player of career.wrong.slice(0, 6)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(career.answer.fullName, { exact: true })).toBeVisible();
});

test('MLB Scouting Report scores a guess by league and division and is won', async ({ page }) => {
  await openMlb(page, 'scouting-report');
  await guessPlayer(page, scouting.wrong[0].fullName);
  const board = page.getByRole('table', { name: 'Your guesses, newest first' });
  await expect(board.getByRole('columnheader').nth(1)).toHaveText('Lg');
  const row = board.locator('.row:not(.blank)').first();
  await expect(row.getByRole('cell').nth(1)).toHaveText(/AL|NL/);
  await expect(row.getByRole('cell').nth(2).locator('.value')).toHaveText(/^[EWC]$/);

  await guessPlayer(page, scouting.answer.fullName);
  await expect(page.locator('.result')).toContainText('Solved in 2 guesses');
});

test('MLB Scouting Report is lost after eight misses', async ({ page }) => {
  await openMlb(page, 'scouting-report');
  for (const player of scouting.wrong.slice(0, 8)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(scouting.answer.fullName, { exact: true })).toBeVisible();
});
