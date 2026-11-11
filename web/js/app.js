// IRR Companion web: tab router and shared helpers.
// The data files are the same as the desktop app's (src/Resources), served under data/.
import { mountMaps } from './maps.js';
import { mountTraders } from './traders.js';
import { mountWeapons } from './weapons.js';

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

// ---------------------------------------------------------------- router
const tabs = { maps: mountMaps, traders: mountTraders, weapons: mountWeapons };
const view = document.getElementById('view');
let current = null, unmount = null;

async function route() {
  const name = (location.hash.replace('#', '').split('/')[0]) || 'maps';
  const tab = tabs[name] ? name : 'maps';
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
