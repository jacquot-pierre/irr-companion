r"""Builds the data of the MISSIONS tab: the story missions, the order they come in, what to do in each one and
what they give.

Usage:
  python -I tools/build_missions.py "<game>\Test_C\Content\Paks" [path\to\repak.exe]

Writes src/Resources/missions.json. Item names come from weapons.json, traders.json and loot.json (run their
scripts first after a game update).

Where the data comes from (Blueprints/MissionSystem, cooked with unversioned properties):
- The order: the mission graph Graphs/MG_MainStory (class IRRMissionGraph). Its nodes are
  IRRMissionGraphNode_Stage (one mission: an import of its data asset, first value of the node),
  IRRMissionGraphNode_Selector (where two branches meet again) and IRRMissionGraphNode_End. The edges
  (IRRMissionGraphEdge) end with <start node> <end node> (export indexes); an edge 10 bytes longer carries a
  condition, always BP_GraphCondition_AllStoryMissionsCompleted: the next mission waits until every story mission
  given so far is done. A stage can hold events: BP_MissionEvent_TriggerMissions (an array of mission imports:
  the reconnaissance side missions it gives), UpdateMissionPoolSize, UnlockAchievement ("Eyes Opened" at the end).
- A mission: DataAssets/Factions/FANG/DAWN/{StoryMissions,SideMissions}/DA_FANG_OBSERVER_<id> (IRRTaskSetup).
  Its texts are FText values read in order: title first, then briefing / debriefing texts, each after its
  localization key (<id>.Briefing.Text.NN, <id>.Debriefing.Text.NN); unkeyed texts after the debriefing are tips
  ("Buy the bombs at the marketplace."). The setup ends with the array of its steps (IRRMissionSequence, or
  BP_Sequence_Extract = extract), each step with its title and an array of objectives (BP_IRRObjective_* /
  BP_IRRObjectie_*, IRRMissionObjective) that have their own text (none: the step title says it all; "Hidden":
  not shown in game). The maps are the Maps.<Map> tags of the package (all four for the traders one), the place a Maps.<Map>.<Place> tag.
- Rewards: entries of the setup that start with the header 80 13 and an import of an item (ID_*) or of an item
  preset (DA_*, a weapon or magazine preset: its item is the ID_* import of the preset package). The count is the
  int32 18 bytes after the last -1.0 double of the entry (checked: cash 2400 for "Shipping and Handling").
  The objective counts ({count}) are not decoded: the text leaves the number out.
- The story flag: bStoryMission of the setup. The setup properties, in the order of their unversioned index (read
  from the PropPointers of the setup class in the exe): 0 Faction, 1 Agent, 2 MissionType, 3 MissionImage,
  4 MissionInfo, 5 bStoryMission, 6 bPersist, 7 MaxRaids, 8 RewardItemPresets, 9 FactionRewards, 10 XPReward,
  11 bReset, 12 SpawnData, 13 SupportedPlayerSpawns, 14 Sequences, 15-17 OnStarted/OnCompleted/OnFailedEvents.
  A bool is only in the property header: index 5 present (and not zero) = true. The wait of the story graph
  (AllStoryMissionsCompleted) checks the missions in progress that have it: a side mission with the flag
  (Reconnaissance - Traders) holds the story until it is done ("blocksStory" in the json).
Every unexpected layout stops the script.
"""
import glob
import json
import os
import re
import shutil
import struct
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_hideout import package_full  # noqa: E402
from build_icons import repak_list, extract  # noqa: E402

RES = os.path.join(HERE, '..', 'src', 'Resources')
OUT = os.path.join(RES, 'missions.json')
BASE = 'Test_C/Content/Blueprints/MissionSystem/'
GRAPH = BASE + 'Graphs/MG_MainStory'
MISSIONS_DIR = BASE + 'DataAssets/Factions/FANG/DAWN/'
PRESETS_DIR = 'Test_C/Content/Blueprints/InventorySystem/Presets/'
OBJECTIVE_CLASSES = ('BP_IRRObjective_', 'BP_IRRObjectie_', 'IRRMissionObjective')
STEP_CLASSES = ('IRRMissionSequence', 'BP_Sequence_Extract_C')
MAPS = ('Delta', 'Quarry', 'Outskirts', 'Bunker')
# side missions: readable kind
SIDE_KIND = 'Reconnaissance'


def fail(msg):
    raise SystemExit('build_missions: ' + msg)


# ------------------------------------------------------------------ texts
def fstrings(b):
    """FStrings of a blob, left to right (ANSI: positive length; UTF-16: negative length; both end with NUL)."""
    out, k = [], 0
    while k + 5 <= len(b):
        n = struct.unpack_from('<i', b, k)[0]
        if 2 <= n <= 20000 and k + 4 + n <= len(b) and b[k + 3 + n] == 0:
            raw = b[k + 4:k + 3 + n]
            if all(32 <= c < 127 or c in (9, 10, 13) or c >= 0xa0 for c in raw):
                out.append(raw.decode('latin1'))
                k += 4 + n
                continue
        if -20000 <= n <= -2 and k + 4 - 2 * n <= len(b) and b[k + 2 - 2 * n:k + 4 - 2 * n] == b'\0\0':
            try:
                t = b[k + 4:k + 2 - 2 * n].decode('utf-16-le')
                if all(c.isprintable() or c in '\r\n\t' for c in t):
                    out.append(t)
                    k += 4 - 2 * n
                    continue
            except UnicodeDecodeError:
                pass
        k += 1
    return out


def texts(b):
    """Texts of an export: '#key' for a localization key, the text itself otherwise (namespaces, GUID keys left out)."""
    res = []
    for t in fstrings(b):
        if t in ('StoryMissionBriefing', 'MissionBriefing') or not t.strip() or re.fullmatch(r'[0-9A-F]{32}', t):
            continue
        if re.fullmatch(r'[\w.]+\.(Text|Subtitle)\.\d+|ObjectiveComplete_\w+', t):
            res.append('#' + t)
        else:
            res.append(t)
    return res


def plain(t):
    """Rich text (<strong>x</>, <italic>x</>) -> plain text."""
    t = re.sub(r'<[^>]*>', '', t)
    return re.sub(r'[ \t]+', ' ', t).strip()


KEY = re.compile(r'[\w.]+\.(Text|Subtitle)\.\d+|[0-9A-F]{32}')


def briefing_texts(b, title, mid):
    """(briefing texts, tips). After the title come the briefing count, then (key, text) pairs: the briefing
    texts, the debriefing texts (keys <id>.Debriefing.Text.NN, or a GUID), then the tips (GUID keys after
    Debriefing keys: "Buy the bombs at the marketplace.")."""
    end = b.find(title.encode('latin1') + b'\0')
    if end < 0:
        fail(mid + ': title not found')
    end += len(title) + 1
    n = struct.unpack_from('<i', b, end + 2)[0]
    pairs, key = [], None
    for t in fstrings(b[end:]):
        if t in ('StoryMissionBriefing', 'MissionBriefing'):
            continue
        if KEY.fullmatch(t):
            key = t
        elif key is not None:
            pairs.append((key, t))
            key = None
    if not 0 <= n <= len(pairs):
        fail('%s: %d briefing texts for %d texts' % (mid, n, len(pairs)))
    briefing = [plain(t) for _, t in pairs[:n] if t != 'PLACEHOLDER']
    seen_debrief, tips = False, []
    for k, t in pairs[n:]:
        if '.Debriefing.' in k:
            seen_debrief = True
        elif seen_debrief and t != 'PLACEHOLDER':
            tips.append(plain(t))
    return briefing, tips


# ------------------------------------------------------------------ items
class Items:
    def __init__(self, presets):
        self.names = {}
        for f in ('loot.json', 'traders.json', 'weapons.json'):
            path = os.path.join(RES, f)
            if os.path.exists(path):
                for k, v in json.load(open(path, encoding='utf-8')).get('items', {}).items():
                    if v.get('name'):
                        self.names[k] = v['name']
        self.presets = presets  # DA_x -> ID_y
        self.icons = {f[5:-4] for f in os.listdir(os.path.join(RES, 'icons')) if f.startswith('item_')}

    def resolve(self, ref):
        """Import name (ID_x or DA_x preset) -> (item id, readable name, has an icon)."""
        iid = self.presets.get(ref, ref)
        if not iid.startswith('ID_'):
            fail('item preset without an item: ' + ref)
        iid = iid[3:]
        name = self.names.get(iid) or iid.replace('_', ' ')
        return iid, name, iid in self.icons


def preset_items(tmp):
    """Item preset DA_x -> its main item: the ID_ import of the preset package closest to the preset name
    (DA_AKS-74U_Default -> ID_AKS-74U, not its parts or rounds)."""
    def common(a, b):
        n = 0
        while n < min(len(a), len(b)) and a[n].lower() == b[n].lower():
            n += 1
        return n
    out = {}
    for path in glob.glob(os.path.join(tmp, PRESETS_DIR.replace('/', os.sep), '**', 'DA_*.uasset'), recursive=True):
        _, imports, _ = package_full(path)
        name = os.path.basename(path)[:-7]
        key = re.sub(r'_Default$', '', name[3:])
        ids = [n for n in imports if n.startswith('ID_')]
        if ids:
            out[name] = max(ids, key=lambda n: (common(n[3:], key), -len(n)))
    return out


def rewards(b, imports, items):
    refs = {-(i + 1): n for i, n in enumerate(imports) if n.startswith(('ID_', 'DA_'))}
    entries = []
    for h in range(len(b) - 16):
        if b[h:h + 2] == b'\x80\x13':
            found = [refs[struct.unpack_from('<i', b, k)[0]] for k in range(h + 2, h + 12)
                     if struct.unpack_from('<i', b, k)[0] in refs]
            if found:
                # an entry can hold an item preset and an item definition: the item wins (the preset is then a
                # leftover, e.g. the AKS-74U preset next to .338 AP rounds)
                ids = [f for f in found if f.startswith('ID_')]
                entries.append((h, h + 2, (ids or found)[0]))
    out = []
    for j, (h, k, ref) in enumerate(entries):
        end = entries[j + 1][0] if j + 1 < len(entries) else len(b)
        f = b.rfind(b'\x00\x00\x00\x00\x00\x00\xf0\xbf', k, end)  # last -1.0 double of the entry
        if f < 0:
            fail('reward entry without its -1.0 values: ' + ref)
        count = struct.unpack_from('<i', b, f + 8 + 18 - 2)[0]
        if j + 1 < len(entries) and f + 24 != end - 8:
            fail('reward entry of unexpected size: ' + ref)
        if not 1 <= count <= 1000000:
            fail('reward count out of range: %s %d' % (ref, count))
        iid, name, icon = items.resolve(ref)
        same = [r for r in out if r['id'] == iid]
        if same:
            same[0]['count'] += count  # the same item in two entries: one line
        else:
            out.append({'id': iid, 'name': name, 'count': count, 'icon': icon})
    return out


# ------------------------------------------------------------------ one mission
def ordered(blob, children):
    """Children (export indexes) in the order of the array that lists them in the blob (count + indexes)."""
    best = []
    for k in range(0, len(blob) - 7):
        n = struct.unpack_from('<i', blob, k)[0]
        if 1 <= n <= len(children) and k + 4 + 4 * n <= len(blob):
            v = list(struct.unpack_from('<%di' % n, blob, k + 4))
            if all(x in children for x in v) and len(set(v)) == n and n > len(best):
                best = v
    return best + [c for c in sorted(children) if c not in best]


PROP_STORY, PROP_SEQUENCES = 5, 14  # unversioned indexes of bStoryMission and Sequences in the setup


def setup_props(b):
    """Unversioned header of a setup -> (indexes present, indexes flagged zero)."""
    o, i, frags = 0, 0, []
    while True:
        v = struct.unpack_from('<H', b, o)[0]
        o += 2
        frags.append((v & 0x7f, v >> 9, bool(v & 0x80)))
        if v & 0x100:
            break
    nz = sum(n for _, n, z in frags if z)
    mask = int.from_bytes(b[o:o + (nz + 7) // 8], 'little') if nz else 0
    present, zeros, zi = set(), set(), 0
    for skip, n, z in frags:
        i += skip
        for _ in range(n):
            if z:
                if mask >> zi & 1:
                    zeros.add(i)
                zi += 1
            present.add(i)
            i += 1
    return present, zeros


def mission(path, items):
    names, imports, exports = package_full(path)
    d = open(path[:-6] + 'uexp', 'rb').read()
    blob = [d[e['offset']:e['offset'] + e['size']] for e in exports]
    mid = os.path.basename(path)[:-7].replace('DA_FANG_OBSERVER_', '')
    setup = [i for i, e in enumerate(exports, 1) if e['class'] == 'IRRTaskSetup']
    if len(setup) != 1:
        fail(mid + ': no single IRRTaskSetup')
    s = setup[0]
    t = texts(blob[s - 1])
    if not t or t[0].startswith('#'):
        fail(mid + ': no title')
    title = plain(t[0])
    briefing, tips = briefing_texts(blob[s - 1], t[0], mid)
    quest = [n.split('.', 3)[3] for n in names if n.startswith('Inventory.Items.Quest.')]
    maps = [n.split('.')[1] for n in names if re.fullmatch(r'Maps\.\w+', n) and n.split('.')[1] in MAPS]
    places = [n.split('.', 2)[2] for n in names if n.startswith('Maps.') and n.count('.') >= 2]
    if not maps:
        fail('%s: no map tag' % mid)

    def children(parent, prefixes):
        return {i for i, e in enumerate(exports, 1) if e['outer'] == parent and e['class'].startswith(prefixes)}

    steps = []
    for st in ordered(blob[s - 1], children(s, STEP_CLASSES)):
        cls = exports[st - 1]['class']
        st_t = [x for x in texts(blob[st - 1]) if not x.startswith('#')]
        objs = []
        for ob in ordered(blob[st - 1], children(st, OBJECTIVE_CLASSES)):
            o_t = [plain(x) for x in texts(blob[ob - 1]) if not x.startswith('#')]
            if not o_t:
                kind = re.sub(r'^BP_IRRObjectiv?e?_|_C$', '', exports[ob - 1]['class'])
                if kind == 'Extract':
                    o_t = ['Extract.']
                else:
                    continue  # the step title says it
            text = o_t[0]
            if text == 'Hidden':
                continue
            target = quest[0] if len(quest) == 1 else 'each trader' if text.startswith('Trade') else 'the target'
            text = text.replace('{count} ', '').replace('{target}', target)
            if text not in objs:
                objs.append(text)
        if cls == 'BP_Sequence_Extract_C':
            steps.append({'title': 'Extract', 'objectives': []})
            continue
        if not st_t:
            fail('%s: step without a title' % mid)
        title_st = plain(st_t[0])
        if [o.rstrip('.') for o in objs] == [title_st.rstrip('.')]:
            objs = []
        steps.append({'title': title_st, 'objectives': objs})
    out = {'title': title, 'maps': maps, 'steps': steps}
    if len(places) == 1:  # one place to go (a reconnaissance lists all the places it asks for)
        out['place'] = places[0]
    if quest:
        out['questItems'] = quest
    if briefing:
        out['briefing'] = briefing
    if tips:
        out['tips'] = tips
    out['rewards'] = rewards(blob[s - 1], imports, items)
    present, zeros = setup_props(blob[s - 1])
    if PROP_SEQUENCES not in present:
        fail(mid + ': setup header not understood (no Sequences at index %d)' % PROP_SEQUENCES)
    out['storyFlag'] = PROP_STORY in present and PROP_STORY not in zeros
    return mid, out


# ------------------------------------------------------------------ the story graph
def story(path):
    names, imports, exports = package_full(path)
    d = open(path[:-6] + 'uexp', 'rb').read()
    blob = {i: d[e['offset']:e['offset'] + e['size']] for i, e in enumerate(exports, 1)}
    node, kind = {}, {}
    for i, e in enumerate(exports, 1):
        if e['class'] == 'IRRMissionGraphNode_Stage':
            b = blob[i]
            refs = [struct.unpack_from('<i', b, k)[0] for k in range(len(b) - 3)]
            refs = [r for r in refs if r < 0 and -r <= len(imports) and imports[-r - 1].startswith('DA_FANG_OBSERVER_')]
            if not refs:
                fail('stage %d without a mission' % i)
            node[i] = imports[-refs[0] - 1].replace('DA_FANG_OBSERVER_', '')
            kind[i] = 'stage'
        elif e['class'] == 'IRRMissionGraphNode_Selector':
            kind[i] = 'join'
        elif e['class'] == 'IRRMissionGraphNode_End':
            kind[i] = 'end'
    nxt, cond = {}, set()
    for i, e in enumerate(exports, 1):
        if e['class'] == 'IRRMissionGraphEdge':
            b = blob[i]
            a, c = struct.unpack_from('<ii', b, len(b) - 12)
            if a not in kind or c not in kind:
                fail('edge %d: unknown nodes' % i)
            nxt.setdefault(a, []).append(c)
            if len(b) == 28:
                cond.add((a, c))
            elif len(b) != 18:
                fail('edge %d of unexpected size' % i)
    # events: missions given by a stage (TriggerMissions), achievement at the end
    unlocks = {}
    for i, e in enumerate(exports, 1):
        if e['class'] == 'BP_MissionEvent_TriggerMissions_C' and e['outer'] in node:
            b = blob[i]
            n = struct.unpack_from('<i', b, 2)[0]
            ms = [imports[-r - 1].replace('DA_FANG_OBSERVER_', '') for r in struct.unpack_from('<%di' % n, b, 6)]
            unlocks.setdefault(node[e['outer']], []).extend(ms)
    roots = [i for i in kind if kind[i] == 'stage' and not any(i in v for v in nxt.values())]
    if len(roots) != 1:
        fail('story graph: %d roots' % len(roots))

    def chain(i, stop, wait_in):
        """Items from node i until node stop (excluded); returns (items, node reached)."""
        items = []
        while i != stop and kind[i] != 'end':
            if kind[i] == 'join':
                return items, i
            item = {'id': node[i]}
            if wait_in:
                item['waitAll'] = True
            items.append(item)
            out = nxt.get(i, [])
            if len(out) == 1:
                wait_in = (i, out[0]) in cond
                i = out[0]
            elif len(out) == 2:
                branches, joins = [], set()
                for o in out:
                    br, j = chain(o, None, (i, o) in cond)
                    branches.append(br)
                    joins.add(j)
                if len(joins) != 1:
                    fail('branches of %s do not meet again' % node[i])
                j = joins.pop()
                items.append({'branches': branches})
                after = nxt.get(j, [])
                if len(after) != 1:
                    fail('join without a single next node')
                wait_in = (j, after[0]) in cond
                i = after[0]
            elif not out:
                fail('dead end at ' + node[i])
            else:
                fail('more than two branches at ' + node[i])
        return items, i

    items, end = chain(roots[0], None, False)
    if kind[end] != 'end':
        fail('the story does not reach its end node')
    achievement = None
    for i, e in enumerate(exports, 1):
        if e['class'] == 'BP_MissionEvent_UnlockAchievement_C':
            t = fstrings(blob[i])
            achievement = t[0] if t else None
    return items, unlocks, achievement


def flat(items):
    for it in items:
        if 'branches' in it:
            for br in it['branches']:
                yield from flat(br)
        else:
            yield it['id']


def main():
    paks = sys.argv[1]
    repak = sys.argv[2] if len(sys.argv) > 2 else (shutil.which('repak') or r'C:\Program Files\repak_cli\bin\repak.exe')
    print('listing the paks...')
    files = repak_list(repak, paks)
    tmp = tempfile.mkdtemp(prefix='irr_missions_')
    try:
        wanted = [f for f in files if (f.startswith(MISSIONS_DIR) or f.startswith(GRAPH) or f.startswith(PRESETS_DIR))
                  and f.endswith(('.uasset', '.uexp'))]
        print('extracting %d files...' % len(wanted))
        extract(repak, files, wanted, tmp)
        items = Items(preset_items(tmp))
        order, unlocks, achievement = story(os.path.join(tmp, (GRAPH + '.uasset').replace('/', os.sep)))
        missions = {}
        side = set(m for v in unlocks.values() for m in v)
        for mid in list(flat(order)) + sorted(side):
            path = os.path.join(tmp, (MISSIONS_DIR + ('SideMissions' if mid in side else 'StoryMissions')
                                      + '/DA_FANG_OBSERVER_' + mid + '.uasset').replace('/', os.sep))
            if not os.path.exists(path):
                fail('mission asset not found: ' + mid)
            k, m = mission(path, items)
            m['kind'] = SIDE_KIND if mid in side else 'Story'
            flag = m.pop('storyFlag')
            if mid in side and flag:
                m['blocksStory'] = True  # counted by the waits of the story
            elif mid not in side and not flag:
                print('warning: story mission %s without the story flag' % mid)
            if mid in unlocks:
                m['unlocks'] = unlocks[mid]
            missions[k] = m
        data = {'version': 1, 'story': order, 'achievement': achievement, 'missions': missions}
        with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
            f.write('\n')
        n_story = len(list(flat(order)))
        print('wrote %s: %d story missions, %d side missions, achievement %s' % (OUT, n_story, len(side), achievement))
        print('side missions that hold the story:', [m['title'] for m in missions.values() if m.get('blocksStory')])
        for mid, m in missions.items():
            print('  %-8s %-34s %-9s %d steps, %d rewards' % (mid, m['title'], '/'.join(m['maps']), len(m['steps']), len(m['rewards'])))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


if __name__ == '__main__':
    main()
