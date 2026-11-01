// Rebuild the committed Open Graph image with the site's bundled fonts.
// Run from web/know-ball: npm run social-card (install Playwright Chromium first).
import { chromium } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

const font = async (packageName, file) => (await readFile(new URL(`../node_modules/@fontsource/${packageName}/files/${file}`, import.meta.url))).toString('base64');
const [display, script, body] = await Promise.all([
  font('alfa-slab-one', 'alfa-slab-one-latin-400-normal.woff2'),
  font('yellowtail', 'yellowtail-latin-400-normal.woff2'),
  font('rokkitt', 'rokkitt-latin-700-normal.woff2'),
]);
const browser = await chromium.launch(process.env.E2E_CHROMIUM ? { executablePath: process.env.E2E_CHROMIUM } : {});
try {
  const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
  await page.setContent(`<!doctype html><html lang="en"><meta charset="utf-8"><style>
    @font-face{font-family:Display;src:url(data:font/woff2;base64,${display})}
    @font-face{font-family:Script;src:url(data:font/woff2;base64,${script})}
    @font-face{font-family:Body;src:url(data:font/woff2;base64,${body})}
    *{box-sizing:border-box}body{margin:0;width:1200px;height:630px;color:#1d2b53;background:#f6ecd2 radial-gradient(#1d2b5312 1.2px,transparent 1.8px) 0 0/6px 6px;font-family:Body}
    header{height:100px;background:#1d2b53;color:#f6ecd2;border-bottom:8px solid #b92d22;padding:25px 52px;font:30px Display;letter-spacing:2px}
    header span{color:#e0a526;margin-right:14px}main{padding:38px 52px}.kicker{margin:0 0 12px;color:#b92d22;letter-spacing:5px;font-size:21px;text-transform:uppercase}
    h1{font:48px/1.18 Display;margin:0}h1 em{display:inline-block;font:86px/1.1 Script;color:#b92d22;text-shadow:2px 2px #f6ecd2,4px 4px #1d2b53;transform:rotate(-3deg);font-style:normal;margin-top:4px}
    .games{display:flex;gap:16px;margin-top:28px}.game{flex:1;border:5px solid var(--trim);border-radius:7px;padding:15px 12px;background:#fbf5e2;box-shadow:inset 0 0 0 2px #f6ecd2,inset 0 0 0 4px #1d2b53;font:20px Display;text-align:center;white-space:nowrap}
    .url{margin:20px 0 0;font-size:19px;color:#4f587a;letter-spacing:1px}
  </style><header><span>★</span>KNOW BALL</header><main><p class="kicker">Daily sports trivia</p><h1>Four ways to prove you<br><em>know ball.</em></h1><div class="games"><div class="game" style="--trim:#b92d22">Spordle</div><div class="game" style="--trim:#1f6f6b">Scouting Report</div><div class="game" style="--trim:#e0a526">Career Path</div><div class="game" style="--trim:#1f6f6b">Numbers Don't Lie</div></div><p class="url">knowball.nathanzimmerman.com</p></main></html>`);
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: fileURLToPath(new URL('../public/social-card.png', import.meta.url)) });
} finally {
  await browser.close();
}
