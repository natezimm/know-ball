import { expect, test } from './support';

test('portfolio navigation, metadata and social preview describe the three-game site', async ({ page, request }) => {
  await page.goto('/');
  await expect(page.getByRole('navigation', { name: 'Main', exact: true }).getByRole('link')).toHaveText(['Games']);
  const footer = page.getByRole('contentinfo');
  await expect(footer.getByRole('link', { name: 'Nathan Zimmerman' })).toHaveAttribute('href', 'https://www.nathanzimmerman.com/');
  await expect(footer.getByRole('link', { name: 'GitHub' })).toHaveAttribute('href', 'https://github.com/natezimm/know-ball');
  await expect(footer.getByRole('link', { name: 'Design Lab' })).toHaveAttribute('href', '/mockups');
  await expect(page.locator('meta[name="description"]')).not.toHaveAttribute('content', /photo/);
  await expect(page.locator('meta[property="og:image"]')).toHaveAttribute('content', 'https://knowball.nathanzimmerman.com/social-card.png');
  const image = await request.get('/social-card.png');
  expect(image.ok()).toBe(true);
  expect(image.headers()['content-type']).toContain('image/png');
  const bytes = await image.body();
  expect(bytes.readUInt32BE(16)).toBe(1200);
  expect(bytes.readUInt32BE(20)).toBe(630);
});

test('the first archive entry opens its explicit puzzle even from the daily URL', async ({ page }) => {
  const daily = page.waitForResponse((r) => r.url().endsWith('/api/scouting-report/daily'));
  await page.goto('/scouting-report');
  const state = await (await daily).json();
  await page.route('**/api/scouting-report/archive', (route) => route.fulfill({ json: [
    { puzzleId: state.puzzleId, number: state.number, date: state.date, status: 'new', guesses: 0 },
  ] }));
  await page.getByRole('button', { name: 'Past puzzles' }).click();
  const puzzle = page.waitForResponse((r) => r.url().endsWith(`/api/scouting-report/puzzles/${state.puzzleId}`));
  await page.getByRole('dialog', { name: 'Past puzzles' }).getByRole('link').first().click();
  await expect(page).toHaveURL(new RegExp(`/scouting-report/puzzle/${state.puzzleId}$`));
  expect((await puzzle).ok()).toBe(true);
  await expect(page.getByRole('combobox', { name: 'Guess a player' })).toBeVisible();
});

test('a small visual viewport keeps the search results above the keyboard', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.addInitScript(() => {
    const viewport = Object.assign(new EventTarget(), { height: innerHeight, offsetTop: 0, scale: 1 });
    Object.defineProperty(window, 'visualViewport', { value: viewport, configurable: true });
  });
  await page.goto('/scouting-report');
  const box = page.getByRole('combobox', { name: 'Guess a player' });
  await box.fill('j');
  await page.evaluate(() => {
    const viewport = window.visualViewport!;
    Object.assign(viewport, { height: 340 });
    viewport.dispatchEvent(new Event('resize'));
  });
  await box.fill('ja');
  await expect(page.getByRole('listbox')).toBeVisible();
  await expect.poll(async () => {
    const banner = (await page.locator('#site-header').boundingBox())!;
    const list = (await page.getByRole('listbox').boundingBox())!;
    const input = (await box.boundingBox())!;
    return input.y >= banner.y + banner.height && list.y >= input.y + input.height && list.y + list.height <= 340;
  }).toBe(true);
  await expect(page.getByRole('option').first()).toBeVisible();
});

test('unaccented searches find accented player names in both daily games', async ({ page }) => {
  for (const slug of ['scouting-report', 'career-path']) {
    await page.goto(`/${slug}`);
    const box = page.getByRole('combobox', { name: 'Guess a player' });
    for (const [query, name] of [['jokic', 'Nikola Jokić'], ['doncic', 'Luka Dončić']]) {
      await box.fill(query);
      await expect(page.getByRole('option').filter({ hasText: name })).toBeVisible();
    }
  }
});

for (const action of ['4 letters', 'New game']) {
  test(`Spordle confirms abandonment before ${action} and records the loss only when accepted`, async ({ page }) => {
    await page.goto('/spordle');
    await expect(page.locator('.status')).toHaveText('Guess 1 of 6');
    await page.keyboard.type('about');
    await page.keyboard.press('Enter');
    await expect(page.locator('.status')).toHaveText('Guess 2 of 6');
    page.once('dialog', async (dialog) => {
      expect(dialog.message()).toContain('count this one as a loss');
      await dialog.dismiss();
    });
    await page.getByRole('button', { name: action, exact: true }).click();
    await expect(page.locator('.status')).toHaveText('Guess 2 of 6');
    page.once('dialog', (dialog) => dialog.accept());
    await page.getByRole('button', { name: action, exact: true }).click();
    await expect(page.locator('.status')).toHaveText('Guess 1 of 6');
    expect(await page.evaluate(() => JSON.parse(localStorage.getItem('knowball.spordle.stats')!).byLength['5'])).toMatchObject({ totalGames: 1, wins: 0 });
  });
}

for (const endpoint of ['guesses', 'hint']) {
  test(`an expired Spordle ${endpoint} offers recovery without recording a loss`, async ({ page }) => {
    await page.goto('/spordle');
    await expect(page.locator('.status')).toHaveText('Guess 1 of 6');
    await page.route(`**/api/spordle/games/*/${endpoint}`, (route) => route.fulfill({ status: 404, json: { error: 'Game not found or expired' } }));
    if (endpoint === 'guesses') {
      await page.keyboard.type('about');
      await page.keyboard.press('Enter');
    } else {
      await page.getByRole('button', { name: 'Category', exact: true }).click();
      await page.getByRole('button', { name: 'Reveal category' }).click();
      await expect(page.locator('dialog[open]')).toHaveCount(0);
    }
    await expect(page.getByRole('alert')).toContainText('Your stats are unchanged');
    await page.getByRole('button', { name: 'Start a fresh game' }).click();
    await expect(page.locator('.status')).toHaveText('Guess 1 of 6');
    await expect(page.getByRole('alert')).toHaveCount(0);
    await page.getByRole('button', { name: 'Stats', exact: true }).click();
    await expect(page.getByRole('dialog', { name: /Statistics/ }).locator('.stats div', { hasText: 'Played' }).locator('dd')).toHaveText('0');
  });
}
