// MISSIONS tab: the FANG story as a tree (missions top to bottom, branches side by side), what to do in each
// mission, its rewards and its briefing. Data: missions.json (tools/build_missions.py).
import { data, el, itemIcon, money } from './app.js?v=dev';

const MAP_COLORS = { Delta: '#8bc34a', Quarry: '#f0a040', Outskirts: '#5da9e9', Bunker: '#ba8cdc' };

export async function mountMissions(view) {
  const d = await data('missions.json');
  const ms = d.missions;
  const all = Object.entries(ms).map(([id, m]) => Object.assign(m, { id }));

  // number the story missions, link each one to the ones before it, the side missions to their giver
  let n = 0;
  const walk = (items, before) => {
    for (const it of items) {
      if (it.branches) { before = it.branches.flatMap(br => walk(br, before)); continue; }
      const m = ms[it.id];
      m.number = ++n;
      m.waitAll = !!it.waitAll;
      m.after = before.map(x => x.id);
      before = [m];
    }
    return before;
  };
  walk(d.story, []);
  const storyCount = n;
  for (const m of all) for (const u of m.unlocks || []) if (ms[u]) ms[u].givenBy = m.id;

  const mapColor = m => m.maps.length === 1 ? MAP_COLORS[m.maps[0]] || 'var(--dim)' : 'var(--dim)';
  const mapText = m => m.maps.length >= 4 ? 'Any map' : m.maps.join(' / ');
  const shortTitle = m => m.title.replace('Reconnaissance - ', 'Recon ');
  const chipText = m => (m.blocksStory ? '! ' : '+ ') + shortTitle(m);
  // side missions the game counts as story missions (bStoryMission): they hold the waits after the mission that gives them
  const blockers = m => all.filter(x => x.blocksStory && x.givenBy && ms[x.givenBy].number < m.number);

  let selected = ms[d.story[0].id];
  const nodes = new Map();  // mission id -> element

  // ---------------------------------------------------------------- tree
  function node(m) {
    const box = el('div.tm', { tabindex: 0, title: 'Story mission ' + m.number + ': ' + m.title + ' (' + mapText(m) + ')', onclick: () => select(m),
      onkeydown: e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); select(m); } } },
      el('span.no', { text: String(m.number) }),
      el('span.tt', null, el('b', { text: m.title }), el('small', { text: mapText(m) + (m.place ? ' · ' + m.place : ''), style: 'color:' + mapColor(m) })));
    box.style.borderLeftColor = mapColor(m);
    nodes.set(m.id, box);
    const chips = (m.unlocks || []).filter(id => ms[id]).map(id => {
      const c = el('div.tchip' + (ms[id].blocksStory ? '.blocks' : ''), { tabindex: 0, text: chipText(ms[id]),
        title: ms[id].title + ': side mission, given with ' + m.title + '.' +
          (ms[id].blocksStory ? ' The game counts it as a story mission: the yellow bars wait until it is done.' : ''),
        onclick: () => select(ms[id]), onkeydown: e => { if (e.key === 'Enter') select(ms[id]); } });
      nodes.set(id, c);
      return c;
    });
    return el('div.tstep', null, box, chips.length ? el('div.tchips', null, chips) : null);
  }

  function seq(items) {
    const out = [];
    const wait = m => el('div.twait', null, el('div.bar'), el('span', { text: 'waits until all the story missions above are done' +
      (blockers(m).length ? ', and ' + blockers(m).map(shortTitle).join(', ') : '') }));
    for (const it of items) {
      if (it.branches) {
        // lines: down to the top bar, one per branch, then from the bottom bar down
        const br = el('div.tbr', null, it.branches.map(b => el('div.tcol', null, el('div.vl'), seq(b), el('div.vl.fill'))));
        br.style.setProperty('--edge', (50 / it.branches.length) + '%');
        if (out.length) out.push(el('div.vl'));
        out.push(br);
        continue;
      }
      const m = ms[it.id];
      if (m.waitAll) out.push(wait(m));
      else if (out.length) out.push(el('div.vl'));
      out.push(node(m));
    }
    return el('div.tseq', null, out);
  }

  const tree = el('div.tree', null, seq(d.story), el('div.vl'), el('div.tend', { text: 'END · achievement ' + (d.achievement || '') }));
  const side = el('div.side.missions-side', null,
    el('div.section', { text: 'Story' }),
    el('div.hint.top', { text: 'The FANG story from the game files, top to bottom. Side by side: branches given at the same time. Click a mission for what to do in it.' }),
    tree);
  const content = el('div.content.missions-content');
  view.replaceChildren(side, content);

  // ---------------------------------------------------------------- details
  const titleOf = id => ms[id] ? ms[id].title : id;

  function render() {
    const m = selected;
    for (const [id, e] of nodes) e.classList.toggle('on', id === m.id);
    const when = [];
    if (m.givenBy || !m.number) when.push((m.kind || 'Side') + ' side mission, given with ' + titleOf(m.givenBy) + '.' +
      (m.blocksStory ? '' : ' Not needed for the story.'));
    else {
      when.push('Story mission ' + m.number + ' of ' + storyCount + '.');
      when.push(m.after.length ? 'Comes after ' + m.after.map(titleOf).join(' / ') + '.' : 'The first mission.');
      if (m.waitAll) when.push('Waits until all the story missions given before it are done (both branches)' +
        (blockers(m).length ? ', and ' + blockers(m).map(x => x.title).join(', ') + ' if it is still in progress' : '') + '.');
      if (m.unlocks?.length) when.push('Also gives the side missions ' + m.unlocks.map(titleOf).join(', ') + '.');
    }
    const steps = [];
    m.steps.forEach((st, i) => {
      const own = st.objectives.length > 0;
      steps.push(el('div.caption', { text: 'Step ' + (i + 1) + (own ? '  ·  ' + st.title : '') }));
      for (const o of own ? st.objectives : [st.title === 'Extract' ? 'Extract from the map.' : st.title]) steps.push(el('div.orow', { text: o }));
    });
    if (m.questItems?.length) {
      steps.push(el('div.caption', { text: 'Quest items' }));
      for (const q of m.questItems) steps.push(el('div.orow.q', { text: q }));
    }
    const out = [
      el('button.btn.back', { text: '▲ Story tree', onclick: () => (nodes.get(m.id) || tree).scrollIntoView({ block: 'center' }) }),
      el('h1', { text: (m.number ? m.number + '.  ' : '') + m.title.toUpperCase() }),
      m.blocksStory ? el('p.mtip', { text: 'Needed for the story: the game counts it as a story mission, so the story stops at the next yellow bar until it is done.' }) : null,
      el('div.mmap', { text: mapText(m) + (m.place ? ' · ' + m.place : ''), style: 'color:' + mapColor(m) }),
      el('p.lead', { text: when.join(' ') }),
      el('div.card', null, el('div.head', null, el('span.t', { text: 'WHAT TO DO' }),
        el('span.s', { text: m.steps.length === 1 ? '1 step' : m.steps.length + ' steps, in this order' })), steps, el('div', { style: 'height:8px' })),
      (m.tips || []).map(t => el('p.mtip', { text: 'Tip: ' + t })),
    ];
    if (m.rewards.length) out.push(el('div.card', null,
      el('div.head', null, el('span.t', { text: 'REWARDS' }), el('span.s', { text: m.rewards.length + (m.rewards.length === 1 ? ' item' : ' items') })),
      m.rewards.map(r => el('div.row.rrow', { title: r.name + (r.id === 'Cash' ? '' : ' × ' + r.count) },
        el('div.icon', null, r.icon ? itemIcon(r.id) : el('span', { text: '?' })),
        el('div.name', { text: r.name }),
        el('div.chance', { text: r.id === 'Cash' ? money(r.count) : '× ' + r.count }))),
      el('div', { style: 'height:8px' })));
    if (m.briefing?.length) out.push(el('details.group.brief', null,
      el('summary', null, el('span.gt', { text: 'BRIEFING' }), el('span.ghint', { text: 'the story told by Observer (spoilers)' })),
      m.briefing.map(p => el('p', { text: p }))));
    out.push(el('p.hint', { text: 'Read from the game\'s mission files. The kill counts are shown in game only.' }));
    content.replaceChildren(...out.flat().filter(Boolean));
    content.scrollTop = 0;
  }

  function select(m) {
    selected = m;
    render();
    // phones: the details are under the tree
    if (window.matchMedia('(max-width: 820px)').matches) content.scrollIntoView({ block: 'start' });
  }

  render();
}
