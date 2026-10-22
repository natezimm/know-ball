// Run from any directory. Uses the existing web app's Playwright dependency.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve, extname, sep } from 'node:path';
const require = createRequire(new URL('../../../web/know-ball/package.json', import.meta.url));
const { chromium } = require(process.env.PROTOTYPE_PLAYWRIGHT_MODULE || '@playwright/test');
const root = fileURLToPath(new URL('../../../', import.meta.url));
const server = createServer(async (req, res) => {
  const path = resolve(root, '.' + decodeURIComponent(new URL(req.url, 'http://localhost').pathname));
  if (!path.startsWith(root.endsWith(sep) ? root : root + sep)) { res.writeHead(403).end(); return; }
  try {
    const body = await readFile(path);
    res.setHeader('Content-Type', ({ '.html':'text/html', '.css':'text/css', '.mjs':'text/javascript', '.json':'application/json', '.woff2':'font/woff2' })[extname(path)] || 'application/octet-stream');
    res.end(body);
  } catch { res.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const url = `http://127.0.0.1:${server.address().port}/docs/prototypes/numbers-dont-lie/index.html`;
let browser;
try {
  browser = await chromium.launch({ headless:true, executablePath:process.env.PROTOTYPE_CHROMIUM || undefined, args:['--no-sandbox'] });
  const page = await browser.newPage();
  const errors = []; page.on('pageerror', e => errors.push(e.message));
  await page.goto(url); await page.getByRole('button', { name:'Play daily preview', exact:true }).waitFor();
  await page.evaluate(() => document.fonts.ready);
  assert.ok(await page.evaluate(() => document.fonts.check('400 30px Alfa')));
  await page.getByRole('button', { name:'Play daily preview', exact:true }).click();
  const firstPrompt = await page.locator('h2').textContent();
  assert.equal(await page.getByRole('button', { name:'Lock in answer' }).isDisabled(), true);
  assert.equal(await page.locator('.feedback').count(), 0);
  for (let i = 0; i < 5; i++) {
    await page.locator(`input[value="${i === 1 ? 'p1' : 'p0'}"]`).check();
    await page.getByRole('button', { name:'Lock in answer' }).click();
    assert.equal(await page.locator('input:disabled').count(), 4);
    assert.equal(await page.locator('.feedback .values li').count(), 4);
    await page.getByRole('button', { name:i === 4 ? 'See results' : 'Next question' }).click();
  }
  assert.equal(await page.locator('.score').textContent(), '4 / 5');
  assert.equal(await page.locator('details').count(), 5);
  await page.getByRole('button', { name:'Choose practice leagues' }).click();
  for (const check of await page.getByRole('checkbox').all()) await check.uncheck();
  await page.getByRole('button', { name:'Start practice' }).click();
  assert.match(await page.locator('#error').textContent(), /Choose at least one/);
  await page.getByRole('checkbox', { name:'NHL', exact:true }).check();
  await page.reload();
  await page.getByRole('button', { name:'Practice', exact:true }).click();
  assert.equal(await page.locator('input:checked').count(), 1);
  assert.equal(await page.getByRole('checkbox', { name:'NHL', exact:true }).isChecked(), true);
  await page.getByRole('button', { name:'Start practice' }).click();
  for (let i = 0; i < 5; i++) {
    assert.match(await page.locator('.meta').nth(0).textContent(), /Practice · NHL/);
    await page.locator('input[value="p0"]').check();
    await page.getByRole('button', { name:'Lock in answer' }).click();
    if (i === 0) {
      page.once('dialog', dialog => dialog.dismiss());
      await page.getByRole('button', { name:'Daily preview', exact:true }).click();
      assert.equal(await page.locator('.feedback').count(), 1);
    }
    await page.getByRole('button', { name:i === 4 ? 'See results' : 'Next question' }).click();
  }
  assert.equal(await page.locator('.score').textContent(), '5 / 5');
  await page.getByRole('button', { name:'Daily preview', exact:true }).click();
  await page.getByRole('button', { name:'Play daily preview', exact:true }).click();
  assert.equal(await page.locator('h2').textContent(), firstPrompt);
  for (const width of [320, 390, 1280]) {
    await page.setViewportSize({ width, height:900 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  }
  await page.setViewportSize({ width:390, height:844 });
  if (process.env.PROTOTYPE_SCREENSHOT) await page.screenshot({ path:process.env.PROTOTYPE_SCREENSHOT, fullPage:true });
  // Enlarge actual rendered text (the design uses px, so changing root rem alone is not a zoom check).
  await page.evaluate(() => {
    const elements = [...document.querySelectorAll('body *')];
    const sizes = elements.map(el => parseFloat(getComputedStyle(el).fontSize));
    elements.forEach((el, i) => el.style.fontSize = `${sizes[i] * 2}px`);
  });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  const blocked = await browser.newContext();
  await blocked.addInitScript(() => Object.defineProperty(window, 'localStorage', { get() { throw new Error('blocked'); } }));
  const blockedPage = await blocked.newPage();
  await blockedPage.goto(url);
  await blockedPage.getByRole('button', { name:'Practice', exact:true }).click();
  assert.equal(await blockedPage.locator('input:checked').count(), 4);
  assert.deepEqual(errors, []);
  console.log('PASS: daily 4/5, practice 5/5, feedback/review, empty selection, preferences, blocked storage, abandon cancel, daily independence and 320/390/1280px layouts.');
} finally { await browser?.close(); server.close(); }
