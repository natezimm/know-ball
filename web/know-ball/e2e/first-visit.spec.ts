import { expect, test } from './support';

test.use({ firstVisit: true });

test('a newcomer sees How to play once on each daily game, and not on practice', async ({ page }) => {
  await page.goto('/career-path');
  const help = page.getByRole('dialog', { name: 'How to play' });
  await expect(help).toBeVisible();
  await help.getByRole('button', { name: 'Close' }).click();
  await expect(help).toBeHidden();

  // Seen once: neither a reload nor the other edition opens it again.
  await page.reload();
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeVisible();
  await expect(help).toBeHidden();

  // Each game introduces itself the first time.
  await page.goto('/numbers-dont-lie');
  await expect(help).toBeVisible();
  await page.keyboard.press('Escape');
  await page.goto('/letter-jacket');
  await expect(help).toBeVisible();
  await page.keyboard.press('Escape');
  await page.reload();
  await expect(page.getByRole('status').filter({ hasText: 'Guess 1 of 6' })).toBeVisible();
  await expect(help).toBeHidden();
});

test('someone who already plays here is not interrupted', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('knowball.anonId', 'e2e-returning-player-1'));
  await page.goto('/team-photo');
  await expect(page.getByRole('combobox')).toBeVisible();
  await expect(page.getByRole('dialog', { name: 'How to play' })).toBeHidden();
});
