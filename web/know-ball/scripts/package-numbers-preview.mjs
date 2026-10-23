// Explicitly publish the review sample, not the future production question catalogue.
// Keep source files in docs so the local and hosted review use the same implementation.
import { readFile, writeFile, mkdir, copyFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';

const source = new URL('../../../docs/prototypes/numbers-dont-lie/', import.meta.url);
const output = new URL('../dist/know-ball/browser/preview/numbers-dont-lie/', import.meta.url);
const fonts = new URL('../node_modules/@fontsource/', import.meta.url);
const read = name => readFile(new URL(name, source), 'utf8');
const digest = text => createHash('sha256').update(text).digest('hex').slice(0, 12);
const publish = async (stem, extension, content) => {
  const name = `${stem}.${digest(content)}.${extension}`;
  await writeFile(new URL(name, output), content);
  return name;
};
await mkdir(new URL('fonts/', output), { recursive:true });
const fontFiles = [
  ['rokkitt', 'rokkitt-latin-500-normal.woff2'],
  ['rokkitt', 'rokkitt-latin-700-normal.woff2'],
  ['alfa-slab-one', 'alfa-slab-one-latin-400-normal.woff2'],
  ['yellowtail', 'yellowtail-latin-400-normal.woff2'],
];
for (const [family, name] of fontFiles) {
  await copyFile(new URL(`${family}/files/${name}`, fonts), new URL(`fonts/${name}`, output));
  await copyFile(new URL(`${family}/LICENSE`, fonts), new URL(`fonts/${family}-LICENSE.txt`, output));
}
let css = await read('style.css');
css = css.replace(/\.\.\/\.\.\/\.\.\/web\/know-ball\/node_modules\/@fontsource\/[^/]+\/files\//g, './fonts/');
const style = await publish('style', 'css', css);
const model = await publish('model', 'js', await read('model.mjs'));
const questions = await publish('questions', 'json', await read('questions.json'));
let app = await read('app.mjs');
app = app.replace("'./model.mjs'", `'./${model}'`).replace("'./questions.json'", `'./${questions}'`);
app = app.replace('Serve the repository over HTTP using the instructions in the prototype README, then reload.', 'Check your connection and reload the preview.');
const script = await publish('app', 'js', app);
let html = await read('index.html');
html = html.replace('href="style.css"', `href="${style}"`).replace('src="app.mjs"', `src="${script}"`);
html = html.replace('href="../../../README.md"', 'href="/" aria-label="Back to Know Ball games"');
html = html.replace('href="../../numbers-dont-lie-data-sources.md"', 'href="https://github.com/natezimm/know-ball/blob/main/docs/numbers-dont-lie-data-sources.md"');
await writeFile(new URL('index.html', output), html);
console.log('Packaged Numbers Don’t Lie preview at /preview/numbers-dont-lie/index.html');
