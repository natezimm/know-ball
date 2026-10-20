import { expect, test } from './support';

const missing = "There is no Who's That Sportsman? puzzle today.";
const today = [
  { game: 'scouting-report', name: 'Scouting Report', available: true, number: 4, status: 'new', guesses: 0, maxGuesses: 8 },
  { game: 'career-path', name: 'Career Path', available: true, number: 4, status: 'new', guesses: 0, maxGuesses: 6 },
  { game: 'whos-that', name: "Who's That Sportsman?", available: false, number: null, status: 'new', guesses: 0, maxGuesses: 5 },
];

test('daily cards wait for availability and explain a day without a puzzle', async ({ page }) => {
  let release!: () => void;
  const pending = new Promise<void>((resolve) => { release = resolve; });
  await page.route('**/api/me/today', async (route) => {
    await pending;
    await route.fulfill({ json: today });
  });
  await page.goto('/');
  await expect(page.getByText('Checking today…', { exact: true })).toHaveCount(3);
  await expect(page.getByRole('link', { name: /^Who's That Sportsman/ })).toHaveCount(0);
  release();
  await expect(page.getByText('No puzzle today', { exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Scouting Report: Play No. 4' })).toBeVisible();
  await expect(page.getByRole('link', { name: /^Who's That Sportsman/ })).toHaveCount(0);
});

test('a gateway timeout offers retry; a missing daily explains availability', async ({ page }) => {
  let requests = 0;
  await page.route('**/api/whos-that/daily', (route) => {
    requests++;
    return requests === 1
      ? route.fulfill({ status: 504, body: 'Gateway Timeout' })
      : route.fulfill({ status: 404, json: { error: missing } });
  });
  await page.route('**/api/me/today', (route) => route.fulfill({ json: today }));
  await page.goto('/whos-that');
  await expect(page.getByRole('alert')).toContainText('The puzzle could not be loaded.');
  await page.getByRole('button', { name: 'Try again' }).click();
  await expect(page.getByText(missing, { exact: true })).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);
  await page.getByRole('link', { name: 'All games', exact: true }).click();
  await expect(page.getByText('No puzzle today', { exact: true })).toBeVisible();
});

for (const width of [390, 1280]) {
  test(`game titles clear the banner at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 });
    await page.route('**/api/**', (route) => route.fulfill({ status: 404, json: { error: missing } }));
    for (const slug of ['scouting-report', 'career-path', 'whos-that', 'spordle']) {
      await page.goto(`/${slug}`);
      await page.evaluate(() => document.fonts.ready);
      const banner = await page.locator('app-root > header').boundingBox();
      const title = await page.getByRole('heading', { level: 1 }).boundingBox();
      expect(banner).not.toBeNull();
      expect(title).not.toBeNull();
      // boundingBox includes the script title's rotation, which lifts its right edge.
      expect(title!.y - (banner!.y + banner!.height), slug).toBeGreaterThanOrEqual(12);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), slug).toBe(true);
    }
  });
}
