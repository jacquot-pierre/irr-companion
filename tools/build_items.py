r"""Builds the data of the ITEMS tab: every item of the game, by category, with its prices.

Usage:
  python -I tools/build_items.py <extracted root>

<extracted root> is the folder of build_weapons.py / build_traders.py (Test_C/Content/Blueprints/InventorySystem).
Run it after build_traders.py and build_icons.py (it reads src/Resources/traders.json and the icon list).
Writes src/Resources/items.json.

Where the prices come from (cooked with unversioned properties; property orders read from the reflection data of
the game exe):
- Vendor table Vendor/DT_Vendor_Old (IRRVendorDefaultItem rows, read with the schema of build_traders.py):
  MarketAvailability = DefaultMarketPrice (cash), BlackMarketPrice (Marked Coins), bSoldInStockMarket (the market
  sells it), bSoldInBlackMarket, bCanBeSoldInStockMarket (you can sell it there), bCanBeSoldInBlackMarket; the
  rarity tier and FactionAvailability (faction + loyalty level needed to buy it). A row gives an item definition
  (ID_x) or an item preset (DA_x: a weapon with its default parts, a loaded magazine): a preset row goes to the
  preset's item when the item has no row of its own (weapons: the price of the weapon as sold, new).
  The price is per unit (a box of rounds: the price of one round).
- Selling at the market: Settings/DA_VendorSettings (VendorSettings): 0 VendorDataTable, 1 VendorItemsPerPage,
  2 VendorSellMultiplierDefault (0.6), 3 VendorSellMultiplierMap (item tag -> multiplier: Weapons 0.35,
  Attachments 0.45, Gear 0.4, Ammunition 0.4, Barter 0.8, Keys 0.2, Currency 1). Sell price = market price x the
  multiplier of the item's top category (the default for the others). Our reading of the settings, not the game's
  code. 144 rows (barter items, keys, keycards, currency) have bCanBeSoldInStockMarket false: written as noSell
  (shown as uncertain: the Barter multiplier 0.8 suggests barter items can be sold).
- Raid traders: BaseActors/Trader/BP_RaidTrader (class RaidTrader: ... AcceptedPlayerItems, bAcceptCash, bGiveChange,
  PriceMultiplier, PriceOverrides ...). The base blueprint sets PriceOverrides (item tag -> RaidTraderPriceOverride:
  bOverrideHandInPrice, HandInPrice, bOverrideBuyPrice, BuyPrice): what a trader pays for the quest crates. No
  trader blueprint and no placed trader sets PriceMultiplier or a BuyPrice: a trader sells at the market price
  (assuming the game's default multiplier, 1, which is in the game code). What each trader can have in stock comes
  from traders.json (weapons are not listed there).
Every unexpected layout stops the script.
"""
import glob
import json
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from build_weapons import Package, header, read_preset  # noqa: E402
from build_traders import Reader, VENDOR_ROW, item_ref, read_items, _bool  # noqa: E402

RES = os.path.join(HERE, '..', 'src', 'Resources')
OUT = os.path.join(RES, 'items.json')
FACTIONS = {'Mission.Faction.BLUEFOR': 'UICS', 'Mission.Faction.OPFOR': 'IGC', 'Mission.Faction.GREYFOR': 'VLF',
            'Mission.Faction.FANG': 'FANG'}
# order and names of the categories (first part of the item tag after Inventory.Items.)
CATEGORIES = [('Weapons', 'Weapons'), ('Attachments', 'Weapon parts'), ('Ammunition', 'Ammunition'), ('Grenades', 'Grenades'),
              ('Melee', 'Melee'), ('Gear', 'Gear'), ('Medical', 'Medical'), ('Provisions', 'Food and drinks'),
              ('Barter', 'Barter'), ('Containers', 'Containers'), ('Gadgets', 'Gadgets'), ('Keys', 'Keys'),
              ('Quest', 'Quest items'), ('Currency', 'Currency')]
NO_SUBS = {'Ammunition', 'Grenades', 'Melee', 'Medical', 'Containers', 'Gadgets', 'Currency'}
OVERRIDE = [('bOverrideHandInPrice', _bool), ('HandInPrice', Reader.i32), ('bOverrideBuyPrice', _bool), ('BuyPrice', Reader.i32)]


def fail(msg):
    raise SystemExit('build_items: ' + msg)


def vendor_rows(inv):
    pkg = Package(os.path.join(inv, 'Vendor', 'DT_Vendor_Old.uasset'))
    b = pkg.data(1)
    count = struct.unpack_from('<i', b, 10)[0]
    rows = []
    for m in re.finditer(rb'\x80\x19(..)\x80\x13(..)', b, re.S):
        r = Reader(pkg, b, m.start())
        props, n = header(b[m.start():])
        r.o = m.start() + n
        row = {}
        for i, zero in props:
            if i >= len(VENDOR_ROW):
                fail('vendor row: unknown field %d' % i)
            name, fn = VENDOR_ROW[i]
            row[name] = None if zero else fn(r)
        rows.append(row)
    if len(rows) != count:
        fail('vendor table: %d rows read of %d' % (len(rows), count))
    return rows


def sell_multipliers(inv):
    """(default, {top category: multiplier}) of DA_VendorSettings."""
    pkg = Package(os.path.join(inv, 'Settings', 'DA_VendorSettings.uasset'))
    b = pkg.data(1)
    props, n = header(b)
    idx = [i for i, z in props if not z]
    if idx[:3] != [0, 2, 3]:
        fail('vendor settings layout %s' % props)
    r = Reader(pkg, b, n + 4)  # after the data table reference
    default = r.f32()
    mults = dict(r.tmap(Reader.tag_struct, Reader.f32))
    if not 0 < default <= 1 or not mults:
        fail('vendor settings: sell multipliers not understood (%s, %s)' % (default, mults))
    return round(default, 4), {t.split('.')[2]: round(v, 4) for t, v in mults.items()}


def hand_in_prices(inv):
    """Item tag -> price a raid trader pays for it (PriceOverrides of BP_RaidTrader)."""
    path = os.path.join(inv, 'BaseActors', 'Trader', 'BP_RaidTrader.uasset')
    pkg = Package(path)
    k = [i for i, e in enumerate(pkg.exports, 1) if e['name'] == 'Default__BP_RaidTrader_C']
    if len(k) != 1:
        fail('BP_RaidTrader: no class defaults')
    b = pkg.data(k[0])
    out = {}
    for m in re.finditer(rb'\x00\x03(.{8})(?=\x80\x09)', b, re.S):
        tag = pkg.fname(b, m.start() + 2)
        if not tag.startswith('Inventory.Items.'):
            continue
        r = Reader(pkg, b, m.end())
        o = r.struct(OVERRIDE, 'price override')
        if o.get('bOverrideBuyPrice'):
            fail('a trader buy price override (%s): show it' % tag)
        if o.get('bOverrideHandInPrice') and o.get('HandInPrice'):
            out[tag] = o['HandInPrice']
    # the trader blueprints must not change the prices (indexes of RaidTrader: PriceMultiplier 18, PriceOverrides 19)
    for p in glob.glob(os.path.join(inv, 'BaseActors', 'Trader', 'BP_RaidTrader_*.uasset')):
        pk = Package(p)
        for i, e in enumerate(pk.exports, 1):
            if e['name'].startswith('Default__'):
                props, _ = header(pk.data(i))
                if any(j in (18, 19) for j, _ in props):
                    fail('%s sets its own prices: read them' % os.path.basename(p))
    return out


def trader_stock():
    """Item id -> [{trader, who, chance}] from traders.json."""
    d = json.load(open(os.path.join(RES, 'traders.json'), encoding='utf-8'))
    out = {}
    for t in d['traders']:
        who = ' & '.join(p['name'] for p in t.get('people', []))
        best = {}
        for it in t['fixed']:
            best[it['id']] = 1.0
        for g in t['choices']:
            for it in g['items']:
                best[it['id']] = max(best.get(it['id'], 0), it['chance'])
        for g in t['random']:
            for c in g['categories']:
                for it in c['items']:
                    best[it['id']] = max(best.get(it['id'], 0), it['chance'])
        for k, ch in best.items():
            out.setdefault(k, []).append({'trader': t['id'], 'who': who, 'chance': round(ch, 4)})
    return out


def main():
    inv = os.path.join(sys.argv[1], 'Test_C', 'Content', 'Blueprints', 'InventorySystem')
    if not os.path.isdir(inv):
        fail('no InventorySystem in ' + sys.argv[1])
    names = read_items(inv)
    default, mults = sell_multipliers(inv)
    print('sell multipliers: default %s, %s' % (default, mults))
    hand_in = hand_in_prices(inv)
    print('trader hand-in prices:', hand_in)
    stock = trader_stock()
    icons = {f[5:-4] for f in os.listdir(os.path.join(RES, 'icons')) if f.startswith('item_')}
    weapons = json.load(open(os.path.join(RES, 'weapons.json'), encoding='utf-8'))
    wclass = {c['id'].lower(): c['name'] for c in weapons['classes']}

    rows = {}
    preset_rows = {}
    for row in vendor_rows(inv):
        ref = item_ref(row.get('ItemData') or {})
        if not ref:
            continue
        if ref.startswith('DA_'):
            target = read_preset(inv, ref)[0]
            if target and target.startswith('ID_'):
                preset_rows.setdefault(target[3:], row)
            continue
        rows.setdefault(ref[3:], row)
    for k, row in preset_rows.items():
        rows.setdefault(k, row)

    order = {c: i for i, (c, _) in enumerate(CATEGORIES)}
    items, unpriced = [], 0
    for k, (name, tag) in names.items():
        parts = (tag or 'Inventory.Items.Other').split('.')
        cat = parts[2]
        sub = parts[3] if len(parts) > 3 else None
        if cat not in order:
            print('WARNING: %s: unknown category %s (add it to CATEGORIES)' % (k, cat))
            continue
        if cat == 'Weapons' and sub:
            sub = wclass.get(sub.lower(), sub)
        if cat in NO_SUBS:
            sub = None  # one item per sub-tag, or a single sub-category
        if cat == 'Quest' and sub not in ('Defense', 'Operation Plans'):
            sub = None
        it = {'id': k, 'name': name, 'cat': cat}
        if sub:
            it['sub'] = sub
        if k in icons:
            it['icon'] = True
        row = rows.get(k)
        if row:
            mk = row.get('MarketAvailability') or {}
            price = mk.get('DefaultMarketPrice') or 0
            if price > 0:
                it['price'] = round(price, 2)
                if mk.get('bSoldInStockMarket'):
                    it['buy'] = True
                # sell price for every priced item; flagged when the table says it cannot be sold at the market
                # (barter items: 0.8 in the sell multipliers, but bCanBeSoldInStockMarket false: to check in game)
                it['sell'] = round(price * mults.get(cat, default), 2)
                if not mk.get('bCanBeSoldInStockMarket'):
                    it['noSell'] = True
            bm = mk.get('BlackMarketPrice') or 0
            if mk.get('bSoldInBlackMarket') and bm > 0:
                it['coins'] = round(bm, 2)
            tier = row.get('ItemRarityTag')
            if tier:
                it['tier'] = int(tier.rsplit(' ', 1)[1])
            fa = row.get('FactionAvailability') or {}
            if fa.get('RequiredLoyaltyLevel') and fa.get('FactionTag') in FACTIONS and it.get('buy'):
                it['loyalty'] = {'faction': FACTIONS[fa['FactionTag']], 'level': int(fa['RequiredLoyaltyLevel'])}
        else:
            unpriced += 1
        if k in stock:
            it['traders'] = stock[k]
        if tag in hand_in:
            it['handIn'] = hand_in[tag]
        items.append(it)
    items.sort(key=lambda it: (order[it['cat']], it.get('sub') or '', it['name'].lower()))
    cats = []
    for c, label in CATEGORIES:
        subs = []
        for it in items:
            if it['cat'] == c and it.get('sub') and it['sub'] not in subs:
                subs.append(it['sub'])
        n = sum(1 for it in items if it['cat'] == c)
        if n:
            cats.append({'id': c, 'name': label, 'count': n, 'subs': subs})
    doc = {'version': 1, 'sellDefault': default, 'sellMultipliers': mults, 'categories': cats, 'items': items}
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print('%d items (%d without a price), %d sold at the market, %d at the black market, %d at a trader -> %s' % (
        len(items), unpriced, sum(1 for i in items if i.get('buy')), sum(1 for i in items if 'coins' in i),
        sum(1 for i in items if 'traders' in i), os.path.normpath(OUT)))
    for c in cats:
        print('  %-14s %3d  %s' % (c['name'], c['count'], ', '.join(c['subs'])))


if __name__ == '__main__':
    main()
