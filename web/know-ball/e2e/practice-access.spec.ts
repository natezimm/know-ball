import { expect, guessPlayer, learnDaily, players, test } from './support';

for (const game of ['scouting-report', 'career-path'] as const) {
  test(`${game} offers practice before completion and preserves the daily board`, async ({ page, request }) => {
    const { wrong } = await learnDaily(request, game, await players(request, game === 'career-path'));
    await page.goto('/' + game);
    await expect(page.locator('.meta')).toContainText('No. 1');
    await page.getByRole('button', { name: 'Practice round', exact: true }).click();
    await expect(page.locator('.meta')).toContainText('Practice round');
    await page.getByRole('link', { name: "Today's puzzle", exact: true }).click();
    await expect(page.locator('.meta')).toContainText('No. 1');
    await guessPlayer(page, wrong[0].fullName);
    await expect(page.getByRole('img', { name: /1 of \d+ guesses used/ })).toBeVisible();
    await page.getByRole('button', { name: 'Practice round', exact: true }).click();
    await expect(page.locator('.meta')).toContainText('Practice round');
    await expect(page.getByRole('img', { name: /0 of \d+ guesses used/ })).toBeVisible();
    await page.getByRole('link', { name: "Today's puzzle", exact: true }).click();
    await expect(page.getByRole('img', { name: /1 of \d+ guesses used/ })).toBeVisible();
    await page.reload();
    await expect(page.getByRole('img', { name: /1 of \d+ guesses used/ })).toBeVisible();
  });
}
