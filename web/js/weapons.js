// WEAPONS tab: build a weapon from the parts each slot accepts and get the additem line (same rules as the desktop app).
// Builds are kept in this browser (localStorage) and can be exported / imported as the desktop app's weapons.json.
import { data, el, itemIcon, copy, status } from './app.js?v=dev';

const STORE = 'irr.weaponBuilds';

export async function mountWeapons(view) {
  const W = await data('weapons.json');
  const items = W.items;
  const find = id => (id && items[id]) || null;
  const slot = (it, name) => (it.slots || []).find(s => s.name === name);
  const weaponIds = W.weapons;
  const classes = Object.fromEntries(W.classes.map(c => [c.id, c.name]));

  // ---------------- model (port of src/Weapons.cs)
  const clone = n => JSON.parse(JSON.stringify(n));
  function normalize(node) {
    const it = find(node.item);
    for (const k of Object.keys(node.parts || {})) {
      const s = it && slot(it, k), child = node.parts[k];
      if (!s || s.kind === 'ammo' || !s.items.includes(child.item) || !find(child.item)) delete node.parts[k];
      else normalize(child);
    }
    node.parts = node.parts || {};
  }
  function defaultsOf(w) { return { item: w, parts: clone(items[w].defaults || {}) }; }
  function at(node, path) {
    let n = node;
    for (const k of path) { n = n && n.parts && n.parts[k]; if (!n) return null; }
    return n;
  }
  function overlay(target, old, oldDefaults) {
    const it = find(target.item);
    if (!it || !old) return;
    for (const [k, v] of Object.entries(old.parts || {})) {
      const s = slot(it, k);
      if (!s || s.kind === 'ammo' || !s.items.includes(v.item)) continue;
      const def = oldDefaults && oldDefaults.parts && oldDefaults.parts[k];
      const mine = target.parts[k];
      const wasDefault = def && def.item === v.item;
      if (!wasDefault) target.parts[k] = clone(v);
      else if (mine && mine.item === v.item) overlay(mine, v, def);
    }
    normalize(target);
  }
  function* walk(node, path) {
    const it = find(node.item);
    if (!it) return;
    for (const s of it.slots || []) {
      const c = node.parts[s.name];
      if (!c) continue;
      const p = path.concat(s.name);
      yield [p, c];
      yield* walk(c, p);
    }
  }
  function magOf(b) {
    for (const [, n] of walk(b.weapon, [])) { const it = find(n.item); if (it && it.cat === 'Mag') return n; }
    return null;
  }
  function isDefault(b, path, node) {
    const w = find(b.weapon.item);
    if (!w || !w.defaults || !node) return false;
    const def = defaultsOf(b.weapon.item);
    const d = at(def, path);
    if (!d || d.item !== node.item) return false;
    for (let i = 1; i < path.length; i++) {
      const a = at(def, path.slice(0, i)), c = at(b.weapon, path.slice(0, i));
      if (!a || !c || a.item !== c.item) return false;
    }
    return true;
  }
  function normalizeBuild(b) {
    normalize(b.weapon);
    const w = find(b.weapon.item);
    const ammo = w && (w.slots || []).find(s => s.kind === 'ammo');
    if (!ammo || !ammo.items.includes(b.ammo)) {
      const mag = magOf(b), mi = mag && find(mag.item);
      const magAmmo = mi && (mi.loads || []).map(l => l.ammo).find(Boolean);
      b.ammo = !ammo ? null : ammo.items.includes(magAmmo) ? magAmmo : ammo.items[0] || null;
    }
    b.mags = Math.max(1, b.mags | 0 || 2);
    b.ammoCount = Math.max(0, b.ammoCount | 0);
  }
  function newBuild(w, name) {
    const b = { name, weapon: items[w].defaults ? defaultsOf(w) : { item: w, parts: {} }, mags: 2, ammo: null, ammoCount: 0 };
    normalizeBuild(b);
    return b;
  }
  function changeWeapon(b, w) {
    const old = b.weapon, oldDef = find(old.item) && find(old.item).defaults ? defaultsOf(old.item) : null;
    b.weapon = items[w].defaults ? defaultsOf(w) : { item: w, parts: {} };
    overlay(b.weapon, old, oldDef);
    normalizeBuild(b);
  }
  function loadOf(b, mag) {
    const l = mag.loads || [];
    return l.find(x => x.ammo === b.ammo) || l.find(x => x.give === mag.give) || l[0] || null;
  }
  function lines(b) {
    const order = [], count = {}, missing = [];
    const add = (it, give, n) => {
      if (n <= 0 || !it) return;
      if (!give) { if (!missing.includes(it.name)) missing.push(it.name); return; }
      if (!(give in count)) { order.push(give); count[give] = 0; }
      count[give] += n;
    };
    const w = find(b.weapon.item);
    if (!w) return { lines: [], missing };
    add(w, w.give, 1);
    const mag = magOf(b);
    for (const [p, n] of walk(b.weapon, [])) {
      const it = find(n.item), def = isDefault(b, p, n);
      if (n === mag) { const l = loadOf(b, it); add(it, l ? l.give : it.give, Math.max(0, b.mags - (def ? 1 : 0))); }
      else if (!def) add(it, it.give, 1);
    }
    const ammo = find(b.ammo);
    add(ammo, ammo && ammo.give, b.ammoCount);
    return { lines: order.map(g => [g, count[g]]), missing };
  }
  const command = b => lines(b).lines.map(([g, n]) => 'additem ' + g + ' ' + n).join(' | ');

  // ---------------- storage
  function load() {
    try {
      const arr = JSON.parse(localStorage.getItem(STORE) || '[]');
      return arr.filter(b => b && b.weapon && find(b.weapon.item)).map(b => { normalizeBuild(b); return b; });
    } catch (e) { return []; }
  }
  function save() {
    try { localStorage.setItem(STORE, JSON.stringify(builds)); } catch (e) { status('Could not save in this browser: ' + e.message); }
  }
  let builds = load();
  if (!builds.length) builds.push(newBuild(weaponIds[0], 'My first build'));
  let cur = builds[0];

  // ---------------- UI
  const list = el('div.list');
  const fileInput = el('input', { type: 'file', accept: '.json,application/json', hidden: true });
  const side = el('div.side', null,
    el('div.section', { text: 'My builds' }),
    el('button.btn.primary', { text: '+ NEW BUILD', onclick: () => { const b = newBuild(cur ? cur.weapon.item : weaponIds[0], uniqueName(nameOf(cur ? cur.weapon.item : weaponIds[0]) + ' build')); builds.push(b); cur = b; changed(true); } }),
    el('div', { style: 'height:8px' }), list,
    el('div', { style: 'height:8px' }),
    el('div.buttons', null,
      el('button.btn', { text: 'EXPORT', title: 'Download your builds (weapons.json, the desktop app\'s format)', onclick: exportBuilds }),
      el('button.btn', { text: 'IMPORT', title: 'Add the builds of a weapons.json (desktop app or export)', onclick: () => fileInput.click() })),
    fileInput,
    el('div.hint', { text: 'Only parts that fit the slot are offered, as in the gunsmith. Builds are saved in this browser. COPY COMMAND copies the additem line that gives the whole build (cheat).' }));
  const content = el('div.content');
  view.replaceChildren(side, content);
  fileInput.addEventListener('change', importBuilds);

  const nameOf = id => (find(id) || {}).name || id;
  function uniqueName(base) {
    let n = base;
    for (let i = 2; builds.some(b => b.name === n); i++) n = base + ' ' + i;
    return n;
  }
  function changedParts(b) { let n = 0; for (const [p, x] of walk(b.weapon, [])) if (!isDefault(b, p, x)) n++; return n; }

  function renderList() {
    list.replaceChildren(...builds.map(b => el('div.item' + (b === cur ? '.on' : ''), { onclick: () => { cur = b; render(); } },
      el('div', { style: 'display:flex;gap:10px;align-items:center' },
        el('div.icon', null, itemIcon(b.weapon.item, find(b.weapon.item).cat)),
        el('div', null, el('div.t', { text: b.name }),
          el('div.s', { text: nameOf(b.weapon.item) + ' · ' + (changedParts(b) ? changedParts(b) + ' parts changed' : 'default parts') }))))));
  }

  function changed(rebuild) { save(); if (rebuild) render(); else { renderList(); renderCommand(); } }

  let cmdBox, cmdInfo;
  function renderCommand() {
    if (!cmdBox) return;
    const r = lines(cur);
    cmdBox.value = command(cur);
    cmdInfo.textContent = r.lines.length + ' additem command' + (r.lines.length === 1 ? '' : 's') + ' · the default parts come with the weapon' +
      (r.missing.length ? ' · left out (additem cannot give them): ' + r.missing.join(', ') : '');
    cmdInfo.style.color = r.missing.length ? 'var(--warn)' : '';
  }

  function itemSelect(options, value, onchange, emptyLabel) {
    const s = el('select.input', { onchange: e => onchange(e.target.value || null) });
    if (emptyLabel !== undefined) s.append(el('option', { value: '', text: emptyLabel }));
    for (const id of options) { const it = find(id); if (it) s.append(el('option', { value: id, text: it.name + ((it.slots || []).some(x => x.kind !== 'ammo') ? '  (+' + it.slots.filter(x => x.kind !== 'ammo').length + ' slots)' : '') })); }
    s.value = value || '';
    return s;
  }

  function slotRows(node, depth, path, out) {
    const it = find(node.item);
    if (!it) return;
    for (const s of it.slots || []) {
      if (s.kind === 'ammo' || !s.items.length) continue;
      const child = node.parts[s.name], p = path.concat(s.name);
      const childItem = child && find(child.item);
      const def = isDefault(cur, p, child);
      const sel = itemSelect(s.items, child && child.item, v => {
        const old = node.parts[s.name];
        if (!v) delete node.parts[s.name];
        else if (!old || old.item !== v) { const n = { item: v, parts: old ? old.parts : {} }; normalize(n); node.parts[s.name] = n; }
        changed(true);
      }, '(empty)');
      const extra = [];
      if (childItem && childItem.cat === 'Mag' && magOf(cur) === child) {
        const l = loadOf(cur, childItem), ammo = l && find(l.ammo);
        extra.push(el('input.input', { type: 'number', min: 1, max: 20, value: cur.mags, title: 'How many of this magazine in total, the one on the weapon included', oninput: e => { cur.mags = Math.max(1, +e.target.value || 1); changed(false); } }));
        extra.push(el('span.note', { text: 'magazines' + (def ? ' (1 comes with the weapon)' : '') + (l && l.rounds && ammo ? ' · loaded with ' + l.rounds + ' × ' + ammo.name : ' · empty') }));
      } else if (childItem) {
        if (def) extra.push(el('span.note', { text: 'comes with the weapon', title: 'additem gives the weapon with this part already on it: it is not in the command.' }));
        else if (!childItem.give) extra.push(el('span.note.warn', { text: 'additem cannot give this part' }));
      }
      out.push(el('div.slotrow', { style: 'padding-left:' + depth * 28 + 'px' },
        depth ? el('span.tree', { text: '└' }) : null,
        el('span.lbl' + (child ? '.set' : ''), { text: s.name, title: s.items.length + ' parts fit this slot' }),
        childItem ? el('div.icon', null, itemIcon(child.item, childItem.cat)) : el('div.icon'),
        sel, ...extra));
      if (child) slotRows(child, depth + 1, p, out);
    }
  }

  function render() {
    renderList();
    const w = find(cur.weapon.item);
    const name = el('input.name', { value: cur.name, title: 'Name of the build', oninput: e => { cur.name = e.target.value; changed(false); } });
    const top = el('div.wtop', null, name, el('div.buttons', null,
      el('button.btn.primary', { text: 'COPY COMMAND', onclick: () => copy(command(cur), 'the "' + cur.name + '" build') }),
      el('button.btn', { text: 'DEFAULT PARTS', title: 'Back to the parts the weapon comes with', onclick: () => { cur.weapon = w.defaults ? defaultsOf(w.id || cur.weapon.item) : { item: cur.weapon.item, parts: {} }; normalizeBuild(cur); changed(true); } }),
      el('button.btn', { text: 'DUPLICATE', onclick: () => { const b = clone(cur); b.name = uniqueName(cur.name + ' copy'); builds.splice(builds.indexOf(cur) + 1, 0, b); cur = b; changed(true); } }),
      el('button.btn', { text: 'DELETE', onclick: () => { if (!confirm('Delete the build "' + cur.name + '"?')) return; builds.splice(builds.indexOf(cur), 1); if (!builds.length) builds.push(newBuild(weaponIds[0], 'My first build')); cur = builds[0]; changed(true); } })));
    // base weapon, grouped by class
    const wsel = el('select.input', { onchange: e => { changeWeapon(cur, e.target.value); changed(true); } });
    let group = null, gcls = null;
    for (const id of weaponIds) {
      const it = items[id];
      if (it.cat !== gcls) { gcls = it.cat; group = el('optgroup', { label: classes[gcls] || gcls }); wsel.append(group); }
      group.append(el('option', { value: id, text: it.name }));
    }
    wsel.value = cur.weapon.item;
    const nslots = (w.slots || []).filter(s => s.kind !== 'ammo').length;
    const bar = el('div.weaponbar', null, el('div.big', null, itemIcon(cur.weapon.item, w.cat)),
      el('div', { style: 'flex:1' }, el('label', { text: 'BASE WEAPON' }), wsel,
        el('div.muted', { text: (classes[w.cat] || w.cat) + ' · ' + nslots + ' slots on the weapon · additem ' + (w.give || cur.weapon.item) })));
    const rows = [];
    slotRows(cur.weapon, 0, [], rows);
    const ammoSlot = (w.slots || []).find(s => s.kind === 'ammo');
    const ammoRows = [];
    if (ammoSlot) {
      ammoRows.push(el('div.section', { text: 'Ammo' }), el('p.muted', { text: 'The weapon and the magazines come loaded; add loose rounds if you want more.' }));
      const a = find(cur.ammo);
      ammoRows.push(el('div.slotrow', null, el('span.lbl.set', { text: 'Rounds' }),
        a ? el('div.icon', null, itemIcon(cur.ammo, 'Ammo')) : el('div.icon'),
        itemSelect(ammoSlot.items, cur.ammo, v => { cur.ammo = v; changed(true); }, '(empty)'),
        el('input.input', { type: 'number', min: 0, max: 10000, step: 30, value: cur.ammoCount, oninput: e => { cur.ammoCount = Math.max(0, +e.target.value || 0); changed(false); } }),
        el('span.note', { text: 'loose rounds' })));
    }
    cmdBox = el('textarea.cmd', { readonly: true, title: 'Paste it in the game console (² / ~, ^ or F10) with Ctrl+V.' });
    cmdInfo = el('div.muted');
    content.replaceChildren(top, bar,
      el('div.section', { text: 'Parts' }), el('p.muted', { text: 'Pick a part for each slot. A part can have slots of its own (rails, sights...), shown under it.' }),
      ...rows, ...ammoRows,
      el('div.section', { text: 'Console command' }), cmdInfo, cmdBox);
    renderCommand();
  }

  function exportBuilds() {
    const doc = { format: 'IRRCompanion.WeaponBuilds', version: 1, builds };
    const a = el('a', { href: URL.createObjectURL(new Blob([JSON.stringify(doc, null, 1)], { type: 'application/json' })), download: 'weapons.json' });
    document.body.append(a);
    a.click();
    a.remove();
    status('Builds exported (weapons.json): the desktop app reads the same file in %APPDATA%\\IRR Companion.');
  }

  async function importBuilds() {
    const f = fileInput.files[0];
    fileInput.value = '';
    if (!f) return;
    try {
      const doc = JSON.parse(await f.text());
      const arr = Array.isArray(doc) ? doc : doc.builds;
      let n = 0;
      for (const b of arr || []) {
        if (!b || !b.weapon || !find(b.weapon.item)) continue;
        b.name = uniqueName(b.name || 'Build');
        normalizeBuild(b);
        builds.push(b);
        n++;
      }
      if (n) cur = builds[builds.length - 1];
      changed(true);
      status(n + ' build' + (n === 1 ? '' : 's') + ' imported.');
    } catch (e) { status('Could not import: ' + e.message); }
  }

  render();
}
