"""Sanity check for the menu background's army table (#448): every pairing the clash groups ALLOW
must be clearly distinguishable (CIE76 delta-E), and every army must keep plenty of opponents.
Run: python3 -I docs/menu-background/army_pairings_check.py"""
import itertools, math

ARMIES = {  # abbreviation -> (army, colour)
    "ALIEN": ("Alien Hives", "#9C6BFF"),       "BAT-BR": ("Battle Brothers", "#4F6BFF"),
    "B-SIST": ("Blessed Sisters", "#FF4D6D"),  "C-BR": ("Custodian Brothers", "#F2E4BC"),
    "DAO": ("DAO Union", "#FF9F40"),           "D-ELF": ("Dark Elf Raiders", "#E04DFF"),
    "D-GUILD": ("Dwarf Guilds", "#D09050"),    "ELF-J": ("Elven Jesters", "#FF85C8"),
    "E-DYN": ("Eternal Dynasty", "#FFEB3B"),   "GOB": ("Goblin Reclaimers", "#A6F58C"),
    "HAVOC": ("Havoc Brothers", "#E8352E"),    "H-ELF": ("High Elf Fleets", "#8EC9FF"),
    "HDF": ("Human Defense Force", "#8CC63F"), "H-INQ": ("Human Inquisition", "#E6EAF2"),
    "INFECT": ("Infected Colonies", "#D4E157"),"JAK": ("Jackals", "#D8A878"),
    "M-CULT": ("Machine Cults", "#E0612A"),    "ORK": ("Orc Marauders", "#3DDC5A"),
    "RAT": ("Ratmen Clans", "#FFB000"),        "REB-G": ("Rebel Guerrillas", "#B5A66A"),
    "ROBOT": ("Robot Legions", "#8C4DFF"),     "S-STAR": ("Saurian Starhost", "#2EE6A6"),
    "S-CULT": ("Soul-Snatcher Cults", "#C58CFF"), "TITAN": ("Titan Lords", "#FFD447"),
    "DEMON": ("Wormhole Daemons", "#FF2E88"),
}

# Hard-coded: two armies sharing any group are never put on the table together. Groups overlap on purpose
# (a colour can sit between two families). A mirror match (army vs itself) is always excluded.
CLASH_GROUPS = [
    ("reds",            ["HAVOC", "B-SIST", "DEMON", "M-CULT"]),
    ("pinks/magentas",  ["DEMON", "ELF-J", "D-ELF", "B-SIST"]),
    ("purples/blues",   ["ALIEN", "ROBOT", "S-CULT", "BAT-BR"]),
    ("violets",         ["D-ELF", "S-CULT", "ALIEN", "ELF-J", "ROBOT"]),
    ("pale/whites",     ["H-INQ", "C-BR", "H-ELF"]),
    ("light blues",     ["H-ELF", "BAT-BR"]),
    ("yellows",         ["E-DYN", "TITAN", "INFECT", "RAT"]),
    ("oranges",         ["DAO", "RAT", "M-CULT", "D-GUILD", "JAK"]),
    ("golds",           ["TITAN", "DAO", "RAT", "D-GUILD"]),
    ("tans",            ["D-GUILD", "JAK", "REB-G", "C-BR"]),
    ("greens",          ["ORK", "HDF", "GOB", "INFECT", "S-STAR"]),
    ("drab greens",     ["HDF", "REB-G", "INFECT"]),
]

def lab(hx):
    rgb = [int(hx[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    rgb = [((c + 0.055) / 1.055) ** 2.4 if c > 0.04045 else c / 12.92 for c in rgb]
    x = (0.4124 * rgb[0] + 0.3576 * rgb[1] + 0.1805 * rgb[2]) / 0.95047
    y = 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2]
    z = (0.0193 * rgb[0] + 0.1192 * rgb[1] + 0.9505 * rgb[2]) / 1.08883
    f = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    return (116 * f(y) - 16, 500 * (f(x) - f(y)), 200 * (f(y) - f(z)))

def de(a, b):
    return math.dist(lab(ARMIES[a][1]), lab(ARMIES[b][1]))

for _, g in CLASH_GROUPS:
    for a in g: assert a in ARMIES, a
banned = {frozenset(p) for _, g in CLASH_GROUPS for p in itertools.combinations(g, 2)}
allowed = [p for p in itertools.combinations(ARMIES, 2) if frozenset(p) not in banned]
worst = sorted(allowed, key=lambda p: de(*p))[:6]
print(f"{len(ARMIES)} armies, {len(banned)} banned pairs, {len(allowed)} allowed pairs")
print("closest ALLOWED pairs (delta-E):", ", ".join(f"{a}/{b} {de(a, b):.0f}" for a, b in worst))
opp = {a: sum(1 for p in allowed if a in p) for a in ARMIES}
print("fewest opponents:", sorted(opp.items(), key=lambda kv: kv[1])[:4])
MIN_DE = 35
bad = [p for p in allowed if de(*p) < MIN_DE]
assert not bad, f"allowed pairs too similar: {bad}"
assert min(opp.values()) >= 12, "an army has too few opponents"
print(f"OK: every allowed pair has delta-E >= {MIN_DE}")
