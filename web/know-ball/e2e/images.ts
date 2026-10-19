import { crc32, deflateSync } from 'node:zlib';

/** An orange figure on blue, or on a clear background; optional portrait dimensions. */
export function png(clear: boolean, width = 200, height = 200): Buffer {
  const rows = Buffer.alloc(height * (width * 4 + 1));
  for (let y = 0, o = 0; y < height; y++) {
    rows[o++] = 0;
    for (let x = 0; x < width; x++, o += 4) {
      const inside = x >= width * .3 && x < width * .7 && y >= height * .15 && y < height * .9;
      rows.set(inside ? [220, 80, 40, 255] : clear ? [0, 0, 0, 0] : [70, 130, 180, 255], o);
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header.set([8, 6, 0, 0, 0], 8);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(rows)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

function chunk(type: string, data: Buffer): Buffer {
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const out = Buffer.alloc(body.length + 8);
  out.writeUInt32BE(data.length, 0);
  body.copy(out, 4);
  out.writeUInt32BE(crc32(body), body.length + 4);
  return out;
}
