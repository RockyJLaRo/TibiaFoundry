#!/usr/bin/env node
// Validates a built /pokemon/ folder before publishing (dependency-free; also used by the GitHub Actions workflow).
//   node validate-deployment.mjs [pokemonDir] [siteRootDir]
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const dir = path.resolve(process.argv[2] || path.join(here, '..', 'dist'));
const siteRoot = process.argv[3] ? path.resolve(process.argv[3]) : null;
const errors = [], warnings = [];
const err = (m) => errors.push(m), warn = (m) => warnings.push(m);
const GB = 1024 ** 3, MB = 1024 ** 2;

function walk(d) { const out = []; for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); if (e.isDirectory()) out.push(...walk(p)); else out.push(p); } return out; }
const read = (rel) => fs.readFileSync(path.join(dir, rel), 'utf8');
const exists = (rel) => fs.existsSync(path.join(dir, rel));

// 1. Entry point & paths
if (!exists('index.html')) err('index.html missing');
else {
  const html = read('index.html');
  for (const m of html.matchAll(/(?:src|href)="([^"]+)"/g)) {
    const u = m[1];
    if (/^(https?:|data:|#|mailto:)/.test(u)) { if (/^https?:/.test(u)) warn(`index.html references an external URL: ${u}`); continue; }
    if (u.startsWith('/') && !u.startsWith('/pokemon/')) err(`index.html path escapes /pokemon/: ${u}`);
    const local = u.replace(/^\/pokemon\//, '');
    if (!u.startsWith('/') || u.startsWith('/pokemon/')) if (!exists(local.split('?')[0])) err(`index.html references missing file: ${u}`);
  }
}
if (exists('CNAME')) err('pokemon/CNAME must not exist (it would conflict with the site root CNAME)');

// 2. PWA manifest & service worker
try {
  const wm = JSON.parse(read('manifest.webmanifest'));
  if (wm.start_url !== '/pokemon/') err(`web manifest start_url is ${wm.start_url}`);
  if (wm.scope !== '/pokemon/') err(`web manifest scope is ${wm.scope}`);
  for (const ic of wm.icons || []) if (!exists(ic.src)) err(`PWA icon missing: ${ic.src}`);
} catch (e) { err(`manifest.webmanifest unreadable: ${e.message}`); }
if (!exists('service-worker.js')) err('service-worker.js missing');
else {
  const sw = read('service-worker.js');
  if (!sw.includes('/pokemon/index.html')) err('service worker navigation fallback is not /pokemon/index.html');
  const pre = [...sw.matchAll(/url:"([^"]+)"/g)].map((m) => m[1]);
  for (const u of pre) if (!exists(u)) err(`service worker precaches a missing file: ${u}`);
  if (pre.some((u) => u.startsWith('assets/'))) err('service worker precaches Pokémon assets (they must load on demand)');
}

// 3. Asset manifest integrity
let manifest = null;
try { manifest = JSON.parse(read('assets/manifest.json')); } catch (e) { err(`assets/manifest.json unreadable: ${e.message}`); }
let verified = 0;
if (manifest) {
  if (!Array.isArray(manifest.assets)) err('assets/manifest.json has no assets array');
  const ids = new Set();
  for (const a of manifest.assets || []) {
    if (ids.has(a.id)) err(`duplicate asset id ${a.id}`); ids.add(a.id);
    if (!a.url || a.url.includes('..') || a.url.startsWith('/')) { err(`unsafe asset url ${a.url}`); continue; }
    const f = a.root ? path.join(dir, a.url) : path.join(dir, 'assets', a.url);
    if (!fs.existsSync(f)) { err(`manifest asset missing on disk: ${a.url}`); continue; }
    const buf = fs.readFileSync(f);
    if (buf.length !== a.bytes) { err(`size mismatch ${a.url}`); continue; }
    if (crypto.createHash('sha256').update(buf).digest('hex') !== a.sha256) { err(`checksum mismatch ${a.url}`); continue; }
    verified++;
  }
  if (manifest.totalFiles !== (manifest.assets || []).length) err('manifest totalFiles does not match asset count');
}

// 4. App data → asset references
try {
  const idx = JSON.parse(read('data/pokedex.json'));
  if (!idx.list?.length) err('data/pokedex.json is empty');
  for (const e of idx.list || []) for (const u of [e.th, e.im]) if (u && !fs.existsSync(path.join(dir, 'assets', u))) err(`pokedex index references missing ${u}`);
  const speciesDir = path.join(dir, 'data', 'species');
  const files = fs.readdirSync(speciesDir);
  if (files.length !== idx.list.length) err(`species files (${files.length}) ≠ index entries (${idx.list.length})`);
  let refs = 0;
  for (const f of files) {
    const s = JSON.parse(fs.readFileSync(path.join(speciesDir, f), 'utf8'));
    for (const v of s.variants) for (const k of ['model', 'modelShiny', 'image', 'imageShiny', 'imageFemale', 'imageFemaleShiny', 'portrait', 'thumb', 'thumbShiny', 'cry']) {
      if (!v[k]) continue; refs++;
      if (!fs.existsSync(path.join(dir, 'assets', v[k].url))) err(`${f} ${v.key}.${k} references missing ${v[k].url}`);
    }
  }
  console.log(`Species data: ${files.length} files, ${refs} asset references checked`);
} catch (e) { err(`app data check failed: ${e.message}`); }

// 5. GitHub limits
const all = walk(dir);
const total = all.reduce((n, f) => n + fs.statSync(f).size, 0);
const biggest = all.reduce((b, f) => { const s = fs.statSync(f).size; return s > b.s ? { f, s } : b; }, { f: '', s: 0 });
if (biggest.s > 100 * MB) err(`file over GitHub's 100 MB limit: ${biggest.f}`); else if (biggest.s > 50 * MB) warn(`file over 50 MB (GitHub warns): ${biggest.f}`);
let siteTotal = total;
if (siteRoot) { const rest = walk(siteRoot).filter((f) => !path.resolve(f).startsWith(dir + path.sep)); siteTotal = total + rest.reduce((n, f) => n + fs.statSync(f).size, 0); if (!fs.existsSync(path.join(siteRoot, 'CNAME'))) warn('site root has no CNAME file'); if (!fs.existsSync(path.join(siteRoot, 'index.html'))) err('site root index.html missing (existing website would be lost)'); }
if (siteTotal > 1 * GB) err(`published site is ${(siteTotal / MB).toFixed(0)} MB, over GitHub Pages' 1 GB limit`); else if (siteTotal > 0.85 * GB) warn(`published site is ${(siteTotal / MB).toFixed(0)} MB (close to the 1 GB limit)`);

console.log(`/pokemon/: ${all.length} files, ${(total / MB).toFixed(1)} MB; largest ${path.relative(dir, biggest.f)} ${(biggest.s / MB).toFixed(2)} MB`);
if (siteRoot) console.log(`Whole site: ${(siteTotal / MB).toFixed(1)} MB of 1024 MB`);
console.log(`Verified ${verified} manifest assets (size + SHA-256).`);
warnings.forEach((w) => console.warn(`WARN  ${w}`));
errors.slice(0, 50).forEach((e) => console.error(`ERROR ${e}`));
if (errors.length) { console.error(`${errors.length} error(s).`); process.exit(1); }
console.log('Deployment validation passed.');
