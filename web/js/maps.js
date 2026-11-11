// MAPS tab: minimaps with resource / extraction / lock markers and the item heatmap (same data as the desktop app).
import { data, el, iconUrl, pct, showTip, hideTip, escapeHtml, debounce } from './app.js?v=dev';

const COLORS = {
  Concrete: '#aaa69c', Food: '#8bc34a', Metal: '#5da9e9', Oil: '#f0a040', Extraction: '#00e676',
  CompactSafe: '#f0c85a', LargeSafe: '#ff8c28', HasDoor: '#ba78dc', LockedDoor: '#ef5350',
};
const LABELS = {
  Extraction: 'Extraction points', CompactSafe: 'Compact safes', LargeSafe: 'Large safes', HasDoor: 'HAS doors', LockedDoor: 'Locked doors',
};
const SINGLE = { Extraction: 'Extraction point', CompactSafe: 'Compact safe', LargeSafe: 'Large safe', HasDoor: 'HAS door', LockedDoor: 'Locked door' };
const LOCKS = ['CompactSafe', 'LargeSafe', 'HasDoor', 'LockedDoor'];

export async function mountMaps(view) {
  const [md, loot] = await Promise.all([data('maps.json'), data('loot.json')]);
  const keys = Object.fromEntries(md.keys.map(k => [k.tag, k]));
  const resources = md.resources;
  const types = resources.map(r => r.type).concat(['Extraction'], LOCKS);
  const shown = new Set(types);
  const maps = md.maps.map(m => ({
    name: m.name, floors: m.floors,
    points: [].concat(
      m.resources.map(p => ({ ...p })),
      m.extractions.map(p => ({ ...p, type: 'Extraction' })),
      m.locks.map(p => ({ ...p, type: p.kind })),
    ),
  }));
  const lootItems = Object.entries(loot.items).map(([id, v]) => ({ id, name: v.name })).sort((a, b) => a.name.localeCompare(b.name));
  const byName = new Map(lootItems.map(i => [i.name.toLowerCase(), i]));

  let map = maps[0], floorIndex = 0, heatItem = null;

  // ---------------- left panel
  const mapList = el('div.list');
  const floorList = el('div.list');
  const floorBox = el('div', null, el('div.section', { text: 'Floor' }), floorList);
  const filterBox = el('div');
  const findInput = el('input.input', { list: 'loot-items', placeholder: 'Type an item...', 'aria-label': 'Item' });
  const datalist = el('datalist', { id: 'loot-items' }, lootItems.map(i => el('option', { value: i.name })));
  const clearBtn = el('button.btn', { text: '×', title: 'Hide the heatmap (Esc)', onclick: () => setHeat(null) });
  const side = el('div.side', null,
    el('div.section', { text: 'Map' }), mapList, floorBox,
    el('div.section', { text: 'Find an item' }),
    el('div.find', null, findInput, clearBtn), datalist,
    el('div.section', { text: 'Show' }), filterBox,
    el('div.hint', { text: 'Wheel / pinch: zoom · drag: move · double-click: fit. Hover a point for its chance or the key it needs. Find an item: where it can spawn (estimate from the loot settings).' }),
  );
  const title = el('div.title');
  const summary = el('div.summary');
  const canvas = el('canvas');
  const wrap = el('div.canvas-wrap', null, canvas);
  const right = el('div.maps-right', null, title, summary, wrap);
  view.replaceChildren(side, right);

  function pickItem(text) {
    const q = text.trim().toLowerCase();
    if (!q) return null;
    return byName.get(q) || lootItems.find(i => i.name.toLowerCase().includes(q)) || null;
  }
  const onFind = debounce(() => { const it = pickItem(findInput.value); if (it && byName.get(findInput.value.trim().toLowerCase())) setHeat(it); }, 300);
  findInput.addEventListener('input', onFind);
  findInput.addEventListener('keydown', e => {
    if (e.key === 'Enter') { const it = pickItem(findInput.value); if (it) { findInput.value = it.name; setHeat(it); } }
    else if (e.key === 'Escape') setHeat(null);
  });

  function renderLeft() {
    mapList.replaceChildren(...maps.map(m => {
      const res = m.points.filter(p => resources.some(r => r.type === p.type)).length;
      return el('div.item' + (m === map ? '.on' : ''), { onclick: () => { map = m; floorIndex = 0; refresh(true); } },
        el('div.t', { text: m.name.toUpperCase() }),
        el('div.s', { text: res + ' resource points' + (m.floors.length > 1 ? ' · ' + m.floors.length + ' floors' : '') }));
    }));
    floorBox.hidden = map.floors.length < 2;
    floorList.replaceChildren(...map.floors.map((f, i) => el('div.item' + (i === floorIndex ? '.on' : ''),
      { onclick: () => { floorIndex = i; refresh(true); } },
      el('div', { text: f.name + (i === 0 ? '  (top)' : i === map.floors.length - 1 ? '  (bottom)' : '') }))));
    const multi = map.floors.length > 1;
    filterBox.replaceChildren(...types.map(t => {
      const n = map.points.filter(p => p.type === t && (!multi || p.floor === floorIndex)).length;
      const f = el('div.filter' + (shown.has(t) ? '.on' : ''), {
        onclick: () => { shown.has(t) ? shown.delete(t) : shown.add(t); refresh(false); },
      }, el('span.box', { text: shown.has(t) ? '✓' : '' }), el('span.dot', { style: 'background:' + COLORS[t] }),
        el('span.lbl', { text: LABELS[t] || t }), el('span.n', { text: String(n) }));
      return f;
    }));
  }

  // ---------------- canvas
  const ctx = canvas.getContext('2d');
  const images = {};
  let scale = 1, ox = 0, oy = 0, hover = null, heatCanvas = null;

  function floor() { return map.floors[floorIndex] || map.floors[0]; }
  function image() {
    const f = floor();
    if (!images[f.image]) {
      const img = new Image();
      img.onload = () => { fit(); draw(); };
      img.src = 'data/maps/' + f.image;
      images[f.image] = img;
    }
    return images[f.image];
  }
  function size() { const img = image(); return img.complete && img.naturalWidth ? [img.naturalWidth, img.naturalHeight] : [2048, 2048]; }
  function toImage(f, x, y) {
    const u = (x - (f.centerX - f.extent)) / (2 * f.extent);
    const v = (y - (f.centerY - f.extent)) / (2 * f.extent);
    return [v, 1 - u];
  }
  function screen(p) {
    const [w, h] = size();
    const [nx, ny] = toImage(floor(), p.x, p.y);
    return [ox + nx * w * scale, oy + ny * h * scale];
  }
  function resize() {
    const r = wrap.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    canvas.width = Math.max(1, Math.round(r.width * dpr));
    canvas.height = Math.max(1, Math.round(r.height * dpr));
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  }
  function view2() { const r = wrap.getBoundingClientRect(); return [r.width, r.height]; }
  function fit() {
    const [w, h] = size(), [W, H] = view2();
    scale = Math.min(W / w, H / h);
    ox = (W - w * scale) / 2;
    oy = (H - h * scale) / 2;
  }
  function zoomAt(sx, sy, factor) {
    const [w, h] = size(), [W, H] = view2();
    const min = Math.min(W / w, H / h) * 0.9, max = 4;
    const ns = Math.max(min, Math.min(max, scale * factor)), f = ns / scale;
    ox = sx - (sx - ox) * f;
    oy = sy - (sy - oy) * f;
    scale = ns;
    draw();
  }

  const tinted = {};
  function tintedIcon(type) {
    if (tinted[type] !== undefined) return tinted[type];
    tinted[type] = null;
    const img = new Image();
    img.onload = () => {
      const c = document.createElement('canvas');
      c.width = img.naturalWidth; c.height = img.naturalHeight;
      const g = c.getContext('2d');
      g.drawImage(img, 0, 0);
      g.globalCompositeOperation = 'source-in';
      g.fillStyle = '#1c1a18';
      g.fillRect(0, 0, c.width, c.height);
      tinted[type] = c;
      draw();
    };
    img.src = iconUrl('res_' + type);
    return null;
  }

  function marker(type, x, y, r, hot) {
    const col = COLORS[type] || '#5c564a', dark = '#141210';
    ctx.lineWidth = hot ? 2.4 : 1.6;
    ctx.strokeStyle = type === 'Extraction' ? '#000' : dark;
    ctx.fillStyle = col;
    if (type === 'Extraction') {
      ctx.beginPath(); ctx.moveTo(x, y - r * 1.15); ctx.lineTo(x + r, y + r * 0.75); ctx.lineTo(x - r, y + r * 0.75); ctx.closePath();
      ctx.fill(); ctx.stroke();
      return;
    }
    const safe = type === 'CompactSafe' || type === 'LargeSafe', door = type === 'HasDoor' || type === 'LockedDoor';
    ctx.beginPath();
    if (safe) { const s = r * 0.95; ctx.roundRect(x - s, y - s, 2 * s, 2 * s, s * 0.45); }
    else ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fill(); ctx.stroke();
    if (safe || door) {
      const bw = r * 0.95, bh = r * 0.7;
      ctx.fillStyle = dark;
      ctx.fillRect(x - bw / 2, y - bh * 0.15, bw, bh);
      ctx.lineWidth = Math.max(1.5, r * 0.18);
      ctx.beginPath(); ctx.arc(x, y - bh * 0.15, bw * 0.32, Math.PI, 0); ctx.stroke();
      return;
    }
    const icon = tintedIcon(type);
    if (icon) { const ir = r * 0.72; ctx.drawImage(icon, x - ir, y - ir, 2 * ir, 2 * ir); }
  }

  function visiblePoints() {
    const multi = map.floors.length > 1;
    return map.points.filter(p => shown.has(p.type) && (!multi || p.floor === floorIndex) && (!heatItem || p.type === 'Extraction'));
  }

  function draw() {
    const [W, H] = view2();
    ctx.clearRect(0, 0, W, H);
    const img = image();
    if (!img.complete || !img.naturalWidth) return;
    const [w, h] = size();
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(img, ox, oy, w * scale, h * scale);
    if (heatCanvas) ctx.drawImage(heatCanvas, ox, oy, w * scale, h * scale);
    const r = 10;
    const pts = visiblePoints();
    for (const p of pts) {
      if (p === hover) continue;
      const [x, y] = screen(p);
      if (x < -r || y < -r || x > W + r || y > H + r) continue;
      marker(p.type, x, y, r, false);
    }
    if (hover) { const [x, y] = screen(hover); marker(hover.type, x, y, r * 1.35, true); }
  }

  // ---------------- heatmap
  function buildHeat() {
    heatCanvas = null;
    if (!heatItem) return;
    const G = 256, sigma = 3, rad = Math.ceil(sigma * 3);
    const kernel = [];
    for (let dy = -rad; dy <= rad; dy++) for (let dx = -rad; dx <= rad; dx++) kernel.push(Math.exp(-(dx * dx + dy * dy) / (2 * sigma * sigma)));
    const grid = new Float64Array(G * G);
    const f = floor(), multi = map.floors.length > 1;
    let any = false;
    for (const [x, y, fl, si] of loot.maps[map.name] || []) {
      if (multi && fl !== floorIndex) continue;
      const c = loot.sources[si].chances[heatItem.id] || 0;
      if (c <= 0) continue;
      const [nx, ny] = toImage(f, x, y);
      const cx = Math.floor(nx * G), cy = Math.floor(ny * G);
      any = true;
      let k = 0;
      for (let dy = -rad; dy <= rad; dy++) {
        const yy = cy + dy;
        for (let dx = -rad; dx <= rad; dx++, k++) {
          const xx = cx + dx;
          if (yy < 0 || yy >= G || xx < 0 || xx >= G) continue;
          grid[yy * G + xx] += c * kernel[k];
        }
      }
    }
    if (!any) return;
    let max = 0;
    for (const v of grid) if (v > max) max = v;
    const c = document.createElement('canvas');
    c.width = G; c.height = G;
    const g = c.getContext('2d');
    const id = g.createImageData(G, G);
    for (let i = 0; i < G * G; i++) {
      let t = grid[i] / max;
      if (t < 0.02) continue;
      t = Math.sqrt(t);
      id.data[i * 4] = 255;
      id.data[i * 4 + 1] = 225 - 175 * t;
      id.data[i * 4 + 2] = 70 - 40 * t;
      id.data[i * 4 + 3] = 60 + 170 * t;
    }
    g.putImageData(id, 0, 0);
    heatCanvas = c;
  }

  function heatSummary() {
    const multi = map.floors.length > 1;
    const counts = {};
    for (const [, , fl, si] of loot.maps[map.name] || []) if (!multi || fl === floorIndex) counts[si] = (counts[si] || 0) + 1;
    const rows = Object.entries(counts).map(([si, n]) => ({ s: loot.sources[si], n, c: loot.sources[si].chances[heatItem.id] || 0 }))
      .filter(x => x.c > 0).sort((a, b) => b.c * b.n - a.c * a.n);
    if (!rows.length) return heatItem.name + ' is not found in the loot of this ' + (multi ? 'floor.' : 'map.');
    const expected = rows.reduce((a, x) => a + x.c * x.n, 0);
    return heatItem.name + ': ≈ ' + expected.toFixed(2).replace(/\.?0+$/, '') + ' per raid on this ' + (multi ? 'floor' : 'map') +
      ' (estimate)   ·   ' + rows.slice(0, 4).map(x => x.s.label + ' (' + x.n + '): ' + pct(x.c) + ' each').join('   ·   ');
  }

  function resourceSummary() {
    const parts = resources.map(r => {
      const n = map.points.filter(p => p.type === r.type).length;
      if (!n) return null;
      const exp = n * r.chance;
      return r.type + ': ' + n + ' points, ≈ ' + exp.toFixed(1) + ' present (≈ ' + Math.round(exp * r.amount).toLocaleString('fr-FR') + ' units)';
    }).filter(Boolean);
    return 'On average per raid (whole map):   ' + parts.join('   ·   ');
  }

  function setHeat(item) {
    heatItem = item;
    if (!item) findInput.value = '';
    refresh(false);
  }

  function refresh(resetView) {
    renderLeft();
    const multi = map.floors.length > 1;
    title.textContent = map.name.toUpperCase() + (multi ? '  ·  ' + floor().name.toUpperCase() : '') + (heatItem ? '  ·  ' + heatItem.name.toUpperCase() : '');
    summary.textContent = heatItem ? heatSummary() : resourceSummary();
    hover = null;
    hideTip();
    buildHeat();
    if (resetView) fit();
    draw();
  }

  // ---------------- mouse / touch
  let drag = null;
  const pointers = new Map();
  canvas.addEventListener('wheel', e => { e.preventDefault(); const r = canvas.getBoundingClientRect(); zoomAt(e.clientX - r.left, e.clientY - r.top, e.deltaY < 0 ? 1.25 : 0.8); }, { passive: false });
  canvas.addEventListener('dblclick', () => { fit(); draw(); });
  canvas.addEventListener('pointerdown', e => {
    canvas.setPointerCapture(e.pointerId);
    pointers.set(e.pointerId, [e.clientX, e.clientY]);
    drag = { x: e.clientX, y: e.clientY, ox, oy, dist: null };
    canvas.style.cursor = 'grabbing';
  });
  canvas.addEventListener('pointerup', e => { pointers.delete(e.pointerId); if (!pointers.size) { drag = null; canvas.style.cursor = 'grab'; } });
  canvas.addEventListener('pointercancel', e => { pointers.delete(e.pointerId); drag = null; });
  canvas.addEventListener('pointerleave', () => { if (hover) { hover = null; hideTip(); draw(); } });
  canvas.addEventListener('pointermove', e => {
    const r = canvas.getBoundingClientRect();
    if (pointers.has(e.pointerId)) pointers.set(e.pointerId, [e.clientX, e.clientY]);
    if (pointers.size === 2) {  // pinch zoom
      const [a, b] = [...pointers.values()];
      const d = Math.hypot(a[0] - b[0], a[1] - b[1]);
      if (drag && drag.dist) zoomAt((a[0] + b[0]) / 2 - r.left, (a[1] + b[1]) / 2 - r.top, d / drag.dist);
      if (drag) drag.dist = d;
      return;
    }
    if (drag) { ox = drag.ox + e.clientX - drag.x; oy = drag.oy + e.clientY - drag.y; draw(); return; }
    const sx = e.clientX - r.left, sy = e.clientY - r.top;
    let best = null, bd = 13 * 13;
    for (const p of visiblePoints()) {
      const [x, y] = screen(p);
      const d = (x - sx) ** 2 + (y - sy) ** 2;
      if (d <= bd) { bd = d; best = p; }
    }
    if (best !== hover) {
      hover = best;
      draw();
    }
    if (hover) showTip(tipHtml(hover), e.clientX, e.clientY); else hideTip();
  });

  function tipHtml(p) {
    if (p.type === 'Extraction') return '<b style="color:' + COLORS.Extraction + '">Extraction point</b>';
    if (p.key) {
      const k = keys[p.key];
      return '<b style="color:' + COLORS[p.type] + '">' + SINGLE[p.type] + '</b>Key needed:<br><b>' +
        escapeHtml(k ? k.name : p.key.replace('Inventory.Items.Keys.', '')) + '</b>';
    }
    const r = resources.find(x => x.type === p.type);
    return '<b style="color:' + COLORS[p.type] + '">' + p.type + '</b>' + (r ? Math.round(r.chance * 100) + ' % chance to be there<br>' + r.amount + ' units when collected' : '');
  }

  const ro = new ResizeObserver(() => { const keep = scale; resize(); if (keep === 1) fit(); draw(); });
  ro.observe(wrap);
  resize();
  refresh(true);
  return () => { ro.disconnect(); hideTip(); };
}
