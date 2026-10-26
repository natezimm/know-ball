// Preserve old preview bookmarks without shipping the answer catalogue to browsers.
import { writeFile, mkdir, rm } from 'node:fs/promises';
const output = new URL('../dist/know-ball/browser/preview/numbers-dont-lie/', import.meta.url);
await rm(output, {recursive:true, force:true});
await mkdir(output, {recursive:true});
await writeFile(new URL('index.html', output), '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="robots" content="noindex"><meta http-equiv="refresh" content="0;url=/numbers-dont-lie"><title>Numbers Don’t Lie</title></head><body><a href="/numbers-dont-lie">Play Numbers Don’t Lie</a></body></html>');
