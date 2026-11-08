import { Player, expect, guessPlayer, learnDaily, players, test } from './support';

let answer: Player;
let wrong: Player[];

test.beforeAll(async ({ request }) => {
  ({ answer, wrong } = await learnDaily(request, 'back-of-the-card', await players(request, true)));
});

test.beforeEach(async ({ page }) => {
  await page.goto('/back-of-the-card');
  await expect(page.getByRole('heading', { name: 'Whose card is this?' })).toBeVisible();
  await expect(page.locator('.meta')).toContainText('No. 1 ·');
});

test('the card prints every season, a miss uncovers the clubs, and the answer wins', async ({ page }) => {
  const rows = page.locator('.back tbody tr');
  expect(await rows.count()).toBeGreaterThanOrEqual(5);
  await expect(rows.first().locator('td').first()).toHaveText(/^\d\d–\d\d$/);
  await expect(page.locator('.back tbody .club .redact')).toHaveCount(await rows.count());
  const steps = page.getByRole('list', { name: 'What each miss uncovers' }).getByRole('listitem');
  await expect(steps).toHaveCount(4);
  await expect(steps.first()).not.toHaveClass(/open/);

  await guessPlayer(page, wrong[0].fullName);
  await expect(steps.first()).toHaveClass(/open/);
  await expect(page.locator('.back tbody .club .redact')).toHaveCount(0);
  await expect(page.locator('.back tbody .club b').first()).toHaveText(/^[A-Z]{2,3}(\/[A-Z]{2,3})*$/);
  const misses = page.getByRole('list', { name: 'Wrong guesses, newest first' });
  await expect(misses.getByText(wrong[0].fullName, { exact: true })).toBeVisible();

  await guessPlayer(page, answer.fullName);
  const result = page.locator('.result');
  await expect(result).toContainText('Solved in 2 guesses');
  await expect(result.getByText(answer.fullName, { exact: true })).toBeVisible();
  await expect(page.locator('.back .strip .name')).toHaveText(answer.fullName);
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toHaveCount(0);
});

test('four misses uncover the whole card, the fifth loses and shows the answer', async ({ page }) => {
  for (const player of wrong.slice(0, 4)) await guessPlayer(page, player.fullName);
  await expect(page.getByRole('list', { name: 'What each miss uncovers' }).locator('li.open')).toHaveCount(4);
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeVisible();
  await guessPlayer(page, wrong[4].fullName);

  const result = page.locator('.result');
  await expect(result).toContainText('The answer was');
  await expect(result.getByText(answer.fullName, { exact: true })).toBeVisible();
  await expect(page.getByRole('list', { name: 'What each miss uncovers' }).locator('li.open')).toHaveCount(4);
  await expect(page.locator('.back .fact')).not.toHaveClass(/locked/);
  await expect(page.getByRole('list', { name: 'Wrong guesses, newest first' }).getByRole('listitem')).toHaveCount(5);
});
