// Renders tools/bench --suite mapdump output with the host's real models, without Unity.
// Usage (from the repository root; simWorld.Host must be a sibling checkout):
//   node tools/maprender/render.js --map artifacts/maprender/map-777-day6.json --out day6.png
//   node tools/maprender/render.js --map ... --view zoom --cx 98 --cz 95 --r 20 --out close.png
//   node tools/maprender/render.js --gallery --out models.jpg
// Needs Playwright's Chromium (preinstalled in the cloud container) and three.js r160 in
// tools/maprender/vendor (run fetch-three.sh once). See README.md.
const http = require('http');
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => {
  if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1] && !all[i + 1].startsWith('--') ? all[i + 1] : 'true']);
  return acc;
}, []));
const here = __dirname;
const repo = path.resolve(here, '..', '..');
const host = path.resolve(args.host || path.join(repo, '..', 'simWorld.Host'));
const models = path.join(host, 'Assets', 'Resources', 'Models');
const palette = path.join(host, 'Assets', 'Art', 'Palette', 'Palette.png');
if (!fs.existsSync(models)) { console.error('no models at ' + models + ' (pass --host <simWorld.Host checkout>)'); process.exit(1); }
const mapFile = args.map ? path.resolve(args.map) : null;
const types = { '.html': 'text/html', '.js': 'text/javascript', '.json': 'application/json', '.png': 'image/png', '.fbx': 'application/octet-stream' };

const server = http.createServer((req, res) => {
  const url = decodeURIComponent(req.url.split('?')[0]);
  let file;
  if (url === '/list.json') {
    const list = fs.readdirSync(models).filter(n => n.endsWith('.fbx')).sort();
    res.writeHead(200, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(list)); return;
  }
  if (url === '/map.json' && mapFile) file = mapFile;
  else if (url === '/palette.png') file = palette;
  else if (url.startsWith('/models/')) file = path.join(models, path.basename(url));
  else file = path.join(here, path.normalize(url).replace(/^([/\\])+/, ''));
  if (!file.startsWith(here) && file !== mapFile && file !== palette && !file.startsWith(models)) { res.writeHead(403); res.end(); return; }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream' }); res.end(data);
  });
});

server.listen(0, '127.0.0.1', async () => {
  const port = server.address().port;
  const w = +(args.w || 1800), h = +(args.h || 1200);
  const q = new URLSearchParams();
  let page;
  if (args.gallery) { page = 'gallery.html'; if (args.only) q.set('only', args.only); q.set('cols', args.cols || '10'); }
  else {
    if (!mapFile) { console.error('pass --map <json> or --gallery'); process.exit(1); }
    page = 'map.html'; q.set('f', 'map.json'); q.set('w', w); q.set('h', h); q.set('view', args.view || 'full');
    for (const k of ['cx', 'cz', 'r', 'pitch', 'yaw', 'seed', 'note']) if (args[k]) q.set(k, args[k]);
  }
  const executablePath = args.chromium || (fs.existsSync('/opt/pw-browsers/chromium-1194/chrome-linux/chrome') ? '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' : undefined);
  const browser = await chromium.launch({ executablePath, args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  try {
    const tab = await browser.newPage({ viewport: { width: w, height: h } });
    tab.on('pageerror', e => console.error('page error: ' + e.message));
    await tab.goto(`http://127.0.0.1:${port}/${page}?${q}`);
    await tab.waitForFunction('window.__done === true', null, { timeout: 600000 });
    const info = await tab.evaluate('window.__info || window.__failed');
    if (info) console.log(JSON.stringify(info));
    const out = path.resolve(args.out || 'render.png');
    const jpeg = /\.jpe?g$/i.test(out);
    await tab.screenshot({ path: out, fullPage: !!args.gallery, type: jpeg ? 'jpeg' : 'png', ...(jpeg ? { quality: 85 } : {}) });
    console.log('wrote ' + out);
  } finally { await browser.close(); server.close(); }
});
