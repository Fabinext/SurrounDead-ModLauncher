// Writes launcher.ico (16/32/48/64 px, 32-bit) from the pixel radio tower used in the launcher.
const fs = require('fs'), path = require('path');
const T = ["....##......","...#..#.....","....##......","....##......","...#..#.....","...#..#.....",
           "..######....","..#....#....",".#......#...",".########...","#........#..","##########.."];
const BG = [20, 20, 20, 255], FG = [0, 169, 242, 255]; // BGRA (amber #F2A900)
function image(size) {
  const s = Math.max(1, Math.floor((size - 2) / 12)), off = Math.floor((size - 12 * s) / 2) + Math.floor(s / 2);
  const px = Buffer.alloc(size * size * 4);
  for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
    const sx = Math.floor((x - off) / s), sy = Math.floor((y - off) / s);
    const on = sx >= 0 && sy >= 0 && sx < 12 && sy < 12 && T[sy][sx] === '#';
    const c = on ? FG : BG, row = size - 1 - y, i = (row * size + x) * 4;
    px[i] = c[0]; px[i + 1] = c[1]; px[i + 2] = c[2]; px[i + 3] = c[3];
  }
  const maskRow = Math.ceil(size / 32) * 4, mask = Buffer.alloc(maskRow * size);
  const hdr = Buffer.alloc(40);
  hdr.writeUInt32LE(40, 0); hdr.writeInt32LE(size, 4); hdr.writeInt32LE(size * 2, 8);
  hdr.writeUInt16LE(1, 12); hdr.writeUInt16LE(32, 14); hdr.writeUInt32LE(px.length + mask.length, 20);
  return Buffer.concat([hdr, px, mask]);
}
const sizes = [16, 32, 48, 64], imgs = sizes.map(image);
const head = Buffer.alloc(6 + 16 * sizes.length);
head.writeUInt16LE(0, 0); head.writeUInt16LE(1, 2); head.writeUInt16LE(sizes.length, 4);
let off = head.length;
sizes.forEach((s, k) => {
  const e = 6 + 16 * k;
  head[e] = s; head[e + 1] = s; head[e + 2] = 0; head[e + 3] = 0;
  head.writeUInt16LE(1, e + 4); head.writeUInt16LE(32, e + 6);
  head.writeUInt32LE(imgs[k].length, e + 8); head.writeUInt32LE(off, e + 12); off += imgs[k].length;
});
fs.writeFileSync(path.join(__dirname, 'launcher.ico'), Buffer.concat([head, ...imgs]));
console.log('launcher.ico written');
