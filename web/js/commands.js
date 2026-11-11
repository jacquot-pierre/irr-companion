// COMMANDS tab: console command builder (every irr.* command of the game, with its arguments) and favorites.
// Favorites are kept in this browser and can be exported / imported as the desktop app's favorites.json.
import { data, el, seg, copy, status, stored, downloadJson, pickJson, dialog, token, splitLine, debounce } from './app.js?v=dev';

const CAT_FAV = '★  FAVORITES', CAT_ESS = 'ESSENTIALS', CAT_ALL = 'ALL COMMANDS', ALL_ITEMS = 'All items';
const fmt = d => String(+(+d).toFixed(3));
const favStore = stored('irr.favorites', []);

export async function mountCommands(view) {
  const d = await data('commands.json');
  const base = d.weatherIndexBase || 0;
  const find = cmd => d.commands.find(c => c.cmd.toLowerCase() === (cmd || '').toLowerCase());
  const itemCats = [...new Set(d.items.map(i => i[1]))];

  let favs = (favStore.load() || []).filter(f => f && f.line && f.line.trim());
  let cat = CAT_ESS, query = '', selected = null, fav = null, cmd = null, editors = [];
  const saveFavs = () => favStore.save(favs);

  // ---------------- layout
  const search = el('input.input', { placeholder: 'Search a command...', 'aria-label': 'Search a command' });
  const cats = el('div.list');
  const side = el('div.side', null,
    el('div.section', { text: 'Commands' }), search, el('div', { style: 'height:6px' }), cats,
    el('div', { style: 'height:10px' }),
    el('div.buttons', null,
      el('button.btn', { text: 'EXPORT', title: 'Download your favorites (favorites.json, the desktop app\'s format)', onclick: exportFavs }),
      el('button.btn', { text: 'IMPORT', title: 'Add the favorites of a favorites.json (desktop app or export)', onclick: importFavs })),
    el('div.hint', { text: 'Open the console in game with ² / ~, ^ or F10, then paste (Ctrl+V). Favorites are saved in this browser.' }));
  const list = el('div.cmdlist');
  const builder = el('div.content.builder');
  view.replaceChildren(side, list, builder);

  const runSearch = () => { query = search.value.trim().toLowerCase(); renderList(); };
  search.addEventListener('input', debounce(runSearch, 250));
  search.addEventListener('keydown', e => {
    if (e.key === 'Enter') runSearch();
    else if (e.key === 'Escape') { search.value = ''; runSearch(); }
  });

  // ---------------- category list
  function count(c) {
    if (c === CAT_FAV) return favs.length;
    if (c === CAT_ESS) return d.essentials.length;
    if (c === CAT_ALL) return d.commands.length;
    return d.commands.filter(x => x.cat === c).length;
  }
  function renderCats() {
    const names = [CAT_FAV, CAT_ESS, ...d.categories, CAT_ALL];
    cats.replaceChildren(...names.map((c, i) => el('div.item.cat' + (c === cat && !query ? '.on' : '') + (i === 1 || i === names.length - 2 ? '.sep' : ''),
      { onclick: () => { cat = c; search.value = ''; query = ''; renderList(); } },
      el('span.t' + (c === CAT_FAV ? '.fav' : c === CAT_ESS ? '.ess' : ''), { text: c === CAT_FAV || c === CAT_ESS || c === CAT_ALL ? c : c.toUpperCase() }),
      el('span.n', { text: String(count(c)) }))));
  }

  // ---------------- command list
  const favCmd = f => splitLine(f.line)[0] || '';
  const isFavCmd = c => favs.some(f => favCmd(f).toLowerCase() === c.cmd.toLowerCase());
  function entries() {
    if (query) {
      const words = query.split(/\s+/).filter(Boolean);
      return [
        ...favs.filter(f => words.every(w => (f.label + ' ' + f.line).toLowerCase().includes(w))),
        ...d.commands.filter(c => words.every(w => (c.cmd + ' ' + c.title + ' ' + c.desc + ' ' + c.cat).toLowerCase().includes(w))),
      ];
    }
    if (cat === CAT_FAV) return favs.slice();
    if (cat === CAT_ESS) return d.essentials.map(find).filter(Boolean);
    if (cat === CAT_ALL) return d.commands.slice();
    return d.commands.filter(c => c.cat === cat);
  }
  function renderList(keep) {
    renderCats();
    const items = entries();
    list.replaceChildren(...items.map(entry => {
      const f = entry.line !== undefined ? entry : null;
      if (f) return el('div.centry.favorite' + (entry === selected ? '.on' : ''), { onclick: () => select(entry) },
        el('div.t', { text: '★  ' + f.label }), el('div.c', { text: f.line }));
      const c = entry;
      const args = c.args.map(a => a.optional ? '[' + a.name + ']' : '<' + a.name + '>').join(' ');
      return el('div.centry' + (entry === selected ? '.on' : ''), { onclick: () => select(entry), title: c.desc },
        el('div.t', null, el('span', { text: c.title }),
          el('span.tags' + (c.warn ? '.warn' : ''), { text: (c.host ? 'HOST  ' : '') + (c.warn ? '⚠  ' : '') + (isFavCmd(c) ? '★' : '') })),
        el('div.c', { text: c.cmd + (args ? '  ' + args : '') }));
    }));
    if (!items.length) list.append(el('p.lead', {
      style: 'padding:12px',
      text: query ? 'No command matches "' + search.value.trim() + '".' : 'No favorites yet. Select a command, set its values, then click ★ ADD TO FAVORITES.',
    }));
    if (keep && items.includes(keep)) { if (keep === selected) highlight(keep); else select(keep, true); }
    else if (items.length && !(selected && items.includes(selected))) select(items[0]);
    else if (!items.length) select(null);
  }

  // ---------------- argument editors
  function makeEditor(a, onChange) {
    const ed = { a, value: '', included: true, map: null };
    const title = a.name.toUpperCase() + (a.unit ? '  (' + a.unit + ')' : '');
    const head = el('div.arg-head');
    let check = null;
    if (a.optional) {
      check = el('input', { type: 'checkbox' });
      check.addEventListener('change', () => { ed.included = check.checked; paint(); onChange(); });
      head.append(el('label.arg-name', { title: 'Optional argument: untick to leave it out of the command.' }, check, ' ' + title));
    } else head.append(el('span.arg-name', { text: title }));
    head.append(el('span.arg-help', { text: a.help || '' }));
    const body = el('div.arg-body');
    const root = el('div.arg', null, head, body);
    ed.root = root;
    const raise = () => {
      if (a.optional && !ed.included) { ed.included = true; check.checked = true; paint(); }
      onChange();
    };
    const paint = () => root.classList.toggle('off', a.optional && !ed.included);

    let setUi;
    const options = a.options || [];
    if (a.kind === 'toggle' || (a.kind === 'choice' && !a.editable && options.length <= 4)) {
      const values = a.kind === 'toggle' ? ['0', '1'] : options.map(o => o[0]);
      const labels = a.kind === 'toggle' ? [a.off || 'OFF', a.on || 'ON'] : options.map(o => o[1]);
      setUi = v => {
        let i = values.indexOf(v);
        if (i < 0) i = Math.max(0, values.indexOf(a.default == null ? '' : String(a.default)));
        ed.value = values[i];
        body.replaceChildren(seg(labels, i, j => { ed.value = values[j]; setUi(ed.value); raise(); }));
      };
    } else if (a.kind === 'choice' && !a.editable) {
      const s = el('select.input', null, ...options.map(o => el('option', { value: o[0], text: o[1] })));
      s.addEventListener('change', () => { ed.value = s.value; raise(); });
      body.append(s);
      setUi = v => {
        if (!options.some(o => o[0] === v)) v = options.some(o => o[0] === a.default) ? a.default : options[0][0];
        s.value = ed.value = v;
      };
    } else if (a.kind === 'choice' || a.kind === 'item') {
      const listId = 'dl-' + Math.random().toString(36).slice(2);
      const dl = el('datalist', { id: listId });
      const box = el('input.input', { list: listId, spellcheck: 'false', title: a.kind === 'item' ? 'Internal item name. Type to search; part of a name also works in the game.' : 'Pick a value or type your own.' });
      box.addEventListener('input', () => { ed.value = box.value; raise(); });
      const fill = c => dl.replaceChildren(...(a.kind === 'item'
        ? d.items.filter(i => !c || c === ALL_ITEMS || i[1] === c).map(i => el('option', { value: i[0] }))
        : options.map(o => el('option', { value: o[0], text: o[1] }))));
      if (a.kind === 'item') {
        const cs = el('select.input', { title: 'Filter the item list by category.' }, el('option', { text: ALL_ITEMS }), ...itemCats.map(c => el('option', { text: c })));
        cs.addEventListener('change', () => fill(cs.value));
        body.append(cs);
      }
      fill(ALL_ITEMS);
      body.append(box, dl);
      setUi = v => { box.value = ed.value = v; };
    } else if (a.kind === 'int' || a.kind === 'float') {
      const bounded = a.min != null && a.max != null;
      const step = a.step || (a.kind === 'int' ? 1 : 0.1);
      const box = el('input.input.num', { inputmode: 'decimal', title: 'Type any value' + (bounded ? ' (the slider covers ' + fmt(a.min) + ' to ' + fmt(a.max) + ').' : '.') });
      let range = null;
      if (bounded) {
        range = el('input.range', { type: 'range', min: a.min, max: a.max, step });
        range.addEventListener('input', () => { ed.value = fmt(+range.value); box.value = ed.value; box.classList.remove('bad'); raise(); });
        body.append(range);
      }
      box.addEventListener('input', () => {
        let v = parseFloat(box.value.trim().replace(',', '.'));
        if (!isFinite(v)) { box.classList.add('bad'); return; }
        if (a.kind === 'int') v = Math.round(v);
        box.classList.remove('bad');
        ed.value = fmt(v);
        if (range) range.value = v;
        raise();
      });
      body.append(box);
      body.classList.add('numeric');
      setUi = v => { box.value = ed.value = v; box.classList.remove('bad'); if (range && isFinite(parseFloat(v))) range.value = parseFloat(v); };
    } else if (a.kind === 'weather') {
      const ms = el('select.input', { title: 'Each map has its own list of weathers.' }, ...d.maps.map(m => el('option', { text: m.name })));
      const ws = el('select.input', { title: 'The percentage is the chance of this weather when a raid starts.' });
      const fillW = idx => {
        const m = d.maps.find(x => x.name === ms.value) || d.maps[0];
        const total = m.weathers.reduce((s, w) => s + w.weight, 0);
        ws.replaceChildren(...m.weathers.map((w, i) => el('option', { value: i, text: w.name + '   (' + Math.round(total ? w.weight / total * 100 : 0) + '%)' })));
        const sel = Math.max(0, Math.min(idx, m.weathers.length - 1));
        ws.value = sel;
        ed.value = String(sel + base);
        ed.map = m.name;
      };
      ms.addEventListener('change', () => { fillW(+ws.value || 0); raise(); });
      ws.addEventListener('change', () => { ed.value = String(+ws.value + base); raise(); });
      body.append(ms, ws);
      ed.weatherName = () => {
        const m = d.maps.find(x => x.name === ed.map);
        const w = m && m.weathers[+ed.value - base];
        return w ? w.name.toLowerCase() : '';
      };
      setUi = (v, tok, map) => {
        let mapName = map, idx = 0;
        const spec = tok != null && tok.includes(':') ? tok : a.default;
        if (spec && spec.includes(':')) { const p = spec.split(':'); if (!mapName) mapName = p[0]; idx = parseInt(p[1], 10) || 0; }
        if (tok != null && !tok.includes(':') && /^-?\d+$/.test(tok)) idx = parseInt(tok, 10) - base;
        ms.value = d.maps.some(m => m.name === mapName) ? mapName : d.maps[0].name;
        fillW(idx);
      };
    } else {
      const box = el('input.input', { spellcheck: 'false' });
      box.addEventListener('input', () => { ed.value = box.value; raise(); });
      body.append(box);
      setUi = v => { box.value = ed.value = v; };
    }

    /** Loads a value (from a favorite); included=false unticks an optional argument. */
    ed.set = (tok, included, map) => {
      if (check) { ed.included = included == null ? tok != null : included; check.checked = ed.included; }
      let v = tok;
      if (v == null) v = a.default == null ? '' : typeof a.default === 'number' ? fmt(a.default) : String(a.default);
      setUi(v, tok, map);
      paint();
    };
    ed.reset = () => ed.set(null, a.optional && (a.default == null || a.default === '') ? false : null, null);
    ed.token = () => (a.optional && !ed.included) || !String(ed.value).trim() ? null : token(ed.value);
    ed.reset();
    return ed;
  }

  // ---------------- builder
  let preview, favNote, saveBtn, upBtn, downBtn;
  const currentLine = () => {
    if (!cmd) return fav ? fav.line : '';
    const parts = [cmd.cmd];
    for (const ed of editors) { const t = ed.token(); if (t == null) break; parts.push(t); } // positional: stop at the first one left out
    return parts.join(' ');
  };
  const weatherMap = () => { const w = editors.find(e => e.a.kind === 'weather'); return w ? w.map : null; };
  function defaultLabel() {
    if (!cmd) return 'Command';
    const parts = [];
    for (const ed of editors) {
      const t = ed.token();
      if (t == null) break;
      if (ed.a.kind === 'weather') parts.push(ed.map + ' ' + ed.weatherName());
      else if (ed.a.kind === 'toggle') parts.push((t === '1' ? ed.a.on || 'ON' : ed.a.off || 'OFF').toLowerCase());
      else parts.push(t.replace(/"/g, '') + (ed.a.unit ? ' ' + ed.a.unit : ''));
    }
    return cmd.title + (parts.length ? ' – ' + parts.join(', ') : '');
  }
  function updateLine() {
    if (!preview) return;
    preview.value = currentLine();
    if (fav) {
      const changed = preview.value !== fav.line || (weatherMap() || '') !== (fav.map || '');
      favNote.textContent = changed ? 'Values changed: click SAVE to keep them in this favorite.' : 'Favorite: ' + fav.label;
      saveBtn.textContent = changed ? 'SAVE *' : 'SAVE';
    }
  }

  function select(entry, keepScroll) {
    selected = entry;
    fav = entry && entry.line !== undefined ? entry : null;
    cmd = fav ? find(favCmd(fav)) : entry;
    highlight(entry, keepScroll);
    renderBuilder();
  }

  function highlight(entry, keepScroll) {
    const idx = entries().indexOf(entry);
    [...list.children].forEach((n, i) => n.classList.toggle('on', i === idx));
    if (idx >= 0 && list.children[idx] && !keepScroll) list.children[idx].scrollIntoView({ block: 'nearest' });
    if (upBtn) { const i = favs.indexOf(fav); upBtn.disabled = i <= 0; downBtn.disabled = i < 0 || i >= favs.length - 1; }
  }

  function renderBuilder() {
    preview = favNote = saveBtn = upBtn = downBtn = null;
    editors = [];
    if (!cmd && !fav) { builder.replaceChildren(el('p.lead', { text: 'Select a command on the left.' })); return; }
    const out = [];
    out.push(el('h1', { text: cmd ? cmd.title : fav.label }));
    out.push(el('div.cmdname', { text: cmd ? cmd.cmd : '(unknown command)' }));
    if (cmd) {
      out.push(el('p.lead', { text: cmd.desc }));
      const notes = [];
      if (cmd.host) notes.push('Host only: works for the player who hosts the game (or in solo).');
      if (cmd.warn) notes.push('⚠ ' + cmd.warn);
      if (cmd.cvar) notes.push('Setting: without a value, the game prints the current value.');
      if (cmd.args.some(a => a.kind === 'weather') && base === 1) notes.push('Weather numbers are assumed to start at 1 (not yet confirmed in game).');
      if (notes.length) out.push(el('div.notes' + (cmd.warn ? '.warn' : ''), null, ...notes.map(n => el('div', { text: n }))));
      if (cmd.args.length) {
        out.push(el('div.section', { text: 'Arguments' }));
        editors = cmd.args.map(a => makeEditor(a, updateLine));
        out.push(...editors.map(e => e.root));
      } else out.push(el('p.muted', { text: 'No argument: copy and paste it as it is.' }));
      if (fav) {
        const toks = splitLine(fav.line);
        editors.forEach((ed, i) => ed.set(i + 1 < toks.length ? toks[i + 1] : null, i + 1 < toks.length, fav.map));
      }
    }
    preview = el('input.input.preview', { readonly: true, title: 'The command line to paste in the game console.' });
    favNote = el('div.favnote');
    out.push(el('div.section', { text: 'Command line' }), preview, favNote);
    const copyBtn = el('button.btn.primary', { text: 'COPY', onclick: () => {
      const line = preview.value.trim();
      if (!line) return;
      copy(line, '"' + line + '"');
      copyBtn.textContent = 'COPIED ✓';
      setTimeout(() => { copyBtn.textContent = 'COPY'; }, 1400);
    } });
    const btns = [copyBtn];
    if (cmd) btns.push(el('button.btn', { text: 'DEFAULTS', onclick: () => { editors.forEach(e => e.reset()); updateLine(); } }));
    if (!fav) btns.push(el('button.btn', { text: '★ ADD TO FAVORITES', onclick: addFav }));
    else {
      const i = favs.indexOf(fav);
      saveBtn = el('button.btn.primary', { text: 'SAVE', title: 'Keep the current values in this favorite', onclick: () => {
        fav.line = preview.value.trim();
        const m = weatherMap();
        if (m) fav.map = m; else delete fav.map;
        if (saveFavs()) status('Favorite saved: ' + fav.label);
        updateLine();
        renderList(fav);
      } });
      btns.push(saveBtn,
        el('button.btn', { text: 'RENAME', onclick: async () => {
          const l = await dialog('Rename favorite', { label: 'Name of this favorite:', value: fav.label });
          if (!l || !l.trim()) return;
          fav.label = l.trim();
          saveFavs();
          updateLine();
          renderList(fav);
        } }),
        el('button.btn', { text: 'REMOVE', onclick: () => {
          if (!confirm('Remove "' + fav.label + '" from your favorites?')) return;
          const at = favs.indexOf(fav);
          favs.splice(at, 1);
          saveFavs();
          status('Favorite removed.');
          selected = null;
          renderList(favs[Math.min(at, favs.length - 1)]);
        } }),
        upBtn = el('button.btn', { text: '▲', title: 'Move up', disabled: i <= 0 || undefined, onclick: () => moveFav(-1) }),
        downBtn = el('button.btn', { text: '▼', title: 'Move down', disabled: i >= favs.length - 1 || undefined, onclick: () => moveFav(1) }));
    }
    out.push(el('div.buttons', { style: 'margin-top:10px' }, ...btns));
    builder.replaceChildren(...out);
    builder.scrollTop = 0;
    updateLine();
  }

  async function addFav() {
    const line = preview.value.trim();
    if (!line) return;
    const label = await dialog('Add to favorites', { label: 'Name of this favorite:', value: defaultLabel() });
    if (label == null) return;
    const f = { label: label.trim() || defaultLabel(), line };
    const m = weatherMap();
    if (m) f.map = m;
    favs.push(f);
    if (!saveFavs()) { favs.pop(); return; }
    status('Added to favorites: ' + f.label);
    renderList(selected);
  }

  function moveFav(dir) {
    const i = favs.indexOf(fav), j = i + dir;
    if (i < 0 || j < 0 || j >= favs.length) return;
    favs.splice(i, 1);
    favs.splice(j, 0, fav);
    saveFavs();
    renderList(fav);
  }

  function exportFavs() {
    if (!favs.length) { status('No favorites to export yet.'); return; }
    downloadJson({ format: 'IRRCompanion.Favorites', version: 1, favorites: favs }, 'favorites.json');
    status('Favorites exported (favorites.json): the desktop app reads the same file in %APPDATA%\\IRR Companion.');
  }

  async function importFavs() {
    try {
      const doc = await pickJson();
      if (!doc) return;
      const arr = Array.isArray(doc) ? doc : doc.favorites;
      if (!Array.isArray(arr)) throw new Error('This file has no favorites.');
      let n = 0;
      for (const o of arr) {
        if (!o || typeof o.line !== 'string' || !o.line.trim()) continue;
        if (favs.some(f => f.line === o.line && f.label === (o.label || ''))) continue;
        const f = { label: o.label || '', line: o.line };
        if (o.map) f.map = o.map;
        favs.push(f);
        n++;
      }
      saveFavs();
      cat = CAT_FAV;
      search.value = '';
      query = '';
      selected = null;
      renderList();
      status(n ? 'Imported ' + n + ' favorite' + (n > 1 ? 's' : '') + '.' : 'Nothing new to import (those favorites are already here).');
    } catch (e) { status('Import failed: ' + e.message); }
  }

  renderList();
}
