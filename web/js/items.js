// ITEMS tab: every item of the game by category, with its buy and sell prices. Data: items.json (tools/build_items.py).
import { data, el, itemIcon, money, debounce } from './app.js?v=dev';

const COLUMNS = [
  { title: 'MARKET', tip: 'Buy at the market (cash). Small text: the faction loyalty level the market asks for.', key: i => i.buy ? i.price : 0 },
  { title: 'BLACK MKT', tip: 'Buy at the black market, in Marked Coins (MC).', key: i => i.coins || 0 },
  { title: 'TRADER', tip: 'Buy from a raid trader (cash): the market price. Small text: which traders can have it in stock (weapons are not listed).', key: i => i.traders ? i.price || 0 : 0 },
  { title: 'SELL', tip: 'What the market pays you (cash): the market price x the share of the item\'s category (weapons 35 %, parts 45 %, gear and ammo 40 %, barter 80 %, keys 20 %, others 60 %). Grey with "?": the game files say it cannot be sold at the market.', key: i => i.sell || 0 },
  { title: 'TRADER PAYS', tip: 'What a raid trader pays you for it (cash): only the quest crates.', key: i => i.handIn || 0 },
];

export async function mountItems(view) {
  const d = await data('items.json');
  const mult = cat => d.sellMultipliers[cat] ?? d.sellDefault;
  let cat = d.categories[0], query = '', sort = -1;

  const list = el('div.list');
  const search = el('input.input', { placeholder: 'Find an item...', 'aria-label': 'Find an item' });
  const side = el('div.side', null, el('div.section', { text: 'Categories' }), search, el('div', { style: 'height:6px' }), list,
    el('div.hint', { text: 'Prices read from the game\'s vendor table and vendor settings. Weapons are priced new, with their default parts. Ammo: price of one round.' }));
  const content = el('div.content.items-content');
  view.replaceChildren(side, content);
  search.addEventListener('input', debounce(() => { query = search.value.trim().toLowerCase(); render(); }));
  search.addEventListener('keydown', e => { if (e.key === 'Escape') { search.value = ''; query = ''; render(); } });

  function renderList() {
    const entries = [{ id: null, name: 'All items', count: d.items.length }, ...d.categories];
    list.replaceChildren(...entries.map(c => el('div.item.cat' + ((cat?.id ?? null) === c.id && !query ? '.on' : ''),
      { onclick: () => { cat = c.id ? c : null; search.value = ''; query = ''; render(); toTable(); } },
      el('span', { text: c.name.toUpperCase(), style: c.id ? '' : 'color:var(--accent)' }), el('span.n', { text: String(c.count) }))));
  }

  // phones: the table is under the category list
  const toTable = () => { if (window.matchMedia('(max-width: 820px)').matches) content.scrollIntoView({ block: 'start' }); };

  function describe(i) {
    const out = [i.name + (i.tier ? '  (tier ' + i.tier + ')' : '')];
    if (!i.price) out.push('No price in the game files.');
    if (i.buy) out.push('Market: ' + money(i.price) + ' cash' + (i.loyalty ? ', needs ' + i.loyalty.faction + ' loyalty ' + i.loyalty.level : ''));
    else if (i.price) out.push('Not sold at the market (market price ' + money(i.price) + ').');
    if (i.coins) out.push('Black market: ' + i.coins + ' Marked Coins');
    for (const t of i.traders || []) out.push('Trader ' + t.who + ' (' + t.trader + '): ' + money(i.price) + ' cash, in stock ' + Math.round(t.chance * 100) + ' % of the time');
    if (i.sell) out.push('Sell at the market: ' + money(i.sell) + ' cash (' + Math.round(mult(i.cat) * 100) + ' % of the price)' +
      (i.noSell ? ' - the game files say it cannot be sold there: to check in game' : ''));
    if (i.handIn) out.push('A raid trader pays ' + money(i.handIn) + ' cash for it.');
    out.push(i.id);
    return out.join('\n');
  }

  const cell = (value, small, cls) => value == null
    ? el('div.pc.none', { text: '—' })
    : el('div.pc' + (cls ? '.' + cls : ''), null, el('b', { text: value }), small ? el('small', { text: small }) : null);

  function row(i, flat) {
    return el('div.irow', { title: describe(i) },
      el('div.icon', null, i.icon ? itemIcon(i.id) : null),
      el('div.iname', null, el('span', { text: i.name }),
        el('small', { text: [i.tier ? 'tier ' + i.tier : '', flat && i.sub ? i.sub : '', flat && !i.sub ? (d.categories.find(c => c.id === i.cat) || {}).name : ''].filter(Boolean).join('  ·  ') })),
      cell(i.buy ? money(i.price) : null, i.buy && i.loyalty ? i.loyalty.faction + ' loyalty ' + i.loyalty.level : null, 'cash'),
      cell(i.coins ? i.coins + ' MC' : null, null, 'coins'),
      cell(i.traders && i.price ? money(i.price) : null, i.traders ? (i.traders.length === 1 ? i.traders[0].who : i.traders.length + ' traders') : null, 'cash'),
      cell(i.sell ? (i.sell < 10 ? String(i.sell) : money(i.sell)) + (i.noSell ? ' ?' : '') : null, null, i.noSell ? 'unsure' : 'gain'),
      cell(i.handIn ? money(i.handIn) : null, null, 'gain'));
  }

  function render() {
    renderList();
    let items = d.items;
    if (query) items = items.filter(i => i.name.toLowerCase().includes(query) || i.id.toLowerCase().includes(query));
    else if (cat) items = items.filter(i => i.cat === cat.id);
    const order = l => sort < 0 ? [...l].sort((a, b) => a.name.localeCompare(b.name))
      : [...l].sort((a, b) => COLUMNS[sort].key(b) - COLUMNS[sort].key(a) || a.name.localeCompare(b.name));
    const grouped = !!cat && !query;
    const rows = [];
    if (grouped) {
      const groups = new Map();
      for (const i of items) { const k = i.sub || ''; if (!groups.has(k)) groups.set(k, []); groups.get(k).push(i); }
      for (const [k, g] of groups) {
        if (k) rows.push(el('div.caption', { text: k + '  ·  ' + g.length }));
        rows.push(...order(g).map(i => row(i, false)));
      }
    } else rows.push(...order(items).map(i => row(i, true)));
    const head = el('div.irow.ihead', null, el('div'),
      el('div.iname', { onclick: () => { sort = -1; render(); }, title: 'Sort by name' }, el('span', { text: 'ITEM' + (sort < 0 ? ' ▲' : '') })),
      ...COLUMNS.map((c, k) => el('div.pc', { onclick: () => { sort = k; render(); }, title: c.tip + ' Click to sort (highest first).' },
        el('b', { text: c.title + (sort === k ? ' ▼' : '') }))));
    content.replaceChildren(
      el('h1', { text: query ? '"' + search.value.trim() + '"' : (cat ? cat.name : 'All items').toUpperCase() }),
      el('p.lead', { text: items.length + (items.length === 1 ? ' item' : ' items') +
        (cat && !query ? '  ·  sold at the market: ' + Math.round(mult(cat.id) * 100) + ' % of the price' : '') + '  ·  MC = Marked Coins  ·  hover (or long-press) a row for the details' }),
      el('div.itable', null, head, rows));
    content.scrollTop = 0;
  }
  render();
}
