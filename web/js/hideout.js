// HIDEOUT tab: what every hideout upgrade costs and needs, the player's own progress, and a shopping list.
// The progress is kept in this browser and can be exported / imported as the desktop app's hideout.json (version 2).
import { data, el, seg, copy, status, stored, downloadJson, pickJson, itemIcon, iconUrl, token } from './app.js?v=dev';

const SHOPPING = 'SHOPPING LIST';
const RES_COLORS = { Concrete: '#aaa69c', Food: '#8bc34a', Metal: '#5da9e9', Oil: '#f0a040' };
const progressStore = stored('irr.hideout', null);
const fmt = v => Math.round(v).toLocaleString('fr-FR');

/** Same model as the desktop app: free first levels (the starting state) are hidden and the levels renumbered. */
function prepare(d) {
  const units = d.units.map(u => ({
    name: u.name, tag: u.tag, startLevel: u.startLevel, free: 0,
    levels: u.levels.map(l => ({
      level: l.level, cash: l.cash || 0, description: l.description || '',
      conditions: (l.conditions || []).map(c => ({ ...c })), items: l.items || [], resources: l.resources || {},
    })),
  }));
  const isFree = l => !l.cash && !l.items.length && !Object.keys(l.resources).length && !l.conditions.length;
  for (const u of units) {
    while (u.levels.some(l => l.level === u.free + 1 && isFree(l))) u.free++;
    if (!u.free) continue;
    u.levels = u.levels.filter(l => l.level > u.free);
    for (const l of u.levels) l.level -= u.free;
    u.startLevel = Math.max(0, u.startLevel - u.free);
  }
  const find = n => units.find(u => u.name.toLowerCase() === (n || '').toLowerCase());
  for (const u of units) for (const l of u.levels) {
    for (const c of l.conditions) if (c.type === 'unit') { const t = find(c.unit); if (t) c.level -= t.free; }
    l.conditions = l.conditions.filter(c => !(c.type === 'unit' && c.level <= 0)); // met from the start
  }
  for (const u of units) { u.max = u.levels.reduce((m, l) => Math.max(m, l.level), 0); u.isFree = isFree; }
  return { units, find, isFree, resources: d.resources, factions: d.factions };
}

export async function mountHideout(view) {
  const H = prepare(await data('hideout.json'));
  const factionName = abbr => (H.factions.find(f => f.abbr === abbr) || {}).name || abbr;

  // ---------------- progress
  const p = { playerLevel: 1, loyalty: {}, units: {} };
  function readProgress(root) {
    if (!root || typeof root !== 'object') return false;
    const gameLevels = (root.version || 1) < 2; // version 1 stored the game levels
    p.playerLevel = Math.max(1, Math.round(root.playerLevel || 1));
    p.loyalty = {};
    for (const [k, v] of Object.entries(root.loyalty || {})) p.loyalty[k] = Math.round(+v || 0);
    p.units = {};
    for (const [k, v] of Object.entries(root.units || {})) {
      const u = H.find(k);
      p.units[k] = gameLevels && u ? Math.max(0, Math.round(v) - u.free) : Math.round(+v || 0);
    }
    return true;
  }
  readProgress(progressStore.load());
  const toDoc = () => ({ format: 'IRRCompanion.Hideout', version: 2, playerLevel: p.playerLevel, loyalty: p.loyalty, units: p.units });
  const save = () => progressStore.save(toDoc());
  const levelOf = name => name in p.units ? p.units[name] : ((H.find(name) || {}).startLevel || 0);
  const loyaltyOf = f => p.loyalty[f] || 0;
  const isMet = c => c.type === 'unit' ? levelOf(c.unit) >= c.level : c.type === 'faction' ? loyaltyOf(c.faction) >= c.level
    : c.type === 'player' ? p.playerLevel >= c.level : false;
  const nextLevel = u => u.levels.find(l => l.level === levelOf(u.name) + 1);
  const condText = c => c.type === 'unit' ? c.unit + ' level ' + c.level : c.type === 'faction' ? c.faction + ' loyalty level ' + c.level
    : c.type === 'player' ? 'Player level ' + c.level : c.type;

  // ---------------- console commands (GIVE buttons)
  const giveItem = (it, n) => 'irr.Item.Give ' + token(it.id || it.tag) + ' ' + n;
  const giveCash = n => 'irr.Item.Give Cash ' + n;
  const giveRes = (r, n) => 'irr.Item.AddResource ' + r + ' ' + n;
  const giveBtn = cmd => el('button.btn.small', { text: 'GIVE', title: 'Copies "' + cmd + '": paste it in the game console (cheat).', onclick: () => copy(cmd, '"' + cmd + '"') });

  // ---------------- layout
  let shown = SHOPPING, nextOnly = true;
  const num = (value, min, max, onchange, title) => {
    const i = el('input.input.num', { type: 'number', min, max, value, title });
    i.addEventListener('change', () => {
      const v = Math.max(min, Math.min(max, Math.round(+i.value || 0)));
      i.value = v;
      onchange(v);
      changed();
    });
    return i;
  };
  const progress = el('div.progress');
  const units = el('div.list');
  const side = el('div.side', null,
    el('div.section', { text: 'My progress' }), progress,
    el('div.section', { text: 'Hideout' }), units,
    el('div', { style: 'height:10px' }),
    el('div.buttons', null,
      el('button.btn', { text: 'EXPORT', title: 'Download your progress (hideout.json, the desktop app\'s format)', onclick: exportProgress }),
      el('button.btn', { text: 'IMPORT', title: 'Load the progress of a hideout.json (desktop app or export)', onclick: importProgress })),
    el('div.hint', { text: 'Set the level of each unit to follow your progress (saved in this browser). GIVE copies a console command (cheat).' }));
  const content = el('div.content');
  view.replaceChildren(side, content);

  function renderProgress() {
    progress.replaceChildren(
      el('div.prow', { title: 'Your player level (some upgrades need a minimum level).' },
        el('span', { text: 'Player level' }), num(p.playerLevel, 1, 100, v => { p.playerLevel = v; }, 'Your player level')),
      el('div.muted', { text: 'Faction loyalty', style: 'margin-top:8px' }),
      ...H.factions.map(f => el('div.prow', { title: f.name + ': your loyalty level with this faction.' },
        el('span', { text: '   ' + f.abbr }), num(loyaltyOf(f.abbr), 0, 10, v => { p.loyalty[f.abbr] = v; }, f.name))));
  }

  function stateOf(u) {
    const n = nextLevel(u);
    return !n ? 'max' : n.conditions.every(isMet) ? 'ready' : 'locked';
  }
  function renderUnits() {
    units.replaceChildren(
      el('div.item.shop' + (shown === SHOPPING ? '.on' : ''), { onclick: () => show(SHOPPING) }, el('div.t', { text: '★  ' + SHOPPING })),
      ...H.units.map(u => {
        const lvl = levelOf(u.name), st = stateOf(u);
        return el('div.item' + (shown === u ? '.on' : ''), { onclick: () => show(u) },
          el('div.t.split', null, el('span', { text: u.name.toUpperCase() }), el('span.s', { text: 'Level ' + lvl + ' / ' + u.max })),
          el('div.s.' + st, { text: st === 'max' ? 'Fully upgraded' : st === 'ready' ? 'Can upgrade to level ' + (lvl + 1) : 'Level ' + (lvl + 1) + ' locked' }));
      }));
  }

  function changed() {
    save();
    renderUnits();
    renderContent(false);
  }
  function show(what) {
    shown = what;
    renderUnits();
    renderContent(true);
  }

  // ---------------- content
  const res = r => el('span.res', { style: '--c:' + RES_COLORS[r] + ';-webkit-mask-image:url(' + iconUrl('res_' + r) + ');mask-image:url(' + iconUrl('res_' + r) + ')' });
  function line(icon, name, detail, value, opts = {}) {
    return el('div.hrow', { title: opts.title || null },
      el('div.icon', null, icon),
      el('div.name', { style: opts.color ? 'color:' + opts.color : null }, opts.link ? el('a', { href: '#hideout', text: name, onclick: e => { e.preventDefault(); opts.link(); } }) : name,
        detail ? el('small', { text: detail }) : null),
      el('div.val', { text: value || '', style: opts.valueColor ? 'color:' + opts.valueColor : null }),
      el('div', null, opts.give || null));
  }

  function levelCard(u, lv, cur) {
    const built = lv.level <= cur, next = lv.level === cur + 1, ready = lv.conditions.every(isMet);
    const state = built ? 'BUILT ✓' : next ? (ready ? 'NEXT UPGRADE - REQUIREMENTS MET' : 'NEXT UPGRADE - LOCKED') : 'LATER';
    const cls = built ? 'built' : next ? (ready ? 'ready' : 'locked') : 'later';
    const rows = [el('div.head', null, el('span.t', { text: 'LEVEL ' + lv.level }), el('span.state.' + cls, { text: state }))];
    if (lv.description) rows.push(el('p.desc', { text: lv.description }));
    if (u.isFree(lv)) { rows.push(el('p.desc', { text: 'No cost, no requirement.' })); return el('div.card.hcard.' + cls, null, rows); }
    if (lv.conditions.length) {
      rows.push(el('div.caption', { text: 'REQUIREMENTS' }));
      for (const c of lv.conditions) {
        const ok = isMet(c);
        const have = 'yours: ' + (c.type === 'unit' ? levelOf(c.unit) : c.type === 'faction' ? loyaltyOf(c.faction) : p.playerLevel);
        const target = c.type === 'unit' ? H.find(c.unit) : null;
        rows.push(line(el('span.mark', { text: ok ? '✓' : '✗' }), condText(c), have, '', {
          color: ok ? 'var(--higher)' : 'var(--lower)', title: c.type === 'faction' ? factionName(c.faction) : null,
          link: target ? () => show(target) : null,
        }));
      }
    }
    if (lv.items.length) {
      rows.push(el('div.caption', { text: 'ITEMS' }));
      for (const it of lv.items) rows.push(line(itemIcon(it.id), it.name, null, '× ' + it.count, { title: it.tag, give: giveBtn(giveItem(it, it.count)) }));
    }
    rows.push(el('div.caption', { text: 'MONEY AND MATERIALS' }));
    if (lv.cash > 0) rows.push(line(itemIcon('Cash'), 'Cash', null, fmt(lv.cash), { color: 'var(--changed)', valueColor: 'var(--changed)', give: giveBtn(giveCash(lv.cash)) }));
    for (const r of H.resources) if (r in lv.resources)
      rows.push(line(res(r), r, null, fmt(lv.resources[r]), { valueColor: RES_COLORS[r], give: giveBtn(giveRes(r, lv.resources[r])) }));
    return el('div.card.hcard.' + cls, null, rows);
  }

  function unitView(u) {
    const cur = levelOf(u.name);
    const labels = Array.from({ length: u.max + 1 }, (_, i) => String(i));
    return [
      el('h1', { text: u.name.toUpperCase() }),
      el('div.levelbar', null, el('b', { text: 'MY LEVEL' }),
        seg(labels, cur, i => { p.units[u.name] = i; status(u.name + ' set to level ' + i + '.'); changed(); }, { title: 'The level of this unit in your hideout. 0 = not built yet.' }),
        u.free ? el('span.muted', { text: 'Built from the start. Level 1 = first upgrade (level ' + (u.free + 1) + ' in the game files).',
          title: 'The game files have a free level 1 for this unit (its starting state). It is not shown, so the levels start at the first upgrade you pay for.' }) : null),
      ...u.levels.map(lv => levelCard(u, lv, cur)),
    ];
  }

  function totals() {
    const t = { items: [], resources: {}, cash: 0, upgrades: 0 };
    for (const u of H.units) {
      const cur = levelOf(u.name);
      for (const lv of u.levels.filter(l => l.level > cur).sort((a, b) => a.level - b.level)) {
        if (nextOnly && lv.level !== cur + 1) break;
        t.upgrades++;
        t.cash += lv.cash;
        for (const [k, v] of Object.entries(lv.resources)) t.resources[k] = (t.resources[k] || 0) + v;
        for (const it of lv.items) {
          let l = t.items.find(x => x.item.tag === it.tag);
          if (!l) t.items.push(l = { item: it, count: 0, for: [] });
          l.count += it.count;
          l.for.push(u.name + ' ' + lv.level);
        }
      }
    }
    t.items.sort((a, b) => a.item.name.localeCompare(b.item.name));
    return t;
  }

  function shoppingView() {
    const t = totals();
    const out = [
      el('h1', { text: SHOPPING }),
      el('div.levelbar', null,
        seg(['NEXT UPGRADES', 'EVERYTHING LEFT'], nextOnly ? 0 : 1, i => { nextOnly = i === 0; renderContent(true); },
          { title: 'NEXT UPGRADES: the next level of every unit. EVERYTHING LEFT: all the levels you have not built yet.' }),
        el('span.muted', { text: t.upgrades === 0 ? 'Nothing left: your hideout is fully upgraded.'
          : t.upgrades + ' upgrade' + (t.upgrades > 1 ? 's' : '') + ' · based on the levels set in each unit' })),
    ];
    if (!t.upgrades) return out;
    out.push(el('div.card.hcard', null,
      el('div.caption', { text: 'MONEY AND MATERIALS' }),
      line(itemIcon('Cash'), 'Cash', null, fmt(t.cash), { color: 'var(--changed)', valueColor: 'var(--changed)', give: t.cash > 0 ? giveBtn(giveCash(t.cash)) : null }),
      ...H.resources.map(r => { const v = t.resources[r] || 0; return line(res(r), r, null, fmt(v), { valueColor: RES_COLORS[r], give: v > 0 ? giveBtn(giveRes(r, v)) : null }); })));
    out.push(el('div.card.hcard', null,
      el('div.caption', { text: 'ITEMS (' + t.items.length + ')' }),
      ...t.items.map(l => line(itemIcon(l.item.id), l.item.name, 'for ' + l.for.join(', '), '× ' + l.count,
        { title: l.item.tag + '\nfor ' + l.for.join(', '), give: giveBtn(giveItem(l.item, l.count)) }))));
    return out;
  }

  function renderContent(resetScroll) {
    const top = content.scrollTop;
    content.replaceChildren(...(shown === SHOPPING ? shoppingView() : unitView(shown)));
    content.scrollTop = resetScroll ? 0 : top;
  }

  function exportProgress() {
    downloadJson(toDoc(), 'hideout.json');
    status('Progress exported (hideout.json): the desktop app reads the same file in %APPDATA%\\IRR Companion.');
  }
  async function importProgress() {
    try {
      const doc = await pickJson();
      if (!doc) return;
      if (doc.format && doc.format !== 'IRRCompanion.Hideout') throw new Error('this is not a hideout progress file (' + doc.format + ').');
      if (!confirm('Replace your progress in this browser with the one of the file?')) return;
      readProgress(doc);
      save();
      renderProgress();
      renderUnits();
      renderContent(true);
      status('Hideout progress imported.');
    } catch (e) { status('Import failed: ' + e.message); }
  }

  renderProgress();
  renderUnits();
  renderContent(true);
}
