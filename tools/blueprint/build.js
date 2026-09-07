#!/usr/bin/env node
/*
 * Builds dist/blueprint.html — the SimWorld Blueprint page — from:
 *   docs/research/rimworld-mechanics.md   (tab 01)
 *   docs/spec/simworld-spec.md            (tab 02)
 *   docs/status.json                      (tab 03, the build tracker)
 *   docs/research/epoch-inspiration.md    (tab 04)
 *
 * Doc tabs come from the DOCS table near the bottom; add a row to add a tab.
 *
 * The page is published as a claude.ai Artifact with the `db` capability so the
 * tracker's unlocked checklist items can be ticked in-page and persist for every viewer.
 * Items marked done in status.json are locked ("in repo") — the repo is the source of truth.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const { marked } = require('marked');

const ROOT = path.resolve(__dirname, '..', '..');
const OUT_DIR = path.join(__dirname, 'dist');
const OUT_FILE = path.join(OUT_DIR, 'blueprint.html');

// ---------------------------------------------------------------- helpers

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

function slugify(text) {
  return text.toLowerCase().replace(/[^\w\s-]/g, '').trim().replace(/\s+/g, '-');
}

function fail(message) {
  console.error('blueprint: ' + message);
  process.exit(1);
}

// ---------------------------------------------------------------- markdown docs

function extractMermaid(md) {
  const lines = md.split('\n');
  const out = [];
  const diagrams = [];
  let heading = '';
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    const h = line.match(/^##{1,2}\s+(.*)$/);
    if (h) heading = h[1];
    if (line.trim() === '```mermaid') {
      const raw = [];
      i++;
      while (i < lines.length && lines[i].trim() !== '```') raw.push(lines[i++]);
      out.push(`@@MERMAID:${diagrams.length}@@`);
      diagrams.push({ raw: raw.join('\n'), caption: heading });
      continue;
    }
    out.push(line);
  }
  return { md: out.join('\n'), diagrams };
}

function renderDoc(relPath, prefix) {
  const raw = fs.readFileSync(path.join(ROOT, relPath), 'utf8');
  const { md, diagrams } = extractMermaid(raw);

  const renderer = new marked.Renderer();
  renderer.code = ({ text, lang }) =>
    `<pre class="codeblock"><code class="lang-${(lang || 'text').trim()}">${escapeHtml(text)}</code></pre>`;
  renderer.heading = ({ text, depth }) => `<h${depth} id="${prefix}-${slugify(text)}">${text}</h${depth}>\n`;
  marked.use({ renderer, gfm: true });

  let html = marked.parse(md);
  let fig = 0;
  html = html.replace(/<p>@@MERMAID:(\d+)@@<\/p>/g, (_, idx) => {
    const d = diagrams[Number(idx)];
    fig++;
    return `<figure class="plate"><pre class="mermaid">\n${d.raw}\n</pre><figcaption><span class="fig-tag">FIG. ${String(fig).padStart(2, '0')}</span>${escapeHtml(d.caption)}</figcaption></figure>`;
  });

  const title = (raw.match(/^#\s+(.*)$/m) || [, ''])[1];
  html = html.replace(/^<h1[^>]*>.*?<\/h1>\s*/s, '');

  const toc = [];
  const h2 = /<h2 id="([^"]+)">([^<]*)<\/h2>/g;
  let m;
  while ((m = h2.exec(html))) {
    const t = m[2].match(/^(\d+)\.\s*(.*)$/);
    toc.push({ id: m[1], n: t ? t[1] : '', text: t ? t[2] : m[2] });
  }

  const firstH2 = html.indexOf('<h2');
  const intro = html.slice(0, firstH2).trim();
  const body = html.slice(firstH2)
    .replace(/<table>/g, '<div class="table-wrap"><table>')
    .replace(/<\/table>/g, '</table></div>');
  const wordCount = html.replace(/<[^>]+>/g, ' ').split(/\s+/).filter(Boolean).length;

  return { title, intro, body, toc, sectionCount: toc.length, diagramCount: diagrams.length, wordCount, source: path.dirname(relPath) + '/' };
}

// ---------------------------------------------------------------- status.json

function loadStatus() {
  const file = path.join(ROOT, 'docs', 'status.json');
  let status;
  try {
    status = JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (e) {
    fail('docs/status.json is not valid JSON: ' + e.message);
  }
  const legend = Object.keys(status.statusLegend || {});
  if (!Array.isArray(status.systems) || status.systems.length === 0) fail('status.json needs a non-empty systems array');
  const seenIds = new Set();
  status.systems.forEach((sys, i) => {
    ['id', 'n', 'rimworld', 'translation', 'module', 'status', 'items'].forEach((k) => {
      if (sys[k] === undefined) fail(`system #${i} is missing "${k}"`);
    });
    if (sys.n !== i + 1) fail(`system "${sys.id}" has n=${sys.n}, expected ${i + 1}`);
    if (!legend.includes(sys.status)) fail(`system "${sys.id}" has unknown status "${sys.status}"`);
    if (seenIds.has(sys.id)) fail(`duplicate system id "${sys.id}"`);
    seenIds.add(sys.id);
    sys.items.forEach((item) => {
      if (!item.id || !item.label || typeof item.done !== 'boolean') fail(`system "${sys.id}" has a malformed item: ${JSON.stringify(item)}`);
      if (!item.id.startsWith(sys.id + '.')) fail(`item "${item.id}" must be prefixed with "${sys.id}."`);
      if (!/^[A-Za-z0-9_.-]+$/.test(item.id)) fail(`item id "${item.id}" contains characters the checklist store rejects`);
      if (seenIds.has(item.id)) fail(`duplicate item id "${item.id}"`);
      seenIds.add(item.id);
    });
  });
  return status;
}

function trackerStats(status) {
  let items = 0, done = 0, tests = 0;
  const portedStatuses = ['ported', 'translated', 'verified'];
  let ported = 0;
  status.systems.forEach((s) => {
    items += s.items.length;
    done += s.items.filter((i) => i.done).length;
    tests += s.tests || 0;
    if (portedStatuses.includes(s.status)) ported++;
  });
  // testsTotal covers the whole suite (shared utilities included); per-system counts are the attributable part.
  if (typeof status.testsTotal === 'number') tests = Math.max(tests, status.testsTotal);
  return { items, done, tests, ported, systems: status.systems.length, pct: Math.round((done / items) * 100) };
}

function renderTracker(status) {
  const stats = trackerStats(status);
  const launch = status.phase.launch;

  const launchHtml = launch.available
    ? `<div class="launch available"><a class="btn" href="${escapeHtml(launch.url)}" target="_blank" rel="noopener">Launch ${escapeHtml(launch.label || 'build')}</a><span class="why">${escapeHtml(launch.reason || '')}</span></div>`
    : `<div class="launch"><span class="btn" aria-disabled="true">Launch</span><span class="why">${escapeHtml(launch.reason)}</span></div>`;

  const stackRows = Object.entries(status.stack)
    .map(([k, v]) => `<div class="kv"><span class="k">${escapeHtml(k)}</span><span class="v">${escapeHtml(v)}</span></div>`)
    .join('\n');

  const systems = status.systems.map((sys) => {
    const total = sys.items.length;
    const verified = sys.items.filter((i) => i.done).length;
    const items = sys.items.map((item) => item.done
      ? `<label class="item locked"><input type="checkbox" checked disabled><span class="txt">${escapeHtml(item.label)}</span><span class="tag">in repo</span></label>`
      : `<label class="item"><input type="checkbox" data-item="${escapeHtml(item.id)}" disabled><span class="txt">${escapeHtml(item.label)}</span></label>`
    ).join('\n');
    const tState = sys.translationState || 'proposed';
    return `<section class="sys" id="t-sys-${sys.id}" data-sys="${sys.id}" data-total="${total}" data-verified="${verified}">
  <div class="sys-head">
    <span class="sys-n">${String(sys.n).padStart(2, '0')}</span>
    <div>
      <h3 id="t-h-${sys.id}">${escapeHtml(sys.rimworld)}</h3>
      <div class="module">${escapeHtml(sys.module)}${sys.tests ? ` · ${sys.tests} tests` : ''}</div>
    </div>
    <span class="pill ${sys.status}">${escapeHtml(sys.status)}</span>
  </div>
  <div class="side">
    <div class="rim"><div class="col-label">RimWorld system</div><p>${escapeHtml(sys.rimworld)}</p></div>
    <div><div class="col-label">SimWorld translation <span class="pill ${tState}">${tState}</span></div><p>${escapeHtml(sys.translation)}</p></div>
  </div>
  <div class="sys-bar"><div class="progress"><span class="seg verified" style="width:${(verified / total) * 100}%"></span><span class="seg ticked" style="width:0%"></span></div><span class="cnt"><b class="v">${verified}</b> in repo · <b class="t">0</b> ticked · ${total} total</span></div>
  <details class="checklist"><summary>Checklist</summary><div class="items">${items}</div></details>
</section>`;
  }).join('\n');

  const decisions = status.decisions.map((d) => `<div class="decision ${d.state}"><span class="when">${escapeHtml(d.date)}</span><div><b>${escapeHtml(d.title)}</b><p>${escapeHtml(d.note)}</p></div></div>`).join('\n');

  const toc = [{ id: 't-overview', n: '', text: 'Overview' }, { id: 't-systems', n: '', text: 'Systems' }]
    .concat(status.systems.map((s) => ({ id: `t-h-${s.id}`, n: String(s.n), text: s.rimworld, sub: true })))
    .concat([{ id: 't-decisions', n: '', text: 'Decisions' }]);

  const html = `<div class="doc-cover tracker-cover">
  <h1>SimWorld Build Tracker</h1>
  <div class="dek"><p>Phase ${status.phase.id} — ${escapeHtml(status.phase.name)}. ${escapeHtml(status.phase.summary)}</p></div>
  <div class="stat-row">
    <div class="stat"><span class="v">${stats.ported}/${stats.systems}</span><span class="l">Systems ported</span></div>
    <div class="stat"><span class="v"><span id="done-count">${stats.done}</span>/${stats.items}</span><span class="l">Items in repo</span></div>
    <div class="stat"><span class="v" id="ticked-count">0</span><span class="l">Ticked in page</span></div>
    <div class="stat"><span class="v">${stats.tests}</span><span class="l">Tests</span></div>
    <div class="stat"><span class="v">${escapeHtml(status.updated)}</span><span class="l">Updated</span></div>
  </div>
  <div class="progress overall" data-total="${stats.items}" data-verified="${stats.done}"><span class="seg verified" style="width:${stats.pct}%"></span><span class="seg ticked" style="width:0%"></span></div>
  ${launchHtml}
</div>
<h2 id="t-overview">Overview</h2>
<div class="kv-grid">${stackRows}</div>
<p class="db-note" id="db-note">Checklist: connecting…</p>
<h2 id="t-systems">Systems</h2>
<p>Left: the RimWorld system as researched. Right: what it becomes in a god-game over a whole civilization. Locked items are verified in the repository; the rest are yours to tick as planning progresses.</p>
${systems}
<h2 id="t-decisions">Decisions</h2>
${decisions}`;

  return { html, toc, stats };
}

// ---------------------------------------------------------------- page

function tocHtml(toc) {
  return toc.map((t) => `<a href="#${t.id}"${t.sub ? ' class="sub"' : ''}><span class="n">${t.n}</span><span class="t">${escapeHtml(t.text)}</span></a>`).join('\n');
}

function coverHtml(doc) {
  return `<div class="doc-cover">
  <h1>${doc.title}</h1>
  <div class="dek">${doc.intro}</div>
  <div class="stat-row">
    <div class="stat"><span class="v">${doc.sectionCount}</span><span class="l">Sections</span></div>
    <div class="stat"><span class="v">${doc.diagramCount}</span><span class="l">Diagrams</span></div>
    <div class="stat"><span class="v">${doc.wordCount}</span><span class="l">Words</span></div>
    <div class="stat"><span class="v">${doc.source}</span><span class="l">Source</span></div>
  </div>
</div>`;
}

function buildPage(docs, tracker, status) {
  return `<title>SimWorld Blueprint</title>
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500;600;700&family=Literata:opsz,wght@7..72,400;7..72,500;7..72,600;7..72,700&display=swap" rel="stylesheet">
<style>
  :root {
    --bg: #eef1f5; --surface: #ffffff; --surface-2: #e7ebf1; --ink: #1b222c; --muted: #5b6675; --line: #d3d9e0;
    --accent: #b5691e; --accent-ink: #ffffff; --accent-wash: rgba(181, 105, 30, 0.08); --code-bg: #e9ecf1; --shadow: rgba(20, 26, 34, 0.08);
    --plate-bg: #f7f5f0; --plate-border: #d8d3c7; --plate-ink: #4a4638; --plate-accent: #b5691e;
  }
  @media (prefers-color-scheme: dark) {
    :root:not([data-theme="light"]) {
      --bg: #12161c; --surface: #1b212b; --surface-2: #171c24; --ink: #dce3ec; --muted: #8792a3; --line: #2b3340;
      --accent: #f0a857; --accent-ink: #1b1206; --accent-wash: rgba(240, 168, 87, 0.12); --code-bg: #20262f; --shadow: rgba(0, 0, 0, 0.4);
    }
  }
  :root[data-theme="dark"] {
    --bg: #12161c; --surface: #1b212b; --surface-2: #171c24; --ink: #dce3ec; --muted: #8792a3; --line: #2b3340;
    --accent: #f0a857; --accent-ink: #1b1206; --accent-wash: rgba(240, 168, 87, 0.12); --code-bg: #20262f; --shadow: rgba(0, 0, 0, 0.4);
  }
  * { box-sizing: border-box; }
  html { scroll-behavior: smooth; }
  @media (prefers-reduced-motion: reduce) { html { scroll-behavior: auto; } }
  body { background: var(--bg); color: var(--ink); font-family: 'Literata', Georgia, 'Times New Roman', serif; font-size: 16px; }
  .mono, .brand, .chip, .doc-tab, .toc-label, nav.toc, .stat, h2, h3, code, .pill, .sys-n, .module, .col-label, .tag, .launch .btn, .db-note, .decision .when, .kv .k, .sys-bar
    { font-family: 'IBM Plex Mono', ui-monospace, monospace; }

  .shell { display: flex; align-items: flex-start; }
  .sidebar { width: 300px; flex-shrink: 0; position: sticky; top: 0; height: 100vh; background: var(--surface-2); border-right: 1px solid var(--line); padding: 1.5rem 1.15rem; display: flex; flex-direction: column; gap: 1.35rem; overflow-y: auto; }
  .brand .mark { font-size: 0.98rem; font-weight: 600; letter-spacing: 0.02em; color: var(--ink); }
  .brand .sub { display: block; margin-top: 0.2rem; font-size: 0.68rem; color: var(--muted); letter-spacing: 0.03em; }
  .meta-row { display: flex; gap: 0.4rem; flex-wrap: wrap; }
  .chip { font-size: 0.62rem; letter-spacing: 0.05em; text-transform: uppercase; padding: 0.2rem 0.45rem; border: 1px solid var(--line); border-radius: 3px; color: var(--muted); }
  .chip.accent { border-color: var(--accent); color: var(--accent); }
  .doc-tabs { display: flex; flex-direction: column; gap: 0.4rem; }
  .doc-tab { font-size: 0.74rem; text-align: left; padding: 0.6rem 0.65rem; border: 1px solid var(--line); background: var(--surface); color: var(--muted); cursor: pointer; border-radius: 5px; letter-spacing: 0.01em; }
  .doc-tab .num { color: var(--accent); margin-right: 0.4rem; font-weight: 600; }
  .doc-tab[aria-selected="true"] { border-color: var(--accent); color: var(--ink); background: var(--accent-wash); }
  .doc-tab:focus-visible, a:focus-visible, button:focus-visible, summary:focus-visible, input:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
  .toc-label { font-size: 0.65rem; letter-spacing: 0.08em; text-transform: uppercase; color: var(--muted); margin: 0.2rem 0 -0.5rem; }
  nav.toc { display: flex; flex-direction: column; font-size: 0.74rem; gap: 0.05rem; }
  nav.toc a { color: var(--muted); text-decoration: none; padding: 0.32rem 0.5rem; border-radius: 4px; display: flex; gap: 0.55rem; line-height: 1.35; }
  nav.toc a.sub { padding-left: 1.2rem; font-size: 0.7rem; }
  nav.toc a .n { color: var(--line); font-variant-numeric: tabular-nums; flex-shrink: 0; min-width: 1.2em; }
  nav.toc a:hover { background: var(--surface); color: var(--ink); }
  nav.toc a.active { background: var(--accent-wash); color: var(--ink); }
  nav.toc a.active .n { color: var(--accent); }

  .main { flex: 1; min-width: 0; padding: 3rem clamp(1.25rem, 4vw, 4rem) 6rem; }
  .content-col { max-width: 70ch; margin: 0 auto; }
  .doc-cover { border-bottom: 1px solid var(--line); padding-bottom: 1.5rem; margin-bottom: 2.25rem; }
  .doc-cover h1 { font-family: 'IBM Plex Mono', ui-monospace, monospace; font-size: clamp(1.4rem, 3vw, 1.95rem); margin: 0 0 0.65rem; text-wrap: balance; font-weight: 600; }
  .doc-cover .dek { color: var(--muted); font-size: 1.05rem; line-height: 1.6; margin: 0 0 1.15rem; }
  .doc-cover .dek p { margin: 0; font-size: inherit; line-height: inherit; }
  .stat-row { display: flex; gap: 1.75rem; flex-wrap: wrap; }
  .stat .v { display: block; font-size: 1.05rem; color: var(--ink); font-variant-numeric: tabular-nums; }
  .stat .l { font-size: 0.62rem; letter-spacing: 0.06em; text-transform: uppercase; color: var(--muted); }

  h2 { font-size: 1.28rem; font-weight: 600; letter-spacing: -0.01em; margin: 3rem 0 1.1rem; padding-top: 1.75rem; border-top: 1px solid var(--line); text-wrap: balance; scroll-margin-top: 1.5rem; }
  h2:first-of-type { border-top: none; padding-top: 0; margin-top: 0; }
  h3 { font-size: 1rem; font-weight: 600; margin: 1.85rem 0 0.65rem; color: var(--ink); scroll-margin-top: 1.5rem; }
  p, li { font-size: 1.03rem; line-height: 1.68; }
  p { margin: 0 0 1.05rem; }
  ul, ol { padding-left: 1.3rem; margin: 0 0 1.15rem; }
  li { margin-bottom: 0.4rem; }
  strong { font-weight: 650; }
  code { font-size: 0.85em; background: var(--code-bg); padding: 0.1rem 0.35rem; border-radius: 3px; }
  pre.codeblock { background: var(--code-bg); border: 1px solid var(--line); border-radius: 7px; padding: 1rem 1.15rem; overflow-x: auto; margin: 1.3rem 0; }
  pre.codeblock code { background: none; padding: 0; font-size: 0.82rem; line-height: 1.55; }
  .table-wrap { overflow-x: auto; margin: 1.5rem 0; }
  table { width: 100%; border-collapse: collapse; font-size: 0.92rem; }
  th, td { text-align: left; padding: 0.55rem 0.8rem; border-bottom: 1px solid var(--line); vertical-align: top; }
  th { font-family: 'IBM Plex Mono', ui-monospace, monospace; font-size: 0.66rem; text-transform: uppercase; letter-spacing: 0.05em; color: var(--muted); font-weight: 600; white-space: nowrap; }
  tbody tr:hover { background: var(--surface); }
  figure.plate { background: var(--plate-bg); border: 1px solid var(--plate-border); border-radius: 7px; margin: 1.85rem 0; padding: 1.25rem 1.25rem 0.9rem; box-shadow: 0 1px 2px var(--shadow); overflow-x: auto; }
  figure.plate pre.mermaid { margin: 0; color: #23262b; }
  figure.plate figcaption { margin-top: 0.8rem; padding-top: 0.6rem; border-top: 1px dashed var(--plate-border); font-family: 'IBM Plex Mono', ui-monospace, monospace; font-size: 0.7rem; color: var(--plate-ink); letter-spacing: 0.02em; }
  .fig-tag { color: var(--plate-accent); font-weight: 600; margin-right: 0.6rem; }

  /* tracker */
  .launch { display: flex; align-items: center; gap: 0.9rem; margin-top: 1.25rem; padding: 0.85rem 1rem; border: 1px dashed var(--line); border-radius: 7px; }
  .launch .btn { font-size: 0.72rem; letter-spacing: 0.06em; text-transform: uppercase; padding: 0.5rem 0.8rem; border-radius: 5px; border: 1px solid var(--line); color: var(--muted); background: var(--surface); text-decoration: none; white-space: nowrap; }
  .launch.available { border-style: solid; border-color: var(--accent); }
  .launch.available .btn { background: var(--accent); color: var(--accent-ink); border-color: var(--accent); }
  .launch .why { font-size: 0.9rem; color: var(--muted); line-height: 1.45; }
  .progress { height: 8px; background: var(--code-bg); border-radius: 4px; overflow: hidden; display: flex; }
  .progress.overall { margin-top: 1.1rem; }
  .progress .seg { height: 100%; display: block; }
  .progress .seg.verified { background: var(--accent); }
  .progress .seg.ticked { background: var(--accent); opacity: 0.4; }
  .kv-grid { display: grid; grid-template-columns: max-content 1fr; gap: 0.45rem 1.25rem; margin: 0 0 1.25rem; font-size: 0.92rem; }
  .kv { display: contents; }
  .kv .k { font-size: 0.66rem; letter-spacing: 0.06em; text-transform: uppercase; color: var(--muted); padding-top: 0.25rem; }
  .kv .v { line-height: 1.5; }
  .db-note { font-size: 0.68rem; color: var(--muted); margin: 0.25rem 0 0; }
  .sys { border: 1px solid var(--line); border-radius: 8px; margin: 1rem 0; background: var(--surface); scroll-margin-top: 1.5rem; }
  .sys-head { display: grid; grid-template-columns: auto 1fr auto; gap: 1rem; align-items: start; padding: 0.9rem 1rem 0.6rem; }
  .sys-n { font-size: 1.1rem; color: var(--muted); font-variant-numeric: tabular-nums; padding-top: 0.1rem; }
  .sys h3 { margin: 0; font-size: 1rem; }
  .module { font-size: 0.7rem; color: var(--muted); margin-top: 0.2rem; }
  .pill { font-size: 0.62rem; letter-spacing: 0.05em; text-transform: uppercase; padding: 0.18rem 0.5rem; border-radius: 999px; border: 1px solid var(--line); color: var(--muted); white-space: nowrap; }
  .pill.in-progress { border-color: var(--accent); color: var(--accent); }
  .pill.ported, .pill.translated, .pill.verified, .pill.decided { background: var(--accent); color: var(--accent-ink); border-color: var(--accent); }
  .pill.proposed { border-style: dashed; }
  .side { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; padding: 0 1rem 0.9rem; }
  .col-label { font-size: 0.62rem; letter-spacing: 0.08em; text-transform: uppercase; color: var(--muted); margin-bottom: 0.3rem; display: flex; align-items: center; gap: 0.5rem; }
  .side p { font-size: 0.95rem; line-height: 1.55; margin: 0; }
  .side .rim { border-right: 1px solid var(--line); padding-right: 1rem; }
  .sys-bar { padding: 0 1rem 0.75rem; display: flex; align-items: center; gap: 0.75rem; font-size: 0.68rem; color: var(--muted); }
  .sys-bar .progress { flex: 1; height: 6px; }
  .sys-bar b { color: var(--ink); font-weight: 600; }
  details.checklist { border-top: 1px solid var(--line); }
  details.checklist summary { cursor: pointer; padding: 0.6rem 1rem; font-family: 'IBM Plex Mono', ui-monospace, monospace; font-size: 0.72rem; color: var(--muted); list-style: none; }
  details.checklist summary::-webkit-details-marker { display: none; }
  details.checklist summary::before { content: "\\25B8  "; }
  details.checklist[open] summary::before { content: "\\25BE  "; }
  .items { padding: 0.2rem 1rem 0.9rem; display: flex; flex-direction: column; gap: 0.35rem; }
  .item { display: flex; align-items: flex-start; gap: 0.6rem; font-size: 0.92rem; line-height: 1.45; cursor: pointer; }
  .item input { margin-top: 0.3rem; accent-color: var(--accent); flex-shrink: 0; }
  .item.locked { cursor: default; }
  .item.locked .txt { color: var(--muted); }
  .item.ticked .txt { color: var(--muted); }
  .item .tag { font-size: 0.6rem; letter-spacing: 0.05em; text-transform: uppercase; color: var(--accent); border: 1px solid var(--accent); border-radius: 3px; padding: 0.05rem 0.35rem; margin-left: auto; white-space: nowrap; align-self: center; }
  .decision { display: grid; grid-template-columns: auto 1fr; gap: 0.75rem 1rem; padding: 0.7rem 0; border-bottom: 1px solid var(--line); }
  .decision .when { font-size: 0.68rem; color: var(--muted); padding-top: 0.25rem; white-space: nowrap; }
  .decision b { font-weight: 600; display: block; margin-bottom: 0.2rem; }
  .decision.open b::after { content: " \\00B7 OPEN"; color: var(--accent); font-family: 'IBM Plex Mono', ui-monospace, monospace; font-size: 0.65rem; letter-spacing: 0.06em; }
  .decision p { margin: 0; font-size: 0.92rem; color: var(--muted); line-height: 1.5; }

  .doc-panel[hidden] { display: none !important; }
  [hidden] { display: none !important; }
  @media (max-width: 900px) {
    .shell { display: block; }
    .sidebar { width: 100%; height: auto; max-height: 46vh; position: static; border-right: none; border-bottom: 1px solid var(--line); }
    .main { padding: 2rem 1.15rem 4rem; }
    .side { grid-template-columns: 1fr; }
    .side .rim { border-right: none; border-bottom: 1px solid var(--line); padding-right: 0; padding-bottom: 0.75rem; }
    .sys-head { grid-template-columns: auto 1fr; }
    .sys-head .pill { grid-column: 2; justify-self: start; }
  }
</style>

<div class="shell">
  <aside class="sidebar">
    <div class="brand">
      <span class="mark">SIMWORLD // DOCS</span>
      <span class="sub">colony-sim design reference &amp; build tracker</span>
    </div>
    <div class="meta-row">
      <span class="chip accent">Phase ${status.phase.id}</span>
      <span class="chip">${escapeHtml(status.stack.language)} / Unity</span>
      <span class="chip">m2windham/simWorld</span>
    </div>
    <div class="doc-tabs" role="tablist">
      <button class="doc-tab" role="tab" data-doc="t" aria-selected="true"><span class="num">03</span>Tracker — Build</button>
${docs.map((d) => `<button class="doc-tab" role="tab" data-doc="${d.prefix}" aria-selected="false"><span class="num">${d.num}</span>${d.label}</button>`).join('\n      ')}
    </div>
    <div>
      <p class="toc-label">Contents</p>
      <nav class="toc" data-doc="t">
${tocHtml(tracker.toc)}
      </nav>
${docs.map((d) => `<nav class="toc" data-doc="${d.prefix}" hidden>
${tocHtml(d.doc.toc)}
      </nav>`).join('\n      ')}
    </div>
  </aside>

  <main class="main">
    <section class="doc-panel content-col" data-doc="t">
      ${tracker.html}
    </section>
${docs.map((d) => `<section class="doc-panel content-col" data-doc="${d.prefix}" hidden>
      ${coverHtml(d.doc)}
      ${d.doc.body}
    </section>`).join('\n    ')}
  </main>
</div>

<script>
(function () {
  var tabs = document.querySelectorAll('.doc-tab');
  var panels = document.querySelectorAll('.doc-panel');
  var tocs = document.querySelectorAll('nav.toc');
  var activeSpyHandler = null;

  function setupSpy(doc) {
    if (activeSpyHandler) window.removeEventListener('scroll', activeSpyHandler);
    var panel = document.querySelector('.doc-panel[data-doc="' + doc + '"]');
    var links = document.querySelectorAll('nav.toc[data-doc="' + doc + '"] a');
    var linkMap = {};
    links.forEach(function (l) { linkMap[l.getAttribute('href').slice(1)] = l; });
    var headings = Array.prototype.slice.call(panel.querySelectorAll('h2[id], h3[id]')).filter(function (h) { return linkMap[h.id]; });

    function update() {
      var current = headings[0];
      for (var i = 0; i < headings.length; i++) {
        if (headings[i].getBoundingClientRect().top - 120 <= 0) current = headings[i]; else break;
      }
      links.forEach(function (l) { l.classList.remove('active'); });
      var link = current && linkMap[current.id];
      if (link) link.classList.add('active');
    }
    activeSpyHandler = function () { requestAnimationFrame(update); };
    window.addEventListener('scroll', activeSpyHandler, { passive: true });
    update();
  }

  function activate(doc) {
    tabs.forEach(function (t) { t.setAttribute('aria-selected', t.dataset.doc === doc ? 'true' : 'false'); });
    panels.forEach(function (p) { p.hidden = p.dataset.doc !== doc; });
    tocs.forEach(function (n) { n.hidden = n.dataset.doc !== doc; });
    window.scrollTo(0, 0);
    setupSpy(doc);
  }
  tabs.forEach(function (t) { t.addEventListener('click', function () { activate(t.dataset.doc); }); });
  setupSpy('t');

  // ---- tracker checklist: unlocked items persist through the artifact's shared db ----
  var boxes = Array.prototype.slice.call(document.querySelectorAll('input[data-item]'));
  var note = document.getElementById('db-note');
  function setNote(text) { if (note) note.textContent = 'Checklist: ' + text; }

  function recount() {
    var totalTicked = 0;
    document.querySelectorAll('.sys').forEach(function (sys) {
      var total = Number(sys.dataset.total);
      var ticked = sys.querySelectorAll('input[data-item]:checked').length;
      totalTicked += ticked;
      var seg = sys.querySelector('.seg.ticked');
      if (seg) seg.style.width = (ticked / total * 100) + '%';
      var t = sys.querySelector('.sys-bar .t');
      if (t) t.textContent = String(ticked);
    });
    var overall = document.querySelector('.progress.overall');
    if (overall) overall.querySelector('.seg.ticked').style.width = (totalTicked / Number(overall.dataset.total) * 100) + '%';
    var tc = document.getElementById('ticked-count');
    if (tc) tc.textContent = String(totalTicked);
  }

  function applyState(map) {
    boxes.forEach(function (b) {
      var s = map[b.dataset.item];
      var done = !!(s && s.done);
      b.checked = done;
      b.closest('.item').classList.toggle('ticked', done);
    });
    recount();
  }

  function readOnly(reason) {
    boxes.forEach(function (b) { b.disabled = true; });
    setNote(reason);
  }

  var use = (window.claude && typeof window.claude.use === 'function') ? window.claude.use('db') : Promise.resolve(null);
  use.then(function (db) {
    if (!db) { readOnly('read-only here. Ticks save when this page is opened inside claude.ai.'); return; }
    setNote('ticks save for everyone who opens this page.');
    db.collection('checks').onSnapshot(function (snap) {
      var map = {};
      snap.docs.forEach(function (d) { map[d.id] = d.data() || {}; });
      applyState(map);
      boxes.forEach(function (b) { b.disabled = false; });
    }, function (err) { readOnly('sync stopped (' + err.code + '). Reload to reconnect.'); });

    boxes.forEach(function (b) {
      b.addEventListener('change', function () {
        var wanted = b.checked;
        b.disabled = true;
        db.doc('checks/' + b.dataset.item).set({ done: wanted, at: new Date().toISOString() })
          .catch(function (e) { b.checked = !wanted; setNote('could not save (' + e.code + ').'); })
          .then(function () { b.disabled = false; recount(); });
      });
    });
  }).catch(function () { readOnly('unavailable in this view.'); });
})();
</script>
`;
}

// ---------------------------------------------------------------- main

// Every prose doc that gets a tab. The tracker is tab 03 and is built separately;
// these numbers are stable labels, so a new doc appends rather than renumbering.
const DOCS = [
  { prefix: 'r', num: '01', label: 'Research — RimWorld', path: 'docs/research/rimworld-mechanics.md' },
  { prefix: 's', num: '02', label: 'Spec — SimWorld', path: 'docs/spec/simworld-spec.md' },
  { prefix: 'e', num: '04', label: 'Research — Epoch', path: 'docs/research/epoch-inspiration.md' },
];

const status = loadStatus();
const docs = DOCS.map((d) => ({ ...d, doc: renderDoc(d.path, d.prefix) }));
const tracker = renderTracker(status);

fs.mkdirSync(OUT_DIR, { recursive: true });
fs.writeFileSync(OUT_FILE, buildPage(docs, tracker, status));

const s = tracker.stats;
const diagrams = docs.reduce((n, d) => n + d.doc.diagramCount, 0);
console.log(`blueprint: wrote ${path.relative(ROOT, OUT_FILE)} — ${s.systems} systems, ${s.done}/${s.items} items in repo (${s.pct}%), ${s.tests} tests, ${diagrams} diagrams across ${docs.length} docs`);
