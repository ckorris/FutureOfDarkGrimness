#!/usr/bin/env python3
"""#416 - census two OPR Army Forge JSON snapshot folders (an OLD ruleset vs a NEW one) and write a Markdown
report of what changed: book roster (new / gone / renamed), every special-rule NAME referenced by units,
weapons, items and upgrade gains (added / removed), book-defined rule DEFINITIONS whose text changed, new
definitions, dropped definitions, and per-book unit-roster / spell / weapon-name churn.

Usage:
  scripts/census-opr-books.py <oldSnapshotDir> <newSnapshotDir> <out.md> [--alias "New Name=Old Name" ...]

The snapshot folders are the off-repo `/api/army-books/<uid>?gameSystem=N` dumps (one `<Book Name>.json`
per book, `_index.json` ignored). Books with no `units` key (OPR's 0-unit parent entries such as
"Wormhole Daemons") are skipped. Aliases map a renamed 3.6 book onto its 3.5 counterpart.

Reference run (2026-10-29, 3.5.x -> 3.6.0): see `docs/opr-3.6-upgrade-plan.md`; outputs live in the
local-only folder `~/Projects/GDF Armies/Rules 3.6.0/`.
"""
import collections
import glob
import json
import os
import re
import sys


def load(dirpath):
    books = {}
    for f in sorted(glob.glob(os.path.join(dirpath, '*.json'))):
        if f.endswith('_index.json'):
            continue
        d = json.load(open(f))
        if not d.get('units'):
            continue
        books[d['name']] = d
    return books


def rule_refs(book):
    """rule name -> set of attachment contexts seen in this book."""
    refs = collections.defaultdict(set)

    def weapon_rules(w, ctx):
        for r in w.get('specialRules') or []:
            refs[r['name']].add(ctx + ':weapon')

    def content_rules(content, ctx):
        for c in content or []:
            if c.get('type') == 'ArmyBookRule':
                refs[c['name']].add(ctx)
            elif c.get('type') == 'ArmyBookWeapon':
                weapon_rules(c, ctx)

    for u in book['units']:
        for r in u.get('rules') or []:
            refs[r['name']].add('unit')
        for w in u.get('weapons') or []:
            weapon_rules(w, 'unit')
        for it in u.get('items') or []:
            content_rules(it.get('content'), 'item')
    for up in book.get('upgradePackages') or []:
        for sec in up.get('sections') or []:
            for opt in sec.get('options') or []:
                for g in opt.get('gains') or []:
                    t = g.get('type')
                    if t == 'ArmyBookRule':
                        refs[g['name']].add('upgrade')
                    elif t == 'ArmyBookWeapon':
                        weapon_rules(g, 'upgrade')
                    elif t == 'ArmyBookItem':
                        content_rules(g.get('content'), 'upgrade-item')
    return refs


def defs(book):
    return {r['name']: r for r in (book.get('specialRules') or [])}


def norm(s):
    return re.sub(r'\s+', ' ', (s or '').replace('”', '"').replace('“', '"').replace('’', "'")).strip()


def most_common_text(by_book):
    return collections.Counter(by_book.values()).most_common(1)[0][0]


def report(old_dir, new_dir, aliases):
    old, new = load(old_dir), load(new_dir)
    out = [f'# Census: {old_dir} ({len(old)} books) vs {new_dir} ({len(new)} books)', '']
    mapped = {n: aliases.get(n, n) for n in new}
    out.append('## Book roster')
    out.append('- new (no old counterpart): ' + ', '.join(sorted(n for n, o in mapped.items() if o not in old)))
    out.append('- gone: ' + ', '.join(sorted(set(old) - set(mapped.values()))))
    out.append('- renamed: ' + ', '.join(f'{o} -> {n}' for n, o in sorted(mapped.items()) if o != n and o in old))
    out.append('')

    old_refs, new_refs = collections.defaultdict(set), collections.defaultdict(set)
    old_defs, new_defs = {}, {}
    for n, b in new.items():
        for k in rule_refs(b):
            new_refs[k].add(n)
        for k, v in defs(b).items():
            new_defs.setdefault(k, {})[n] = v.get('description', '')
    for n, b in old.items():
        for k in rule_refs(b):
            old_refs[k].add(n)
        for k, v in defs(b).items():
            old_defs.setdefault(k, {})[n] = v.get('description', '')

    added, removed = sorted(set(new_refs) - set(old_refs)), sorted(set(old_refs) - set(new_refs))
    out.append('## Rule names referenced anywhere (units, weapons, items, upgrades)')
    out.append(f'- old distinct names: {len(old_refs)}; new distinct names: {len(new_refs)}')
    out.append(f'- NEW names ({len(added)}):')
    for k in added:
        bs = sorted(new_refs[k])
        out.append(f'  - {k}  [{len(bs)} books: {", ".join(bs[:6])}{"..." if len(bs) > 6 else ""}]')
    out.append(f'- REMOVED names ({len(removed)}):')
    for k in removed:
        out.append(f'  - {k}  [was in {len(old_refs[k])} books]')
    out.append('')

    changed = []
    for k in sorted(set(old_defs) & set(new_defs)):
        od, nd = most_common_text(old_defs[k]), most_common_text(new_defs[k])
        if norm(od) != norm(nd):
            changed.append((k, od, nd))
    out.append('## Book-defined special rules whose description text changed (same name)')
    out.append(f'{len(changed)} changed definitions.')
    for k, od, nd in changed:
        out += [f'### {k}', f'- OLD: {od}', f'- NEW: {nd}']
    out.append('')
    out.append('## New book-defined special rules (definitions present in the new snapshot only)')
    for k in sorted(set(new_defs) - set(old_defs)):
        out.append(f'- **{k}** ({len(new_defs[k])} books): {most_common_text(new_defs[k])}')
    out.append('')
    out.append('## Book-defined special rules dropped')
    out.append(', '.join(sorted(set(old_defs) - set(new_defs))))
    out.append('')

    out.append('## Per-book unit roster and spell changes')
    for n in sorted(new):
        o = old.get(mapped[n])
        if not o:
            out.append(f'- {n}: NEW BOOK ({len(new[n]["units"])} units)')
            continue
        ou, nu = {u['name'] for u in o['units']}, {u['name'] for u in new[n]['units']}
        osp = {s['name']: s for s in o.get('spells') or []}
        nsp = {s['name']: s for s in new[n].get('spells') or []}
        sp_changed = [k for k in set(osp) & set(nsp)
                      if (osp[k].get('threshold'), norm(osp[k].get('effect'))) != (nsp[k].get('threshold'), norm(nsp[k].get('effect')))]
        ow = {w['name'] for u in o['units'] for w in u.get('weapons') or []}
        nw = {w['name'] for u in new[n]['units'] for w in u.get('weapons') or []}
        out.append(f'- {n}: units {len(ou)}->{len(nu)} (+{len(nu - ou)} / -{len(ou - nu)}); spells added {sorted(set(nsp) - set(osp))}, '
                   f'removed {sorted(set(osp) - set(nsp))}, text/threshold changed {len(sp_changed)}; base weapon names new {len(nw - ow)} gone {len(ow - nw)}')
        if nu - ou:
            out.append(f'    - new units: {", ".join(sorted(nu - ou))}')
        if ou - nu:
            out.append(f'    - gone units: {", ".join(sorted(ou - nu))}')
    return '\n'.join(out) + '\n'


def main(argv):
    if len(argv) < 4:
        print(__doc__)
        return 2
    aliases = {}
    rest = argv[4:]
    while rest:
        flag = rest.pop(0)
        if flag == '--alias' and rest:
            new_name, _, old_name = rest.pop(0).partition('=')
            aliases[new_name] = old_name
    text = report(argv[1], argv[2], aliases)
    with open(argv[3], 'w') as f:
        f.write(text)
    print(f'wrote {argv[3]} ({len(text.splitlines())} lines)')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
