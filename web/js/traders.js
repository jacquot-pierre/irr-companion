// TRADERS tab: stock of the traders met in raids, with the chance and the price of every item.
import { data, el, itemIcon, pct, money, chanceClass, debounce } from './app.js?v=dev';

export async function mountTraders(view) {
  const d = await data('traders.json');
  const items = d.items;
  let trader = d.traders[0], query = '';

  const list = el('div.list');
  const search = el('input.input', { placeholder: 'Find an item...', 'aria-label': 'Find an item' });
  const content = el('div.content');
  const side = el('div.side', null, el('div.section', { text: 'Traders' }), search, el('div', { style: 'height:6px' }), list,
    el('div.hint', { text: 'Read from the game\'s trader presets. Weapons are not listed. The chances of the random loot are an estimate.' }));
  view.replaceChildren(side, content);

  const apply = () => { query = search.value.trim().toLowerCase(); render(); };
  search.addEventListener('input', debounce(apply));
  search.addEventListener('keydown', e => {
    if (e.key === 'Enter') apply();
    else if (e.key === 'Escape') { search.value = ''; apply(); }
  });

  const people = t => (t.people || []).map(p => p.name).join(' & ');
  const mapText = p => p.map ? p.map + (p.guessed ? ' (probably)' : '') : 'map unknown';
  const match = it => !query || (items[it.id]?.name || it.id).toLowerCase().includes(query) || it.id.toLowerCase().includes(query);

  function renderList() {
    list.replaceChildren(...d.traders.map(t => el('div.item' + (t === trader ? '.on' : ''), { onclick: () => { trader = t; render(); } },
      el('div.t', { text: people(t) || t.id + ' trader' }),
      el('div.s', { text: t.id + ' · ' + t.name }),
      el('div.m', { text: (t.people || []).map(mapText).join(', ') }))));
  }

  function row(it, detail, chance) {
    const info = items[it.id] || {};
    const price = info.price || 0;
    return el('div.row', { title: it.id },
      el('div.icon', null, itemIcon(it.id)),
      el('div.name', null, info.name || it.id, detail ? el('small', { text: detail }) : null),
      el('div.price', { text: money(price), title: price ? money(price) + ' cash per unit' + (it.count > 1 ? ' (× ' + it.count + ' = ' + money(price * it.count) + ')' : '') : 'No market price in the game files.' }),
      el('div.chance.' + chanceClass(chance), { text: pct(chance) }));
  }

  function card(title, state, tip, rows) {
    return el('div.card', null,
      el('div.head', { title: tip }, el('span.t', { text: title }), el('span.s', { text: state })),
      el('div.cols', null, el('div'), el('span', { text: 'PRICE', title: 'Market price of one unit, in cash (vendor table of the game). A trader in a raid could apply its own multiplier.' }),
        el('span', { text: 'CHANCE', title: 'Chance that the item is in the trader\'s stock.' })),
      rows);
  }

  function render() {
    renderList();
    const t = trader, ps = t.people || [];
    const who = !ps.length ? t.name + ' trader. '
      : ps.length === 1 ? ps[0].name + ', ' + t.name + ' trader, on ' + mapText(ps[0]) + '. '
        : ps.map(p => p.name + ' (' + mapText(p) + ')').join(' and ') + ': the ' + t.name + ' traders, same stock. ';
    const out = [
      el('h1', { text: (people(t) || t.id + ' trader').toUpperCase() + (ps.length ? '  ·  ' + t.id : '') }),
      el('p.lead', { text: who + 'Each time the stock is made: random loot, a random pick in a list, and the items always in stock.' +
        (t.weapons > 0 ? ' Plus a ' + pct(t.weapons) + ' chance of a weapon (not listed).' : '') }),
    ];
    for (const g of t.random) {
      const n = g.min === g.max ? String(g.min) : g.min + ' to ' + g.max;
      const rows = [];
      for (const c of g.categories) {
        const its = c.items.filter(match);
        if (!its.length) continue;
        const single = c.item && c.items.length === 1;
        if (!single) rows.push(el('div.caption', { text: c.label + '  ·  ' + pct(c.share) + ' of the draws  ·  ' + c.items.length + ' items' }));
        for (const it of its) rows.push(row(it, single ? pct(c.share) + ' of the draws' : it.tier ? 'tier ' + it.tier : '', it.chance));
      }
      if (rows.length) out.push(card('RANDOM LOOT', n + ' items · estimated',
        'The game draws ' + n + ' items: each draw picks a kind of item by its weight, then (for a family of items) a rarity tier by its weight (' +
        g.tiers.map(([k, w]) => 'tier ' + k + ': ' + w).join(', ') + '), then an item of that tier.', rows));
    }
    for (const g of t.choices) {
      const its = g.items.filter(match);
      if (!its.length) continue;
      const n = g.min === g.max ? String(g.min) : g.min + ' to ' + g.max;
      const total = g.items.reduce((a, i) => a + i.weight, 0);
      out.push(card('RANDOM PICK', n + ' of these ' + g.items.length + ' items',
        'The game draws ' + n + ' different items from this list; an item with a higher weight comes out more often. Exact chances.',
        its.map(it => row(it, 'weight ' + it.weight + ' / ' + total, it.chance))));
    }
    const fixed = t.fixed.filter(match).sort((a, b) => (items[a.id]?.name || a.id).localeCompare(items[b.id]?.name || b.id));
    if (fixed.length) out.push(card('ALWAYS IN STOCK', t.fixed.length + ' items · ' + t.fixed.reduce((a, i) => a + i.count, 0) + ' units',
      'These items are always added to the stock, in the quantity shown.', fixed.map(it => row(it, it.count > 1 ? '× ' + it.count : '', 1))));
    if (out.length === 2) out.push(el('p.lead', { text: 'No item of this trader matches "' + search.value.trim() + '".' }));
    content.replaceChildren(...out);
    content.scrollTop = 0;
  }
  render();
}
