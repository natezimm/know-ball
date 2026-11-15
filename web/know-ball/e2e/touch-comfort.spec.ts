import { Locator, Page } from '@playwright/test';
import { expect, test } from './support';

test.use({ viewport: { width: 390, height: 700 }, hasTouch: true });

/**
 * Whether a tap up to 22px from the control's centre, in each direction, still lands on it. The
 * control may look smaller than that; its touch area is what counts.
 */
async function takesTaps(control: Locator): Promise<boolean> {
  return control.evaluate((el) => {
    const box = el.getBoundingClientRect();
    const [x, y] = [box.left + box.width / 2, box.top + box.height / 2];
    return [[0, -21], [0, 21], [-21, 0], [21, 0]].every(([dx, dy]) => {
      const hit = document.elementFromPoint(x + dx, y + dy);
      return hit === el || el.contains(hit);
    });
  });
}

/** The smallest text on the page, leaving out the initials drawn inside a round avatar in place of a headshot. */
async function smallestText(page: Page): Promise<number> {
  return page.evaluate(() => {
    let smallest = Infinity;
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) {
      const el = walker.currentNode.parentElement!;
      if (!walker.currentNode.textContent!.trim() || el.closest('app-avatar, .visually-hidden, script, style')) continue;
      const box = el.getBoundingClientRect();
      if (box.width && box.height) smallest = Math.min(smallest, parseFloat(getComputedStyle(el).fontSize));
    }
    return smallest;
  });
}

test('small controls in a player game take a full-size tap, and nothing is set below 13px', async ({ page }) => {
  await page.goto('/scouting-report');
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeVisible();
  if (await page.locator('dialog[open]').count()) await page.keyboard.press('Escape');

  for (const name of ['How to play', 'Statistics', 'Past puzzles']) {
    expect(await takesTaps(page.getByRole('button', { name, exact: true })), name).toBe(true);
  }
  expect(await takesTaps(page.getByRole('navigation', { name: 'League' }).getByRole('link', { name: 'NFL' })), 'NFL tab').toBe(true);
  expect(await smallestText(page)).toBeGreaterThanOrEqual(13);

  await page.getByRole('button', { name: 'Past puzzles' }).click();
  const dialog = page.getByRole('dialog', { name: 'Past puzzles' });
  expect(await takesTaps(dialog.getByRole('button', { name: 'Close' })), 'Close').toBe(true);
  // The practice button sits above its explanation, which keeps the full width of the box.
  const button = (await dialog.getByRole('button', { name: 'Practice round' }).boundingBox())!;
  const note = (await dialog.locator('.practice span').boundingBox())!;
  expect(note.y).toBeGreaterThanOrEqual(button.y + button.height);
  expect(await smallestText(page)).toBeGreaterThanOrEqual(13);
});

test('Letter Jacket word lengths and hints, and the home page, take a full-size tap', async ({ page }) => {
  await page.goto('/letter-jacket');
  await expect(page.getByRole('status').filter({ hasText: 'Guess 1 of 6' })).toBeVisible();
  for (const name of ['4 letters', '5 letters', '6 letters', 'Category', 'Letter', 'How to play', 'Stats', 'New game']) {
    expect(await takesTaps(page.getByRole('button', { name, exact: true })), name).toBe(true);
  }
  expect(await smallestText(page)).toBeGreaterThanOrEqual(13);

  await page.goto('/');
  const nfl = page.getByRole('link', { name: /^Scouting Report NFL/ });
  // Mid-screen, so the area above and below the badge is on screen too.
  await nfl.evaluate((el) => el.scrollIntoView({ block: 'center' }));
  expect(await takesTaps(nfl), 'NFL badge').toBe(true);
  expect(await smallestText(page)).toBeGreaterThanOrEqual(13);
});

test("the quiz names its rounds the same way throughout", async ({ page }) => {
  // While today's quiz is loading the header must not call it practice.
  await page.route('**/api/numbers-dont-lie/daily', async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 400));
    await route.continue();
  });
  await page.goto('/numbers-dont-lie');
  await expect(page.locator('.kb-meta .kb-label')).toHaveText("Today's quiz");
  await expect(page.locator('.kb-meta .kb-label')).toHaveText(/Daily ·/);
  expect(await smallestText(page)).toBeGreaterThanOrEqual(13);
});
