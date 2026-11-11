// PRESETS tab: AI difficulty / simulation preset editor, game share codes (IRR1-...) and comparison.
// Same model as the desktop app (Preset.cs, ShareCode.cs). The presets are kept in this browser and can be exported /
// imported as the desktop app's presets.json.
import { data, el, seg, copy, status, stored, downloadJson, pickJson, dialog } from './app.js';

const presetStore = stored('irr.presets', []);
const kept = {};
const clone = o => JSON.parse(JSON.stringify(o));
const getPath = (root, path) => path.split('.').reduce((o, k) => (o && typeof o === 'object' ? o[k] : undefined), root);
function setPath(root, path, v) {
  const parts = path.split('.');
  let cur = root;
  for (const k of parts.slice(0, -1)) { if (!cur[k] || typeof cur[k] !== 'object') cur[k] = {}; cur = cur[k]; }
  cur[parts[parts.length - 1]] = v;
}
const same = (a, b) => typeof a === 'number' && typeof b === 'number' ? Math.abs(a - b) < 1e-9 : a === b;
function overlay(target, src) {
  for (const [k, v] of Object.entries(src)) {
    if (v && typeof v === 'object' && !Array.isArray(v) && target[k] && typeof target[k] === 'object' && !Array.isArray(target[k])) overlay(target[k], v);
    else target[k] = clone(v);
  }
}
const now = () => { const d = new Date(), p = n => String(n).padStart(2, '0'); return d.getUTCFullYear() + '.' + p(d.getUTCMonth() + 1) + '.' + p(d.getUTCDate()) + '-' + p(d.getUTCHours()) + '.' + p(d.getUTCMinutes()) + '.' + p(d.getUTCSeconds()); };
export function makeId(name) {
  let s = '';
  for (const c of (name || '').trim().toLowerCase()) {
    if (/[a-z0-9]/.test(c)) s += c;
    else if (s && !s.endsWith('_')) s += '_';
  }
  s = s.replace(/^_+|_+$/g, '');
  return s || 'preset';
}

// ---------------------------------------------------------------- share codes
const ALPHA = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ';
export function checksum(payload) {
  let h = 5381;
  for (let i = 0; i < payload.length; i++) h = ((Math.imul(h, 33) >>> 0) ^ payload.charCodeAt(i)) >>> 0;
  let s = '';
  for (let i = 0; i < 4; i++) { s += ALPHA[h % 36]; h = Math.floor(h / 36); }
  return s;
}
export function encodeCode(doc) {
  const bytes = new TextEncoder().encode(JSON.stringify(doc));
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  const payload = btoa(bin).replace(/=+$/, '');
  return 'IRR1-' + payload + '-' + checksum(payload);
}
export function decodeCode(code) {
  code = (code || '').replace(/\s+/g, '');
  if (!code) throw new Error('Paste a code first.');
  if (!code.startsWith('IRR')) throw new Error('That is not a share code: it should start with IRR1-.');
  const first = code.indexOf('-'), last = code.lastIndexOf('-');
  if (first < 0 || last <= first) throw new Error('This code is damaged (it was probably cut short when copied).');
  const head = code.slice(0, first), payload = code.slice(first + 1, last), chk = code.slice(last + 1);
  if (head === 'IRR2') throw new Error('IRR2 codes are not supported yet by this tool (only IRR1).');
  if (head !== 'IRR1') throw new Error('Unknown share code version \'' + head + '\'.');
  if (checksum(payload) !== chk) throw new Error('This code is damaged (checksum mismatch). It was probably cut short when copied.');
  let b64 = payload.replace(/-/g, '+').replace(/_/g, '/');
  b64 += '='.repeat((4 - b64.length % 4) % 4);
  let obj;
  try { obj = JSON.parse(new TextDecoder().decode(Uint8Array.from(atob(b64), c => c.charCodeAt(0)))); }
  catch (e) { throw new Error('This code is damaged (its content cannot be read).'); }
  if (!obj || typeof obj !== 'object' || Array.isArray(obj)) throw new Error('This code does not contain a preset.');
  return obj;
}

export async function mountPresets(view) {
  const G = await data('defaults.json');
  const params = G.params.map(p => ({ ...p, sim: p.cat === 'Simulation', bool: p.type === 'bool', precision: p.precision ?? 2 }));
  for (const p of params) {
    p.viewMin = p.uiMin ?? p.min ?? 0;
    p.viewMax = p.uiMax ?? p.max ?? 1;
    p.hardMin = p.min ?? Math.min(0, p.viewMin);
    p.hardMax = p.max ?? Math.max(p.viewMax * 4, 1);
    p.inc = p.step ?? Math.pow(10, -p.precision);
  }
  const fmtV = (p, v) => v == null ? '—' : typeof v === 'boolean' ? (v ? 'On' : 'Off') : typeof v === 'number' ? v.toFixed(Math.max(p.precision, 0)) : String(v);
  const tooltip = p => {
    let t = p.name;
    if (p.desc) t += '\n\n' + p.desc;
    if (p.warn) t += '\n\n⚠ ' + p.warn;
    if (p.advanced) t += '\n\nNot in the game\'s settings menu.' + (p.unofficial ? ' Name and description written by IRR Companion from the game key and values, not by the developers.' : '');
    if (!p.bool) {
      t += '\n\nRecommended range: ' + fmtV(p, p.viewMin) + ' – ' + fmtV(p, p.viewMax);
      if (p.min != null || p.max != null) t += '\nAllowed: ' + (p.min != null ? fmtV(p, p.min) : '…') + ' – ' + (p.max != null ? fmtV(p, p.max) : '…');
    }
    return t + '\n\nGame key: ' + p.key;
  };

  // ---------------- model
  const get = (pr, p) => getPath(p.sim ? pr.sim : pr.ai, p.docKey);
  const simLevel = pr => G.simOrder.find(n => params.every(p => !p.sim || same(get(pr, p), getPath(G.sim[n], p.docKey)))) || null;
  const defaultOther = () => ({ weapons: 'Normal', barks: 'Normal', global: 'Normal', menuStyle: 'Normal' });
  const fromGame = (aiName, simName) => {
    const label = G.aiLabels[aiName] || aiName;
    return {
      name: label, author: 'Game', builtIn: true, version: 1, savedAt: '', other: defaultOther(), otherVars: [],
      description: 'In-game difficulty "' + label + '" (file ' + aiName + '.json), with simulation "' + simName + '". Shipped with the game build of ' + G.gameBuild + '.',
      ai: clone(G.ai[aiName]), sim: clone(G.sim[simName]),
    };
  };
  function toShare(pr) {
    const id = makeId(pr.name), sl = simLevel(pr);
    const sel = { aISettings: id, weapons: pr.other.weapons || 'Normal', barks: pr.other.barks || 'Normal', simulation: sl || id, global: pr.other.global || 'Normal', menuStyle: pr.other.menuStyle || 'Normal' };
    for (const [k, v] of Object.entries(pr.other)) if (!(k in sel)) sel[k] = v;
    const vars = [{ c: 'AISettings', n: id, hv: G.aiHardVersion, doc: { ...clone(pr.ai), schemaSoftVersion: G.aiSoftVersion, schemaHardVersion: G.aiHardVersion } }];
    if (!sl) vars.push({ c: 'Simulation', n: id, hv: G.simHardVersion, doc: { ...clone(pr.sim), schemaSoftVersion: G.simSoftVersion, schemaHardVersion: G.simHardVersion } });
    for (const v of pr.otherVars) vars.push(clone(v));
    return {
      t: 'p', id,
      preset: { presetId: id, displayName: pr.name, description: pr.description || '', author: pr.author || '', version: pr.version || 1, savedAt: pr.savedAt || now(), selections: sel },
      vars,
    };
  }
  function fromShare(root) {
    if (!root || root.t !== 'p') throw new Error('This code is not a preset code.');
    const meta = root.preset || {}, sel = meta.selections || {};
    const pr = {
      name: meta.displayName || meta.presetId || root.id || 'Imported preset', author: meta.author || '', description: meta.description || '',
      savedAt: meta.savedAt || '', version: meta.version || 1, builtIn: false, otherVars: [],
    };
    let aiDoc = null, simDoc = null;
    for (const v of root.vars || []) {
      if (!v || typeof v !== 'object') continue;
      if (v.c === 'AISettings' && !aiDoc) aiDoc = v.doc;
      else if (v.c === 'Simulation' && !simDoc) simDoc = v.doc;
      else pr.otherVars.push(clone(v));
    }
    // start from the full built-in document so every key exists, then apply the code
    pr.ai = clone(G.ai[sel.aISettings] || G.ai.Normal);
    if (aiDoc) overlay(pr.ai, aiDoc);
    pr.sim = clone(G.sim[sel.simulation] || G.sim.Normal);
    if (simDoc) overlay(pr.sim, simDoc);
    for (const d of [pr.ai, pr.sim]) { delete d.schemaSoftVersion; delete d.schemaHardVersion; }
    pr.other = {};
    for (const [k, v] of Object.entries(sel)) if (k !== 'aISettings' && k !== 'simulation') pr.other[k] = v;
    for (const [k, v] of Object.entries(defaultOther())) if (!(k in pr.other)) pr.other[k] = v;
    return pr;
  }
  const markSaved = pr => { pr.snapshot = JSON.stringify(toShare(pr)); pr.dirty = false; };
  const cloneAs = (pr, name) => {
    const c = fromShare(toShare(pr));
    c.name = name;
    if (pr.author === 'Game') c.author = '';
    c.dirty = true;
    return c;
  };

  // kept while the page is open, so unsaved edits survive a switch to another tab
  if (!kept.presets) {
    kept.presets = G.aiOrder.map(n => fromGame(n, 'Normal'));
    for (const o of presetStore.load() || []) {
      try { const pr = fromShare(o); markSaved(pr); kept.presets.push(pr); } catch (e) { /* skip unreadable entry */ }
    }
  }
  const presets = kept.presets;
  const mine = () => presets.filter(p => !p.builtIn);
  const persist = () => presetStore.save(mine().filter(p => p.snapshot).map(p => JSON.parse(p.snapshot)));
  const regular = () => presets.find(p => p.builtIn && p.name === G.aiLabels.Normal) || presets[0];
  const uniqueName = base => {
    let n = base;
    for (let i = 2; mine().some(p => p.name === n || makeId(p.name) === makeId(n)); i++) n = base + ' ' + i;
    return n;
  };

  // ---------------- layout
  let cur = presets.includes(kept.cur) ? kept.cur : presets[0], refChoice = null, onlyChanged = false, mode = 'edit';
  let cmpA = null, cmpB = null, onlyDiff = false, cmpAdvanced = false;
  const list = el('div.list');
  const side = el('div.side', null,
    el('div.section', { text: 'Presets' }),
    el('div.buttons', null,
      el('button.btn.primary', { text: '+ NEW', title: 'New preset from the game\'s Regular difficulty', onclick: newPreset }),
      el('button.btn', { text: 'IMPORT CODE', title: 'Import a share code (IRR1-...) copied from the game or a friend', onclick: importCode })),
    el('div', { style: 'height:8px' }), list,
    el('div', { style: 'height:10px' }),
    el('div.buttons', null,
      el('button.btn', { text: 'EXPORT', title: 'Download your presets (presets.json, the desktop app\'s format)', onclick: exportPresets }),
      el('button.btn', { text: 'IMPORT', title: 'Add the presets of a presets.json (desktop app or export)', onclick: importPresets })),
    el('div.hint', { text: 'Edit a preset, then COPY CODE and paste it in the game\'s preset import. Your presets are saved in this browser; nothing is written to the game.' }));
  const content = el('div.content');
  view.replaceChildren(side, content);

  function renderList() {
    const item = p => el('div.item.preset' + (p === cur && mode === 'edit' ? '.on' : '') + (p.builtIn ? '.builtin' : ''), { onclick: () => { cur = p; mode = 'edit'; render(); } },
      el('span', { text: p.name + (p.dirty ? '  ●' : '') }));
    const m = mine();
    list.replaceChildren(
      el('div.group', { text: 'GAME DIFFICULTIES' }), ...presets.filter(p => p.builtIn).map(item),
      el('div.group', { text: 'MY PRESETS' }), ...(m.length ? m.map(item) : [el('div.muted', { style: 'padding:4px 12px', text: '(none yet – duplicate a preset)' })]));
  }

  const confirmLeave = e => { if (mine().some(p => p.dirty)) { e.preventDefault(); e.returnValue = ''; } };
  window.addEventListener('beforeunload', confirmLeave);

  // ---------------- editor
  let rows = [], infoEl, advHint, perfHint, diffSeg, simSeg, saveBtn;
  const refPreset = () => refChoice && refChoice !== cur && presets.includes(refChoice) ? refChoice : null;

  function paramRow(p) {
    const ro = cur.builtIn;
    const refEl = el('span.ref');
    const nameEl = el('span.pname', { text: p.name.toUpperCase(), title: tooltip(p) });
    const row = { p, el: null, refEl };
    let ctrl;
    if (p.bool) {
      const holder = el('div.pctl');
      const draw = () => holder.replaceChildren(seg(['OFF', 'ON'], get(cur, p) ? 1 : 0, i => { change(p, i === 1); draw(); }, { disabled: ro, small: true }));
      draw();
      ctrl = holder;
      row.sync = draw;
    } else {
      const range = el('input.range', { type: 'range', min: p.viewMin, max: p.viewMax, step: p.inc, disabled: ro || undefined });
      const box = el('input.input.num', { title: 'Type an exact value and press Enter.', readonly: ro || undefined, inputmode: 'decimal' });
      const show = () => { const v = get(cur, p); box.value = fmtV(p, v); range.value = typeof v === 'number' ? v : p.viewMin; box.classList.toggle('out', typeof v === 'number' && (v < p.viewMin - 1e-9 || v > p.viewMax + 1e-9)); };
      range.addEventListener('input', () => { change(p, +(+range.value).toFixed(Math.max(p.precision, 0) + 2)); show(); });
      const commit = () => {
        if (ro) return;
        let v = parseFloat(box.value.trim().replace(',', '.').replace(/\s/g, ''));
        if (!isFinite(v)) return show();
        v = Math.max(p.hardMin, Math.min(p.hardMax, v));
        if (!same(v, get(cur, p))) change(p, v);
        show();
      };
      box.addEventListener('keydown', e => { if (e.key === 'Enter') commit(); else if (e.key === 'Escape') show(); });
      box.addEventListener('blur', commit);
      show();
      ctrl = el('div.pctl', null, range, box);
      row.sync = show;
    }
    row.el = el('div.prm', null, el('div.ptop', null, nameEl, refEl), ctrl);
    rows.push(row);
    return row.el;
  }

  function change(p, v) {
    if (cur.builtIn) return;
    setPath(p.sim ? cur.sim : cur.ai, p.docKey, v);
    cur.dirty = true;
    afterChange(p);
  }
  function afterChange(only) {
    refreshChoices();
    refreshRefs(only);
    if (!only) rows.forEach(r => r.sync());
    applyFilter();
    updateInfo();
    renderList();
  }

  function refreshRefs(only) {
    const ref = refPreset();
    for (const r of rows) {
      if (only && r.p !== only) continue;
      if (!ref) { r.refEl.textContent = ''; continue; }
      const rv = get(ref, r.p), v = get(cur, r.p);
      let txt = ref.name + ' ' + fmtV(r.p, rv), cls = 'eq';
      if (!same(rv, v)) {
        if (typeof v === 'number' && typeof rv === 'number') { const up = v > rv; txt = (up ? '▲ above ' : '▼ below ') + txt; cls = up ? 'up' : 'down'; }
        else { txt = '● ' + txt; cls = 'chg'; }
      } else txt = '= ' + txt;
      r.refEl.textContent = txt;
      r.refEl.className = 'ref ' + cls;
    }
  }

  function applyFilter() {
    const ref = refPreset(), on = onlyChanged && ref;
    for (const sec of content.querySelectorAll('.psec')) {
      let any = false;
      for (const r of rows) if (sec.contains(r.el)) {
        const shown = !on || !same(get(cur, r.p), get(ref, r.p));
        r.el.hidden = !shown;
        any = any || shown;
      }
      sec.hidden = !any;
    }
  }

  function refreshChoices() {
    if (!diffSeg) return;
    const sl = simLevel(cur);
    const di = G.aiOrder.findIndex(n => params.every(p => p.sim || same(get(cur, p), getPath(G.ai[n], p.docKey))));
    const mark = (box, i) => [...box.children].forEach((b, j) => b.classList.toggle('on', j === i));
    mark(diffSeg, di < 0 ? G.aiOrder.length : di);
    mark(simSeg, sl ? G.simOrder.indexOf(sl) : G.simOrder.length);
  }

  function updateInfo() {
    const ref = refPreset();
    const differ = f => !ref ? 0 : params.filter(p => f(p) && !same(get(cur, p), get(ref, p))).length;
    const parts = [];
    if (cur.builtIn) parts.push('Game difficulty');
    else {
      let best = Infinity, closest = null;
      for (const n of G.aiOrder) {
        const k = params.filter(p => !p.sim && !same(get(cur, p), getPath(G.ai[n], p.docKey))).length;
        if (k < best) { best = k; closest = G.aiLabels[n]; }
      }
      parts.push(best === 0 ? 'Same values as ' + closest : 'Closest game difficulty: ' + closest);
    }
    if (ref) { const n = differ(() => true); parts.push(n === 0 ? 'same values as ' + ref.name : n + (n === 1 ? ' setting differs' : ' settings differ') + ' from ' + ref.name); }
    infoEl.replaceChildren(parts.join(' · '), !cur.builtIn && cur.dirty ? el('span', { style: 'color:var(--changed)', text: '  · unsaved changes' }) : '');
    const adv = params.filter(p => p.advanced).length, advD = differ(p => p.advanced), perfD = differ(p => p.sim);
    advHint.textContent = adv + ' settings the game menu does not show' + (advD ? ' · ' + advD + ' differ' : '') + ' · change them only if you know what they do';
    const sl = simLevel(cur);
    perfHint.textContent = 'AI simulation: ' + (sl ? sl.toUpperCase() : 'CUSTOM') + ' · optimization ranges' + (perfD ? ' · ' + perfD + ' differ' : '');
    if (saveBtn) saveBtn.textContent = cur.dirty ? 'SAVE *' : 'SAVE';
  }

  function sections(ps) {
    const out = [];
    let sec = null, name = null;
    for (const p of ps) {
      if (p.section !== name) { name = p.section; sec = el('div.psec', null, el('div.section', { text: name })); out.push(sec); }
      sec.append(paramRow(p));
    }
    return out;
  }

  function editView() {
    rows = [];
    const ro = cur.builtIn;
    const nameBox = el('input.name', { value: cur.name, readonly: ro || undefined, title: 'Preset name, shown in the game. Its ID (used by the game as folder name) is derived from it.' });
    nameBox.addEventListener('input', () => { cur.name = nameBox.value; cur.dirty = true; updateInfo(); renderList(); });
    nameBox.addEventListener('change', () => renderEditor(true));
    saveBtn = el('button.btn.primary', { text: 'SAVE', disabled: ro || undefined, title: 'Save this preset in this browser. Nothing is written to the game.', onclick: saveCurrent });
    const more = el('select.input.more', { title: 'More actions' },
      el('option', { value: '', text: 'More…' }),
      el('option', { value: 'view', text: 'View share code' }),
      el('option', { value: 'dup', text: 'Duplicate' }),
      ro ? null : el('option', { value: 'info', text: 'Author and description…' }),
      ro ? null : el('option', { value: 'revert', text: 'Revert unsaved changes' }),
      ro ? null : el('option', { value: 'del', text: 'Delete' }));
    more.addEventListener('change', () => { const v = more.value; more.value = ''; ({ view: viewCode, dup: duplicate, info: editInfo, revert, del: remove })[v]?.(); });
    const refSel = el('select.input', { title: 'Reference values shown next to each setting (▲ higher, ▼ lower).' },
      el('option', { value: '-1', text: 'None' }), ...presets.map((p, i) => p === cur ? null : el('option', { value: i, text: p.name })));
    refSel.value = refPreset() ? String(presets.indexOf(refChoice)) : '-1';
    refSel.addEventListener('change', () => { refChoice = refSel.value === '-1' ? null : presets[+refSel.value]; refreshRefs(); applyFilter(); updateInfo(); });
    const only = el('input', { type: 'checkbox' });
    only.checked = onlyChanged;
    only.addEventListener('change', () => { onlyChanged = only.checked; applyFilter(); });
    infoEl = el('div.muted');
    advHint = el('span.ghint');
    perfHint = el('span.ghint');

    diffSeg = seg([...G.aiOrder.map(n => G.aiLabels[n].toUpperCase()), 'CUSTOM'], -1, i => {
      if (ro || i >= G.aiOrder.length) return;
      cur.ai = clone(G.ai[G.aiOrder[i]]);
      cur.dirty = true;
      afterChange();
      status('Loaded the values of the game difficulty "' + G.aiLabels[G.aiOrder[i]] + '". Use Revert to undo.');
    }, { disabled: ro });
    simSeg = seg([...G.simOrder.map(n => n.toUpperCase()), 'CUSTOM'], -1, i => {
      if (ro || i >= G.simOrder.length) return;
      cur.sim = clone(G.sim[G.simOrder[i]]);
      cur.dirty = true;
      afterChange();
    }, { disabled: ro });

    const group = (title, hint, children) => el('details.group', null, el('summary', null, el('span.gt', { text: title }), hint), ...children);
    const out = [
      el('div.wtop', null, nameBox, el('div.buttons', null, saveBtn,
        el('button.btn', { text: 'COPY CODE', title: 'Copy the share code, then paste it in the game\'s preset import.', onclick: copyCode }),
        el('button.btn', { text: 'COMPARE', title: 'Compare this preset with another one, setting by setting.', onclick: openCompare }), more)),
      infoEl,
      el('div.refbar', null, el('span.muted', { text: 'Compare values to' }), refSel, el('label', { title: 'Show only the settings that differ from the preset chosen in "Compare values to".' }, only, ' Only changed settings')),
    ];
    if (ro) out.push(el('div.banner', null, el('b', { text: 'Game difficulty – read only. Make your own copy to change it.' }),
      el('button.btn.primary', { text: 'MAKE MY OWN COPY', onclick: duplicate })));
    out.push(el('div.section', { text: 'AI Difficulty' }),
      el('div.choicerow', { title: 'Loads all the values of a game difficulty into this preset (like in the game).\nCUSTOM means the values differ from every game difficulty.' }, el('b', { text: 'START FROM' }), diffSeg),
      ...sections(params.filter(p => !p.sim && !p.advanced)),
      group('ADVANCED', advHint, sections(params.filter(p => p.advanced))),
      group('PERFORMANCE', perfHint, [
        el('div.choicerow', { title: 'Simulation level: how far bots are simulated in full detail. Higher costs more performance.\nEditing the values below switches to CUSTOM.' }, el('b', { text: 'AI SIMULATION' }), simSeg),
        ...sections(params.filter(p => p.sim))]));
    return out;
  }

  function renderEditor(keepScroll) {
    const top = content.scrollTop;
    content.replaceChildren(...editView());
    refreshChoices();
    refreshRefs();
    applyFilter();
    updateInfo();
    content.scrollTop = keepScroll ? top : 0;
  }

  // ---------------- compare
  function compareView() {
    const pick = (v, on) => {
      const s = el('select.input', null, ...presets.map((p, i) => el('option', { value: i, text: p.name })));
      s.value = String(presets.indexOf(v));
      s.addEventListener('change', () => { on(presets[+s.value]); render(); });
      return s;
    };
    const a = cmpA, b = cmpB;
    const rowsOut = [];
    let diffs = 0, total = 0, section = null;
    const items = [{ section: 'Simulation', name: 'AI SIMULATION', a: simLevel(a) || 'Custom', b: simLevel(b) || 'Custom' },
      ...params.filter(p => !p.advanced || cmpAdvanced).map(p => ({ p, section: p.sim ? 'AI Optimization Ranges' : p.section, name: p.name.toUpperCase(), a: get(a, p), b: get(b, p) }))];
    for (const r of items) {
      const differs = !same(r.a, r.b);
      total++;
      if (differs) diffs++;
      if (onlyDiff && !differs) continue;
      if (r.section !== section) { section = r.section; rowsOut.push(el('tr.sec', null, el('td', { colspan: 4, text: section }))); }
      const fa = r.p ? fmtV(r.p, r.a) : r.a, fb = r.p ? fmtV(r.p, r.b) : r.b;
      let chg = '=', cls = 'eq';
      if (differs) {
        if (typeof r.a === 'number' && typeof r.b === 'number') {
          const dd = r.b - r.a;
          chg = (dd > 0 ? '▲ +' : '▼ −') + fmtV(r.p, Math.abs(dd)) + (Math.abs(r.a) > 1e-12 ? '  (' + (dd > 0 ? '+' : '') + Math.round(dd / Math.abs(r.a) * 100) + '%)' : '');
          cls = dd > 0 ? 'up' : 'down';
        } else { chg = '● ' + fa + ' → ' + fb; cls = 'chg'; }
      }
      rowsOut.push(el('tr' + (differs ? '.diff' : ''), null,
        el('td.n', { text: r.name, title: r.p ? tooltip(r.p) : null }), el('td.a', { text: fa }), el('td.b', { text: fb }), el('td.' + cls, { text: chg })));
    }
    const advHidden = cmpAdvanced ? 0 : params.filter(p => p.advanced && !same(get(a, p), get(b, p))).length;
    const box = (label, checked, on) => { const c = el('input', { type: 'checkbox' }); c.checked = checked; c.addEventListener('change', () => { on(c.checked); render(); }); return el('label', null, c, ' ' + label); };
    return [
      el('div.wtop', null, el('h1', { text: 'COMPARE' }), el('button.btn', { text: '◀  BACK TO PRESET', onclick: () => { mode = 'edit'; render(); } })),
      el('div.cmpbar', null,
        el('b.ca', { text: 'A' }), pick(a, v => { cmpA = v; }),
        el('button.btn', { text: '⇄', title: 'Swap A and B', onclick: () => { [cmpA, cmpB] = [cmpB, cmpA]; render(); } }),
        el('b.cb', { text: 'B' }), pick(b, v => { cmpB = v; }),
        box('Only differences', onlyDiff, v => { onlyDiff = v; }),
        box(advHidden ? 'Advanced (+' + advHidden + ')' : 'Advanced', cmpAdvanced, v => { cmpAdvanced = v; }),
        el('span.muted', { text: diffs === 0 ? 'Identical' : diffs + ' of ' + total + ' differ' })),
      el('table.cmp', null, el('thead', null, el('tr', null, el('th', { text: 'SETTING' }), el('th.a', { text: 'A' }), el('th.b', { text: 'B' }), el('th', { text: 'CHANGE (A → B)' }))),
        el('tbody', null, ...rowsOut)),
    ];
  }

  function openCompare() {
    cmpA = refPreset() || (cur === regular() ? presets.find(p => p !== cur) : regular());
    cmpB = cur;
    mode = 'compare';
    render();
  }

  function render() {
    kept.cur = cur;
    renderList();
    if (mode === 'compare') { content.replaceChildren(...compareView()); content.scrollTop = 0; }
    else renderEditor(false);
  }

  // ---------------- actions
  function validate(pr) {
    if (!pr.name.trim()) { alert('Give the preset a name first.'); return false; }
    const clash = mine().find(x => x !== pr && makeId(x.name) === makeId(pr.name));
    if (clash) { alert('Another preset ("' + clash.name + '") has the same ID "' + makeId(pr.name) + '".\nThe game would replace one with the other: choose a different name.'); return false; }
    return true;
  }
  function addAndSelect(pr) {
    presets.push(pr);
    cur = pr;
    mode = 'edit';
    render();
    const n = content.querySelector('.wtop .name');
    if (n) { n.focus(); n.select(); }
  }
  function newPreset() {
    const pr = cloneAs(regular(), uniqueName('New preset'));
    pr.description = '';
    addAndSelect(pr);
    status('New preset created from the game\'s Regular difficulty. Click SAVE to keep it.');
  }
  function duplicate() {
    const pr = cloneAs(cur, uniqueName(cur.builtIn ? 'My ' + cur.name : cur.name + ' copy'));
    if (cur.builtIn) pr.description = 'Based on the game difficulty ' + cur.name + '.';
    addAndSelect(pr);
    status('Duplicated. Change the values, then click SAVE.');
  }
  function saveCurrent() {
    if (cur.builtIn || !validate(cur)) return;
    cur.savedAt = now();
    markSaved(cur);
    if (persist()) status('Saved "' + cur.name + '" in this browser.');
    updateInfo();
    renderList();
  }
  function revert() {
    if (!cur.dirty) return;
    if (!cur.snapshot) { alert('This preset has never been saved: there is nothing to revert to.'); return; }
    const p = fromShare(JSON.parse(cur.snapshot));
    Object.assign(cur, p, { snapshot: cur.snapshot, dirty: false });
    renderEditor(true);
    renderList();
    status('Unsaved changes discarded.');
  }
  function remove() {
    if (!confirm('Delete the preset "' + cur.name + '" from this browser?\n\nThis does not affect the game.')) return;
    const i = presets.indexOf(cur);
    presets.splice(i, 1);
    persist();
    cur = mine().slice(-1)[0] || presets[Math.min(i, presets.length - 1)];
    render();
    status('Preset deleted.');
  }
  async function editInfo() {
    const author = await dialog('Author', { label: 'Author of this preset (shown in the game)', value: cur.author || '' });
    if (author == null) return;
    const desc = await dialog('Description', { label: 'Short description of this preset', value: cur.description || '' });
    if (desc == null) return;
    cur.author = author;
    cur.description = desc;
    cur.dirty = true;
    updateInfo();
    renderList();
  }
  function copyCode() {
    if (!validate(cur)) return;
    const code = encodeCode(toShare(cur));
    copy(code, 'the share code of "' + cur.name + '" (' + code.length + ' characters)');
  }
  function viewCode() {
    if (!validate(cur)) return;
    dialog('Share code – ' + cur.name, { value: encodeCode(toShare(cur)), multiline: true, readonly: true, ok: 'CLOSE', what: 'the share code' });
  }
  async function importCode() {
    let initial = '';
    try { const t = (await navigator.clipboard.readText()).trim(); if (t.startsWith('IRR')) initial = t; } catch (e) { /* no clipboard access */ }
    let pr = null;
    const code = await dialog('Import a share code', {
      label: 'Paste the code (IRR1-...) copied from the game or sent by a friend:', value: initial, multiline: true, ok: 'IMPORT',
      validate: t => { try { pr = fromShare(decodeCode(t)); return null; } catch (e) { return e.message; } },
    });
    if (code == null || !pr) return;
    const original = pr.name;
    pr.name = uniqueName(pr.name.trim() || 'Imported preset');
    markSaved(pr);
    presets.push(pr);
    persist();
    cur = pr;
    mode = 'edit';
    render();
    status('Imported "' + pr.name + '"' + (pr.name !== original ? ' (renamed: a preset named "' + original + '" already exists)' : '') + '.');
  }
  function exportPresets() {
    const saved = mine().filter(p => p.snapshot);
    if (!saved.length) { status('No saved preset to export yet.'); return; }
    downloadJson({ format: 'IRRCompanion', version: 1, presets: saved.map(p => JSON.parse(p.snapshot)) }, 'presets.json');
    status('Presets exported (presets.json): the desktop app reads the same file in %APPDATA%\\IRR Companion.');
  }
  async function importPresets() {
    try {
      const doc = await pickJson();
      if (!doc) return;
      const arr = Array.isArray(doc.presets) ? doc.presets : null;
      if (!arr) throw new Error('This file has no presets.');
      let n = 0, last = null;
      for (const o of arr) {
        let pr;
        try { pr = fromShare(o); } catch (e) { continue; }
        const json = JSON.stringify(toShare(pr));
        if (mine().some(p => p.snapshot === json)) continue;
        pr.name = uniqueName(pr.name.trim() || 'Imported preset');
        markSaved(pr);
        presets.push(pr);
        last = pr;
        n++;
      }
      persist();
      if (last) { cur = last; mode = 'edit'; }
      render();
      status(n ? 'Imported ' + n + ' preset' + (n > 1 ? 's' : '') + '.' : 'Nothing new to import.');
    } catch (e) { status('Import failed: ' + e.message); }
  }

  render();
  return () => window.removeEventListener('beforeunload', confirmLeave);
}
