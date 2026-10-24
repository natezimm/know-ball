import { expect, test } from './support';

test('the deployed preview opens from the footer and plays daily and selected-league practice', async ({ page, request }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');
  await page.getByRole('contentinfo').getByRole('link', { name: "Numbers Don't Lie preview" }).click();
  await expect(page).toHaveURL(/\/preview\/numbers-dont-lie\/index\.html\?ngsw-bypass=true$/);
  await expect(page.getByText('Design preview', { exact:true })).toBeVisible();
  await expect(page.locator('meta[name="robots"]')).toHaveAttribute('content', 'noindex, nofollow');
  await expect(page.getByRole('link', { name:'Back to Know Ball games' })).toHaveAttribute('href', '/');
  await page.getByRole('button', { name:'Play daily preview', exact:true }).click();
  for (let index = 0; index < 5; index++) {
    await page.locator(`input[value="${index === 1 ? 'p1' : 'p0'}"]`).check();
    await page.getByRole('button', { name:'Lock in answer' }).click();
    await expect(page.locator('.feedback .source a').first()).toBeVisible();
    await page.getByRole('button', { name:index === 4 ? 'See results' : 'Next question' }).click();
  }
  await expect(page.locator('.score')).toHaveText('4 / 5');
  await page.getByRole('button', { name:'Customize practice' }).click();
  for (const name of ['NBA', 'MLB']) await page.getByRole('checkbox', { name, exact:true }).uncheck();
  await page.getByRole('button', { name:'Start practice' }).click();
  const leagues: string[] = [];
  for (let index = 0; index < 5; index++) {
    const label = await page.locator('.meta').first().textContent();
    expect(label).toMatch(/^Practice · (NFL|NHL)/);
    leagues.push(label!.includes('NFL') ? 'NFL' : 'NHL');
    await page.locator('input[value="p0"]').check();
    await page.getByRole('button', { name:'Lock in answer' }).click();
    await page.getByRole('button', { name:index === 4 ? 'See results' : 'Next question' }).click();
  }
  expect(new Set(leagues).size).toBe(2);
  await expect(page.locator('.score')).toHaveText('5 / 5');
  await page.reload();
  await expect(page.getByRole('button', { name:'View today’s result', exact:true })).toBeVisible();
  await page.getByRole('button', { name:'Practice', exact:true }).click();
  await expect(page.locator('input[name="league"]:checked')).toHaveCount(2);
  for (const name of ['NFL', 'NHL']) await expect(page.getByRole('checkbox', { name, exact:true })).toBeChecked();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  // Shipped assets must be real static resources, not the Angular HTML fallback.
  for (const path of ['fonts/rokkitt-latin-500-normal.woff2', 'fonts/alfa-slab-one-latin-400-normal.woff2']) {
    const response = await request.get(`/preview/numbers-dont-lie/${path}`);
    expect(response.ok()).toBe(true);
    expect(response.headers()['content-type']).toContain('font/woff2');
  }
  const manifest = await (await request.get('/ngsw.json')).json();
  expect(Object.keys(manifest.hashTable).some(path => path.startsWith('/preview/'))).toBe(false);
});
