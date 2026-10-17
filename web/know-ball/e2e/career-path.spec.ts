import { Player, expect, guessPlayer, learnDaily, players, test } from './support';

let answer: Player;
let wrong: Player[];

test.beforeAll(async ({ request }) => {
  ({ answer, wrong } = await learnDaily(request, 'career-path', await players(request, true)));
});

test.beforeEach(async ({ page }) => {
  await page.goto('/career-path');
  await expect(page.getByRole('heading', { name: 'Whose career is this?' })).toBeVisible();
  await expect(page.locator('.meta')).toContainText('No. 1 ·');
});

test('the path is shown, a miss opens a hint, and the answer wins', async ({ page }) => {
  // At least three franchises, each with its years.
  const stops = page.locator('ol.rail > li');
  expect(await stops.count()).toBeGreaterThanOrEqual(3);
  await expect(stops.first().locator('small')).toContainText(/season/);
  const hints = page.getByRole('list', { name: 'Hints' }).getByRole('listitem');
  await expect(hints.first()).not.toHaveClass(/open/);

  await guessPlayer(page, wrong[0].fullName);
  await expect(hints.first()).toHaveClass(/open/);
  const misses = page.getByRole('list', { name: 'Wrong guesses, newest first' });
  await expect(misses.getByText(wrong[0].fullName, { exact: true })).toBeVisible();
  await expect(misses.locator('small')).toHaveText(/^(None of these stops|Also played for .+)$/);

  await guessPlayer(page, answer.fullName);
  const result = page.locator('.result');
  await expect(result).toContainText('Solved in 2 guesses');
  await expect(result.getByText(answer.fullName, { exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toHaveCount(0);
});

test('six misses lose and show the answer, then a practice round starts', async ({ page }) => {
  for (const player of wrong.slice(0, 6)) await guessPlayer(page, player.fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(answer.fullName, { exact: true })).toBeVisible();
  await expect(page.getByRole('list', { name: 'Wrong guesses, newest first' }).getByRole('listitem')).toHaveCount(6);

  await result.getByRole('button', { name: 'Practice round' }).click();
  await expect(page).toHaveURL(/\/career-path\/puzzle\/\d+$/);
  await expect(page.locator('.meta')).toContainText('Practice round');
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeEnabled();
});
