import { expect, guessPlayer, learnDaily, players, test } from './support';

for (const game of ['scouting-report', 'career-path', 'back-of-the-card'] as const) {
  test(`${game} offers practice from the archive before completion and preserves the daily board`, async ({ page, request }) => {
    const { wrong } = await learnDaily(request, game, await players(request, game !== 'scouting-report'));
    await page.goto('/' + game);
    await expect(page.locator('.meta')).toContainText('No. 1');
    await page.getByRole('button', { name: 'Past puzzles' }).click();
    await page.getByRole('dialog', { name: 'Past puzzles' }).getByRole('button', { name: 'Practice round', exact: true }).click();
    await expect(page.locator('.meta')).toContainText('Practice round');
    await page.getByRole('link', { name: "Today's puzzle", exact: true }).click();
    await expect(page.locator('.meta')).toContainText('No. 1');
    await guessPlayer(page, wrong[0].fullName);
    await expect(page.getByRole('img', { name: /1 of \d+ guesses used/ })).toBeVisible();
    await page.getByRole('button', { name: 'Past puzzles' }).click();
    await page.getByRole('dialog', { name: 'Past puzzles' }).getByRole('button', { name: 'Practice round', exact: true }).click();
    await expect(page.locator('.meta')).toContainText('Practice round');
    await expect(page.getByRole('img', { name: /0 of \d+ guesses used/ })).toBeVisible();
    await page.getByRole('link', { name: "Today's puzzle", exact: true }).click();
    await expect(page.getByRole('img', { name: /1 of \d+ guesses used/ })).toBeVisible();
    await page.reload();
    await expect(page.getByRole('img', { name: /1 of \d+ guesses used/ })).toBeVisible();
  });
}
