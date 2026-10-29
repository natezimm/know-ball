import { expect, test } from './support';

test('fourth game opens and saves daily feedback across reloads', async ({ page }) => {
  // Keep the answered radio visible briefly after Next, reproducing the CI race.
  await page.route('**/api/numbers-dont-lie/quizzes/*/continue', async route => {
    const response = await route.fetch();
    await new Promise(resolve => setTimeout(resolve, 150));
    await route.fulfill({ response });
  });
  await page.setViewportSize({width:390,height:844});
  await page.goto('/');
  await expect(page.getByRole('list',{name:'Games'}).getByRole('heading',{level:2})).toHaveCount(4);
  await page.getByRole('link',{name:"Numbers Don't Lie: Play beta"}).click();
  await expect(page.getByRole('heading',{name:"Numbers Don't Lie",exact:true})).toBeVisible();
  for(let i=0;i<5;i++) {
    await expect(page.getByText(new RegExp(`Question ${i + 1} of 5$`))).toBeVisible();
    await expect(page.getByRole('radio').first()).toBeEnabled();
    await page.getByRole('radio').first().check();
    await page.getByRole('button',{name:'Lock in answer'}).click();
    await expect(page.locator('.feedback')).toBeVisible();
    await page.reload();
    await expect(page.locator('.feedback')).toBeVisible();
    await page.getByRole('button',{name:i===4?'See results':'Next question'}).click();
  }
  await expect(page.locator('.score')).toBeVisible();
  await page.reload();
  await expect(page.locator('.score')).toBeVisible();
  await page.getByRole('button',{name:'Customize practice'}).click();
  for(const name of ['NFL','MLB','NHL']) await page.getByRole('checkbox',{name,exact:true}).uncheck();
  await page.getByRole('button',{name:'Start practice'}).click();
  await expect(page.getByText(/NBA · .*Question 1 of 5/)).toBeVisible();
  await page.reload();
  await expect(page.getByText(/NBA · .*Question 1 of 5/)).toBeVisible();
  expect(await page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('old preview URL redirects to the server-backed game', async ({page,request})=>{
  await page.goto('/preview/numbers-dont-lie/index.html?ngsw-bypass=true');
  await expect(page).toHaveURL(/numbers-dont-lie\?round=/);
  const response=await request.get('/preview/numbers-dont-lie/questions.json');
  expect(response.status()).toBe(404);
});

test('practice keeps topic and year filters on reload and makes exhausted replay explicit', async ({ page }) => {
  await page.goto('/numbers-dont-lie');
  await expect(page.getByRole('button', { name: 'Practice', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Practice', exact: true }).click();
  for (const name of ['NFL', 'MLB', 'NHL']) await page.getByRole('checkbox', { name, exact: true }).uncheck();
  await page.getByText('Customize topics and years', { exact: true }).click();
  for (const name of ['awards', 'teams', 'seasons']) await page.getByRole('checkbox', { name, exact: true }).uncheck();
  await page.getByLabel('From season year').fill('2023');
  await page.getByLabel('Through season year').fill('2023');
  await expect(page.locator('.filter-summary')).toHaveText('NBA · stats · 2023–2023');
  await expect(page.getByText(/fresh facts \/ 6 matching facts/)).toBeVisible();
  const repeat = page.getByRole('checkbox', { name: 'Allow previously included facts for this round' });
  if (await repeat.isVisible()) await repeat.check(); // Today's shared quiz may reserve one of these facts.
  await page.getByRole('button', { name: 'Start practice', exact: true }).click();
  await expect(page.getByText(/NBA · .*Question 1 of 5/)).toBeVisible();
  await page.reload();
  await expect(page.locator('.filter-summary')).toHaveText('NBA · stats · 2023–2023');
  for (let i = 0; i < 5; i++) {
    await expect(page.getByText(new RegExp(`Question ${i + 1} of 5$`))).toBeVisible();
    await expect(page.getByRole('radio').first()).toBeEnabled();
    await page.getByRole('radio').first().check();
    await page.getByRole('button', { name: 'Lock in answer' }).click();
    await expect(page.locator('.feedback')).toBeVisible();
    await page.getByRole('button', { name: i === 4 ? 'See results' : 'Next question' }).click();
  }
  await expect(page.locator('.score')).toBeVisible();
  await page.getByRole('button', { name: 'Play another round' }).click();
  await expect(page.getByRole('heading', { name: 'Choose your practice' })).toBeVisible();
  await expect(page.locator('.filter-summary')).toHaveText('NBA · stats · 2023–2023');
  await expect(page.getByRole('button', { name: 'Start practice', exact: true })).toBeDisabled();
  await expect(repeat).not.toBeChecked();
  await repeat.check();
  const started = page.waitForResponse(r => r.url().endsWith('/practice') && r.request().method() === 'POST');
  await page.getByRole('button', { name: 'Start practice', exact: true }).click();
  expect((await started).request().postDataJSON()).toMatchObject({ leagues: ['nba'], topics: ['stats'], fromYear: 2023, throughYear: 2023, allowRepeats: true });
  await expect(page.getByText(/NBA · .*Question 1 of 5/)).toBeVisible();
});
