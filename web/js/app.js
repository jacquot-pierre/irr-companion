// IRR Companion web: tab router and shared helpers.
// The data files are the same as the desktop app's (src/Resources), served under data/.
import { mountMaps } from './maps.js';
import { mountTraders } from './traders.js';
import { mountWeapons } from './weapons.js';
import { mountCommands } from './commands.js';
import { mountHideout } from './hideout.js';
import { mountPresets } from './presets.js';

const cache = {};

/** JSON file of data/ (cached). */
export function data(name) {
  if (!cache[name]) cache[name] = fetch('data/' + name).then(r => {
    if (!r.ok) throw new Error(name + ': ' + r.status);
    return r.json();
  });
  return cache[name];
}

/** URL of a game icon (item_<id>.png, cat_<category>.png). */
export function iconUrl(name) { return 'data/icons/' + name + '.png'; }

/** <img> of an item icon, falling back to the silhouette of its category, then nothing. */
export function itemIcon(id, cat) {
  const img = document.createElement('img');
  img.alt = '';
  img.loading = 'lazy';
  img.src = iconUrl('item_' + id);
  img.onerror = () => {
    if (cat && !img.dataset.fallback) { img.dataset.fallback = '1'; img.src = iconUrl('cat_' + cat); img.style.opacity = '.55'; }
    else img.remove();
  };
  return img;
}

/** Small element builder: el('div.cls', {attr}, children...). */
export function el(spec, attrs, ...children) {
  const [tag, ...cls] = spec.split('.');
  const e = document.createElement(tag || 'div');
  if (cls.length) e.className = cls.join(' ');
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (v === undefined || v === null || v === false) continue;
    if (k === 'text') e.textContent = v;
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else e.setAttribute(k, v === true ? '' : v);
  }
  for (const c of children.flat()) if (c !== null && c !== undefined && c !== false) e.append(c);
  return e;
}

export function pct(p) {
  if (p >= 0.995) return '100 %';
  const v = p * 100;
  if (v >= 9.5) return Math.round(v) + ' %';
  if (v >= 0.95) return v.toFixed(1).replace(/\.0$/, '') + ' %';
  if (v >= 0.0095) return v.toFixed(2).replace(/0$/, '') + ' %';
  return '< 0.01 %';
}

export function money(v) { return v > 0 ? (v >= 10 ? Math.round(v).toLocaleString('fr-FR') : String(+v.toFixed(2))) : '?'; }

export function chanceClass(p) { return p >= 0.995 ? 'c-high' : p >= 0.4 ? 'c-mid' : p >= 0.1 ? 'c-low' : 'c-min'; }

export function status(text) { document.getElementById('status').textContent = text; }

export async function copy(text, what) {
  try {
    await navigator.clipboard.writeText(text);
    status('Copied ' + (what || '') + '  ·  paste it in the game console (² / ~, ^ or F10) with Ctrl+V.');
  } catch (e) { status('Could not copy: ' + e.message); }
}

const tooltip = document.getElementById('tooltip');
export function showTip(html, x, y) {
  tooltip.innerHTML = html;
  tooltip.hidden = false;
  const r = tooltip.getBoundingClientRect();
  let left = x + 16, top = y - r.height / 2;
  if (left + r.width > window.innerWidth - 4) left = x - 16 - r.width;
  top = Math.max(4, Math.min(window.innerHeight - r.height - 4, top));
  tooltip.style.left = left + 'px';
  tooltip.style.top = top + 'px';
}
export function hideTip() { tooltip.hidden = true; }

export function escapeHtml(s) { return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]); }

/** Runs fn after the user stops typing for a moment (no rebuild on every key). */
export function debounce(fn, ms = 350) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}

/** Segmented control: one button per label; onpick(index). */
export function seg(labels, selected, onpick, opts = {}) {
  const box = el('div.seg' + (opts.small ? '.small' : ''), opts.title ? { title: opts.title } : null);
  labels.forEach((l, i) => box.append(el('button' + (i === selected ? '.on' : ''), {
    type: 'button', text: l, disabled: opts.disabled || undefined, onclick: () => onpick(i),
  })));
  return box;
}

/** Value kept in this browser (localStorage); load() gives def when missing or unreadable. */
export function stored(key, def) {
  return {
    load() { try { const v = localStorage.getItem(key); return v == null ? def : JSON.parse(v); } catch (e) { return def; } },
    save(v) { try { localStorage.setItem(key, JSON.stringify(v)); return true; } catch (e) { status('Could not save in this browser: ' + e.message); return false; } },
  };
}

/** Downloads a JSON document as a file. */
export function downloadJson(doc, filename) {
  const a = el('a', { href: URL.createObjectURL(new Blob([JSON.stringify(doc, null, 2)], { type: 'application/json' })), download: filename });
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 10000);
}

/** Asks for a JSON file and gives its content (null when cancelled). */
export function pickJson() {
  return new Promise((resolve, reject) => {
    const input = el('input', { type: 'file', accept: '.json,application/json', hidden: true });
    input.addEventListener('change', async () => {
      const f = input.files[0];
      input.remove();
      if (!f) return resolve(null);
      try { resolve(JSON.parse(await f.text())); } catch (e) { reject(new Error(f.name + ' is not a JSON file the app can read.')); }
    });
    document.body.append(input);
    input.click();
  });
}

/** Modal dialog with a text box. Resolves to the text (OK) or null (cancel).
 *  opts: { label, value, multiline, readonly, ok, validate(text) -> error message or null } */
export function dialog(title, opts = {}) {
  return new Promise(resolve => {
    const box = opts.multiline
      ? el('textarea.input.code', { rows: 6, spellcheck: 'false' })
      : el('input.input', { spellcheck: 'false' });
    box.value = opts.value || '';
    if (opts.readonly) box.readOnly = true;
    const err = el('div.err');
    const close = v => { back.remove(); document.removeEventListener('keydown', key); resolve(v); };
    const ok = () => {
      if (opts.readonly) return close(box.value);
      const e = opts.validate ? opts.validate(box.value) : null;
      if (e) { err.textContent = e; return; }
      close(box.value);
    };
    const key = e => {
      if (e.key === 'Escape') close(null);
      else if (e.key === 'Enter' && !opts.multiline) ok();
    };
    const back = el('div.modal-back', { onclick: e => { if (e.target === back) close(null); } },
      el('div.modal', null,
        el('div.modal-title', { text: title }),
        opts.label ? el('div.muted', { text: opts.label, style: 'margin-bottom:6px' }) : null,
        box, err,
        el('div.buttons', { style: 'justify-content:flex-end;margin-top:10px' },
          opts.readonly ? el('button.btn', { text: 'COPY', onclick: () => copy(box.value, opts.what || 'the code') }) : null,
          opts.readonly ? null : el('button.btn', { text: 'CANCEL', onclick: () => close(null) }),
          el('button.btn.primary', { text: opts.ok || 'OK', onclick: ok }))));
    document.body.append(back);
    document.addEventListener('keydown', key);
    box.focus();
    if (!opts.readonly && !opts.multiline) box.select();
    else if (opts.readonly) box.select();
  });
}

/** Quotes a console argument when it contains spaces (same as the desktop app). */
export function token(s) {
  if (s == null) return '';
  s = String(s).trim();
  return s.includes(' ') ? '"' + s.replace(/"/g, '') + '"' : s;
}

/** Splits a console line into tokens (double quotes group words). */
export function splitLine(line) {
  const out = [];
  let cur = '', quoted = false, any = false;
  for (const ch of line || '') {
    if (ch === '"') { quoted = !quoted; any = true; continue; }
    if (/\s/.test(ch) && !quoted) { if (cur.length || any) out.push(cur); cur = ''; any = false; continue; }
    cur += ch;
  }
  if (cur.length || any) out.push(cur);
  return out;
}

// ---------------------------------------------------------------- router
const tabs = { presets: mountPresets, commands: mountCommands, weapons: mountWeapons, traders: mountTraders, hideout: mountHideout, maps: mountMaps };
const view = document.getElementById('view');
let current = null, unmount = null;

async function route() {
  const name = (location.hash.replace('#', '').split('/')[0]) || 'presets';
  const tab = tabs[name] ? name : 'presets';
  if (tab === current) return;
  current = tab;
  document.querySelectorAll('#tabs a').forEach(a => a.classList.toggle('on', a.dataset.tab === tab));
  if (unmount) unmount();
  unmount = null;
  hideTip();
  view.replaceChildren(el('div.loading', { text: 'Loading...' }));
  try {
    unmount = await tabs[tab](view) || null;
  } catch (e) {
    view.replaceChildren(el('div.loading', { text: 'Could not load the data: ' + e.message }));
    console.error(e);
  }
}

window.addEventListener('hashchange', route);
data('defaults.json').then(d => { document.getElementById('gamebuild').textContent = 'game data ' + d.gameBuild; }).catch(() => {});
route();
