import { Player, expect, guessPlayer, learnDaily, players, test } from './support';

let answer: Player;
let wrong: Player[];

test.beforeAll(async ({ request }) => {
  ({ answer, wrong } = await learnDaily(request, 'scouting-report', await players(request, false)));
});

test.beforeEach(async ({ page }) => {
  await page.goto('/scouting-report');
  await expect(page.getByRole('heading', { name: 'Scouting Report' })).toBeVisible();
  await expect(page.locator('.meta')).toContainText('No. 1 ·');
});

test('a wrong guess gets a scouting card, and the answer wins', async ({ page }) => {
  await expect(page.getByText('Mystery player')).toBeVisible();

  await guessPlayer(page, wrong[0].fullName);
  const cards = page.getByRole('list', { name: 'Your guesses, newest first' }).locator('li.card');
  const first = page.locator('li.card').filter({ hasText: wrong[0].fullName });
  await expect(first).toBeVisible();
  await expect(first.locator('.chips small')).toHaveText(['Team', 'Conf', 'Division', 'Pos', 'Height', 'Age', 'No.']);
  await expect(page.getByRole('img', { name: '1 of 8 guesses used' })).toBeVisible();

  await page.getByRole('combobox', { name: 'Guess a player' }).fill(wrong[0].fullName);
  const duplicate = page.waitForResponse((r) => r.request().method() === 'POST' && /\/guess$/.test(r.url()));
  await page.getByRole('option').filter({ hasText: wrong[0].fullName }).first().click();
  expect((await duplicate).status()).toBe(400);
  await expect(page.locator('.kb-toast')).toContainText('You already guessed');
  await expect(page.getByRole('img', { name: '1 of 8 guesses used' })).toBeVisible();

  await guessPlayer(page, answer.fullName);
  const result = page.locator('.result');
  await expect(result).toContainText('Solved in 2 guesses');
  await expect(result.getByText(answer.fullName, { exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toHaveCount(0);
  await expect(cards).toHaveCount(2);

  // The server keeps the result, so a reload shows the finished game.
  await page.reload();
  await expect(page.locator('.result')).toContainText('Solved in 2 guesses');

  await page.getByRole('button', { name: 'Statistics' }).click();
  const stats = page.getByRole('dialog', { name: 'Statistics' });
  await expect(stats.locator('.stats div', { hasText: 'Played' }).locator('dd')).toHaveText('1');
  await expect(stats.locator('.stats div', { hasText: 'Win %' }).locator('dd')).toHaveText('100%');
  await stats.getByRole('button', { name: 'Close' }).click();

  await page.getByRole('button', { name: 'Past puzzles' }).click();
  await expect(page.getByRole('dialog', { name: 'Past puzzles' }).getByRole('link', { name: /No\. 1/ })).toContainText('Solved in 2');
});

test('eight misses lose and show the answer, then a practice round starts', async ({ page }) => {
  for (const [i, player] of wrong.slice(0, 8).entries()) {
    await guessPlayer(page, player.fullName);
    if (i < 7) await expect(page.getByRole('img', { name: `${i + 1} of 8 guesses used` })).toBeVisible();
  }

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(answer.fullName, { exact: true })).toBeVisible();
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toHaveCount(0);

  await result.getByRole('button', { name: 'Practice round' }).click();
  await expect(page).toHaveURL(/\/scouting-report\/puzzle\/\d+$/);
  await expect(page.locator('.meta')).toContainText('Practice round');
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeEnabled();
});
