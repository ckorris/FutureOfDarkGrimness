"""Holo war-table menu mockup. Usage: holo_table.py topo|terrain out.png"""
import math, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

MODE, OUT = sys.argv[1], sys.argv[2]
W, H = 1920, 1080
SS = 2
TW, TD = 72.0, 48.0          # table, inches
HX, HY = TW / 2, TD / 2
RIM = 1.5                     # slab thickness

# ---------------- camera: look-at with lens shift, auto-fit to the region right of the panel ----------------
pitch, yaw, fov = math.radians(47), math.radians(-16), math.radians(40)
tanh = math.tan(fov / 2)
target = np.array([0.0, 1.0, 0.0])
REGION = (540, 1880, 50, 1040)

def basis(D):
    d = np.array([math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)])
    cam = target + D * d
    fwd = (target - cam) / np.linalg.norm(target - cam)
    right = np.cross(fwd, [0, 0, 1.0]); right /= np.linalg.norm(right)
    up = np.cross(right, fwd)
    return cam, fwd, right, up

corners3 = [(sx * HX, sy * HY, z) for sx in (-1, 1) for sy in (-1, 1) for z in (0, -RIM)]
ppx, ppy = W / 2, H / 2
def proj_with(c, p, pp):
    cam, fwd, right, up = c
    rel = np.asarray(p, float) - cam
    xc, yc, zc = rel @ right, rel @ up, rel @ fwd
    return (pp[0] + xc / zc / tanh * (H / 2), pp[1] - yc / zc / tanh * (H / 2))

D = 60.0
for _ in range(80):
    c = basis(D)
    pts = [proj_with(c, p, (0, 0)) for p in corners3]
    xs_, ys_ = [p[0] for p in pts], [p[1] for p in pts]
    if (max(xs_) - min(xs_)) <= REGION[1] - REGION[0] and (max(ys_) - min(ys_)) <= REGION[3] - REGION[2]:
        break
    D *= 1.03
CAM = basis(D)
ppx = (REGION[0] + REGION[1]) / 2 - (max(xs_) + min(xs_)) / 2
ppy = (REGION[2] + REGION[3]) / 2 - (max(ys_) + min(ys_)) / 2
cam, fwd, right, up = CAM

def project(p): return proj_with(CAM, p, (ppx, ppy))

def unproject_grid():
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float64)
    nx = (xs - ppx) / (H / 2) * tanh
    ny = (ppy - ys) / (H / 2) * tanh
    d = [fwd[i] + nx * right[i] + ny * up[i] for i in range(3)]
    t = -cam[2] / d[2]
    t = np.where(t > 0, t, np.nan)
    return xs, ys, cam[0] + t * d[0], cam[1] + t * d[1], t

xs, ys, wx, wy, tdist = unproject_grid()
on_table = (np.abs(wx) <= HX) & (np.abs(wy) <= HY)
on_table = np.nan_to_num(on_table).astype(bool)

def aa_lines(val, width):
    gy, gx = np.gradient(val)
    g = np.sqrt(gx * gx + gy * gy) + 1e-6
    d = np.abs((val + 0.5) % 1.0 - 0.5) / g
    return np.clip(1.0 - d / width, 0, 1)

# ---------------- noise ----------------
rng = np.random.default_rng(11)
perm = rng.permutation(256); vals = rng.random(256)
def hash2(ix, iy): return vals[perm[(perm[ix & 255] + iy) & 255]]
def vnoise(x, y):
    ix, iy = np.floor(x).astype(np.int64), np.floor(y).astype(np.int64)
    fx, fy = x - ix, y - iy
    ux, uy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b, c, d = hash2(ix, iy), hash2(ix + 1, iy), hash2(ix, iy + 1), hash2(ix + 1, iy + 1)
    return (a + (b - a) * ux) + ((c + (d - c) * ux) - (a + (b - a) * ux)) * uy
def fbm(x, y, oct=5):
    s, amp, f, n = 0, 1.0, 1.0, 0
    for _ in range(oct):
        s = s + amp * vnoise(x * f, y * f); n += amp; amp *= 0.5; f *= 2.03
    return s / n

# ---------------- scene: units ----------------
FRIEND, HOSTILE = (1.0, 0.30, 0.43), (0.24, 0.86, 0.35)
TRACER, WHITE, CYAN = (1.0, 0.97, 0.88), (0.9, 0.95, 1.0), (0.20, 0.72, 1.00)

if MODE == "topo":
    units = {
        "B-SIST 1":    dict(c=(-17, -15), col=FRIEND, n=3, r=0.6),
        "B-SIST 2": dict(c=(-6, -9), col=FRIEND, n=10, r=0.5),
        "B-SIST 3": dict(c=(3, -14), col=FRIEND, n=5, r=0.5),
        "B-SIST 4": dict(c=(18, -13), col=FRIEND, n=1, r=2.0),
        "ORK 1": dict(c=(4, 6), col=HOSTILE, n=8, r=0.5, dead=2),
        "ORK 2": dict(c=(13, 3), col=HOSTILE, n=5, r=0.5),
        "ORK 3": dict(c=(-16, 12), col=HOSTILE, n=3, r=0.8),
        "ORK 4": dict(c=(26, 15), col=HOSTILE, n=1, r=1.7),
    }
    trails = {"B-SIST 1": [(-23, -21), (-20, -18)], "B-SIST 4": [(13, -22), (16, -18)], "ORK 4": [(32, 20), (29, 18)],
              "ORK 3": [(-25, 19), (-20, 15)]}
    fire_lines = [("B-SIST 2", "ORK 1"), ("B-SIST 3", "ORK 1"), ("ORK 2", "B-SIST 3")]
    burning, measure_pair, pulse_c = "ORK 1", ("B-SIST 4", "ORK 4"), (4, 2)
    pieces = []
else:
    units = {
        "B-SIST 1":    dict(c=(-21, -16), col=FRIEND, n=3, r=0.6),
        "B-SIST 2": dict(c=(-9, -9), col=FRIEND, n=10, r=0.5),
        "B-SIST 3": dict(c=(4, -13), col=FRIEND, n=5, r=0.5),
        "B-SIST 4": dict(c=(31, -14), col=FRIEND, n=1, r=2.0),
        "ORK 1": dict(c=(-3, 10), col=HOSTILE, n=8, r=0.5, dead=2),
        "ORK 2": dict(c=(13, 5), col=HOSTILE, n=5, r=0.5),
        "ORK 3": dict(c=(-28, 16), col=HOSTILE, n=3, r=0.8),
        "ORK 4": dict(c=(30, 17), col=HOSTILE, n=1, r=1.7),
    }
    trails = {"B-SIST 1": [(-28, -21), (-24, -19)], "B-SIST 4": [(26, -22), (29, -18)], "ORK 4": [(34, 22), (32, 20)],
              "ORK 3": [(-34, 21), (-31, 18)]}
    fire_lines = [("B-SIST 2", "ORK 1"), ("B-SIST 3", "ORK 2"), ("ORK 2", "B-SIST 3")]
    burning, measure_pair, pulse_c = "ORK 1", ("B-SIST 4", "ORK 4"), (2, 0)
    # (kind, params). Footprints (2D polygons) are used for the LOS check.
    pieces = [
        ("ruin", dict(c=(-24, 2), yaw=15, legs=[((-5, 0), (5, 0)), ((-5, 0), (-5, 7))], h=5.5)),
        ("ruin", dict(c=(25, -2), yaw=-160, legs=[((-4, 0), (6, 0)), ((6, 0), (6, 6))], h=6.0)),
        ("ruin", dict(c=(5, 18), yaw=5, legs=[((-6, 0), (4, 0))], h=4.5)),
        ("box", dict(c=(-14, 15), size=(7, 3, 3), yaw=20)),
        ("box", dict(c=(-30, -12), size=(3, 6, 2.5), yaw=0)),
        ("box", dict(c=(17, -21), size=(6, 2.5, 2.5), yaw=-10)),
        ("box", dict(c=(34, 4), size=(2.5, 6, 3), yaw=0)),
        ("bunker", dict(c=(-10, -21), r=3.0, h=3.0)),
        ("forest", dict(c=(-18, -4), r=5.0, trees=[(-2, -1.5), (1.5, -2), (0, 2.2), (2.6, 1.2), (-2.6, 1.8)])),
        ("forest", dict(c=(20, 11), r=4.5, trees=[(-1.8, 0), (1.5, 1.5), (1.0, -2.0), (-0.5, 2.6)])),
        ("crater", dict(c=(6, -2), r=3.2)),
        ("barricade", dict(c=(2, -6), size=(6, 0.8, 1.2), yaw=-25)),
        ("barricade", dict(c=(-14, 8), size=(5, 0.8, 1.2), yaw=10)),
    ]

# ---------------- base image ----------------
img = np.zeros((H, W, 3)) + np.array([0.006, 0.010, 0.020])
# projector glow pooled under the table
cx_s, cy_s = project((0, 0, 0))
r2 = ((xs - cx_s) / 1100) ** 2 + ((ys - cy_s) / 700) ** 2
img += np.exp(-r2 * 2.5)[..., None] * np.array([0.010, 0.030, 0.055])

tab = on_table.astype(float)
img += tab[..., None] * np.array([0.012, 0.028, 0.050])

wxs, wys = np.nan_to_num(wx), np.nan_to_num(wy)
if MODE == "topo":
    h = fbm(wxs / 17.0 + 3.1, wys / 17.0 + 7.7, oct=4)
    # keep the relief inside the table: ease toward a flat level near the rim
    edge = np.clip(np.minimum(HX - np.abs(wxs), HY - np.abs(wys)) / 5.0, 0, 1)
    h = 0.45 + (h - 0.45) * (0.35 + 0.65 * edge)
    v = h * 20
    minor = aa_lines(v, 0.9)
    major = aa_lines(v, 1.7) * (np.round(v).astype(int) % 5 == 0)
    surf = np.maximum(minor * 0.55, major) * (0.55 + 0.75 * np.clip((h - 0.3) / 0.5, 0, 1))
    surf += np.maximum(aa_lines(wxs / 12.0, 0.7), aa_lines(wys / 12.0, 0.7)) * 0.10
else:
    surf = np.maximum(aa_lines(wxs / 6.0, 0.8), aa_lines(wys / 6.0, 0.8)) * 0.22
    surf = np.maximum(surf, np.maximum(aa_lines(wxs, 0.6), aa_lines(wys, 0.6)) * 0.06)

pr = np.sqrt((wxs - pulse_c[0]) ** 2 + (wys - pulse_c[1]) ** 2)
R = 17.0
pulse = np.exp(-((pr - R) / 1.4) ** 2) + 0.3 * np.exp(-np.clip(R - pr, 0, None) / 5.0) * (pr < R)
ring = np.exp(-((pr - R) / 0.3) ** 2) * 0.6
lines = surf * (1 + 2.4 * pulse) * tab
img += lines[..., None] * np.array(CYAN) + (ring * tab)[..., None] * np.array([0.5, 0.9, 1.0])

L = Image.fromarray((np.clip(lines, 0, 1) * 255).astype(np.uint8))
g1 = np.asarray(L.filter(ImageFilter.GaussianBlur(3)), float) / 255
g2 = np.asarray(L.filter(ImageFilter.GaussianBlur(12)), float) / 255
img += (g1 * 0.9 + g2 * 1.1)[..., None] * np.array(CYAN) * 0.6

# ---------------- overlay ----------------
ov = Image.new("RGB", (W * SS, H * SS), (0, 0, 0))
dr = ImageDraw.Draw(ov)
mono = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
monob = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"
f_lbl, f_meas, f_tick = ImageFont.truetype(mono, 14 * SS), ImageFont.truetype(monob, 19 * SS), ImageFont.truetype(mono, 11 * SS)

def S(p): return (p[0] * SS, p[1] * SS)
def C(c, k=1.0): return tuple(int(min(255, x * 255 * k)) for x in c)
def pulse_k(p2):
    d = math.hypot(p2[0] - pulse_c[0], p2[1] - pulse_c[1])
    return 1 + 1.3 * math.exp(-((d - R) / 3.0) ** 2)
def line3(a, b, col, k=1.0, w=2):
    dr.line([S(project(a)), S(project(b))], fill=C(col, k), width=max(1, int(w * SS)))
def poly3(pts, col, k):
    dr.polygon([S(project(p)) for p in pts], fill=C(col, k))
def dot(p, r, col, k=1.0):
    r *= SS; dr.ellipse([p[0] * SS - r, p[1] * SS - r, p[0] * SS + r, p[1] * SS + r], fill=C(col, k))
def ground_circle(c, r, col, k=1.0, w=2, dashed=False, z=0.0, n=64):
    pts = [(c[0] + r * math.cos(a), c[1] + r * math.sin(a), z) for a in np.linspace(0, 2 * math.pi, n + 1)]
    for i in range(n):
        if dashed and i % 2: continue
        line3(pts[i], pts[i + 1], col, k, w)

# table slab: top outline, bottom outline, corner posts, rim ticks
top = [(-HX, -HY, 0), (HX, -HY, 0), (HX, HY, 0), (-HX, HY, 0)]
bot = [(x, y, -RIM) for x, y, _ in top]
for i in range(4):
    line3(top[i], top[(i + 1) % 4], (0.55, 0.9, 1.0), 1.0, 3)
    line3(bot[i], bot[(i + 1) % 4], CYAN, 0.45, 2)
    line3(top[i], bot[i], CYAN, 0.6, 2)
for x in range(-36, 37, 6):
    big = x % 12 == 0
    line3((x, -HY, 0), (x, -HY - (1.4 if big else 0.7), 0), CYAN, 0.8 if big else 0.5, 2)
    line3((x, HY, 0), (x, HY + (1.4 if big else 0.7), 0), CYAN, 0.5, 1)
for y in range(-24, 25, 6):
    big = y % 12 == 0
    line3((-HX, y, 0), (-HX - (1.4 if big else 0.7), y, 0), CYAN, 0.8 if big else 0.5, 2)
    line3((HX, y, 0), (HX + (1.4 if big else 0.7), y, 0), CYAN, 0.8 if big else 0.5, 2)
for x in range(-24, 37, 12):
    p = project((x, -HY - 2.6, 0))
    s = f'{x + 36}"'
    dr.text(S((p[0] - 10, p[1] - 6)), s, font=f_tick, fill=C(CYAN, 0.75))
for y in range(-12, 25, 12):
    p = project((HX + 3.0, y, 0))
    dr.text(S((p[0] - 6, p[1] - 6)), f'{y + 24}"', font=f_tick, fill=C(CYAN, 0.75))
# corner brackets on the table top
for sx in (-1, 1):
    for sy in (-1, 1):
        cx, cy = sx * HX, sy * HY
        line3((cx, cy, 0.02), (cx - sx * 4, cy, 0.02), (0.8, 0.97, 1.0), 1.0, 4)
        line3((cx, cy, 0.02), (cx, cy - sy * 4, 0.02), (0.8, 0.97, 1.0), 1.0, 4)

# ---------------- terrain pieces ----------------
def rot(p, yaw_deg, c):
    a = math.radians(yaw_deg)
    return (c[0] + p[0] * math.cos(a) - p[1] * math.sin(a), c[1] + p[0] * math.sin(a) + p[1] * math.cos(a))

footprints = []   # 2D polygons that block lines of fire
fills, wires = [], []

def add_box(c, size, yaw_deg, col=CYAN, k=1.0):
    sx, sy, sz = size[0] / 2, size[1] / 2, size[2]
    fp = [rot(p, yaw_deg, c) for p in [(-sx, -sy), (sx, -sy), (sx, sy), (-sx, sy)]]
    footprints.append(fp)
    k *= pulse_k(c)
    fills.append(([(x, y, sz) for x, y in fp], col, 0.10 * k))
    for i in range(4):
        a, b = fp[i], fp[(i + 1) % 4]
        wires[len(wires):] = [((a[0], a[1], 0), (b[0], b[1], 0), col, 0.55 * k, 1.5),
                  ((a[0], a[1], sz), (b[0], b[1], sz), col, 1.0 * k, 2),
                  ((a[0], a[1], 0), (a[0], a[1], sz), col, 0.8 * k, 1.5)]
    return fp

def add_ruin(c, yaw_deg, legs, h, col=CYAN):
    rr = np.random.default_rng(int(abs(c[0] * 7 + c[1] * 13)))
    k = pulse_k(c)
    t = 0.35
    for (a, b) in legs:
        a2, b2 = rot(a, yaw_deg, c), rot(b, yaw_deg, c)
        L = math.hypot(b2[0] - a2[0], b2[1] - a2[1])
        ux, uy = (b2[0] - a2[0]) / L, (b2[1] - a2[1]) / L
        nx, ny = -uy * t, ux * t
        footprints.append([(a2[0] + nx, a2[1] + ny), (b2[0] + nx, b2[1] + ny), (b2[0] - nx, b2[1] - ny), (a2[0] - nx, a2[1] - ny)])
        n = max(4, int(L / 1.2))
        ss = np.linspace(0, 1, n + 1)
        hs = [h * (0.45 + 0.55 * rr.random()) if 0 < i < n else h * rr.uniform(0.6, 1.0) for i in range(n + 1)]
        for i in range(n + 1):
            px, py = a2[0] + (b2[0] - a2[0]) * ss[i], a2[1] + (b2[1] - a2[1]) * ss[i]
            if i < n:
                qx, qy = a2[0] + (b2[0] - a2[0]) * ss[i + 1], a2[1] + (b2[1] - a2[1]) * ss[i + 1]
                for side in (1, -1):
                    o = (nx * side, ny * side)
                    wires.append(((px + o[0], py + o[1], hs[i]), (qx + o[0], qy + o[1], hs[i + 1]), col, 1.0 * k, 2))
                    wires.append(((px + o[0], py + o[1], 0), (qx + o[0], qy + o[1], 0), col, 0.5 * k, 1.5))
                fills.append(([(px, py, 0), (qx, qy, 0), (qx, qy, hs[i + 1]), (px, py, hs[i])], col, 0.07 * k))
            if i in (0, n) or i % 2 == 0:
                wires.append(((px, py, 0), (px, py, hs[i]), col, 0.7 * k, 1.5))
            # window holes
            if 0 < i < n - 1 and i % 2 == 1 and hs[i] > 2.6:
                m = ((px + qx) / 2, (py + qy) / 2)
                w0 = (m[0] - ux * 0.35, m[1] - uy * 0.35); w1 = (m[0] + ux * 0.35, m[1] + uy * 0.35)
                wires[len(wires):] = [((w0[0], w0[1], 1.2), (w1[0], w1[1], 1.2), col, 0.6 * k, 1.2),
                          ((w0[0], w0[1], 2.2), (w1[0], w1[1], 2.2), col, 0.6 * k, 1.2),
                          ((w0[0], w0[1], 1.2), (w0[0], w0[1], 2.2), col, 0.6 * k, 1.2),
                          ((w1[0], w1[1], 1.2), (w1[0], w1[1], 2.2), col, 0.6 * k, 1.2)]

def add_bunker(c, r, h, col=CYAN):
    k = pulse_k(c)
    ring_ = [(c[0] + r * math.cos(a), c[1] + r * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 9)[:-1] + math.pi / 8]
    footprints.append(ring_)
    fills.append(([(x, y, h) for x, y in ring_], col, 0.10 * k))
    for i in range(8):
        a, b = ring_[i], ring_[(i + 1) % 8]
        wires[len(wires):] = [((a[0], a[1], 0), (b[0], b[1], 0), col, 0.5 * k, 1.5), ((a[0], a[1], h), (b[0], b[1], h), col, 1.0 * k, 2),
                  ((a[0], a[1], 0), (a[0], a[1], h), col, 0.75 * k, 1.5)]
    # firing slit
    a, b = ring_[1], ring_[2]
    for z in (h * 0.6, h * 0.75):
        wires.append(((a[0] * 0.8 + b[0] * 0.2, a[1] * 0.8 + b[1] * 0.2, z), (a[0] * 0.2 + b[0] * 0.8, a[1] * 0.2 + b[1] * 0.8, z), col, 0.9 * k, 1.2))

def add_forest(c, r, trees, col=(0.30, 0.85, 0.95)):
    k = pulse_k(c)
    n = 14
    rr = np.random.default_rng(int(abs(c[0] * 3 + c[1] * 5)))
    fp = [(c[0] + r * (0.85 + 0.25 * rr.random()) * math.cos(a), c[1] + r * (0.85 + 0.25 * rr.random()) * math.sin(a))
          for a in np.linspace(0, 2 * math.pi, n + 1)[:-1]]
    footprints.append(fp)
    for i in range(n):
        if i % 2 == 0:
            a, b = fp[i], fp[(i + 1) % n]
            wires.append(((a[0], a[1], 0), (b[0], b[1], 0), col, 0.8 * k, 2))
    for (tx, ty) in trees:
        p = (c[0] + tx, c[1] + ty)
        hgt = rr.uniform(5.0, 6.5)
        wires.append(((p[0], p[1], 0), (p[0], p[1], 1.4), col, 0.7 * k, 1.5))
        for lvl, (z0, rad) in enumerate([(1.4, 1.5), (2.8, 1.15), (4.0, 0.8)]):
            ringp = [(p[0] + rad * math.cos(a), p[1] + rad * math.sin(a), z0) for a in np.linspace(0, 2 * math.pi, 13)]
            for i in range(12):
                wires.append((ringp[i], ringp[i + 1], col, 0.75 * k, 1.3))
            for i in range(0, 12, 3):
                wires.append((ringp[i], (p[0], p[1], min(hgt, z0 + 2.2)), col, 0.85 * k, 1.3))

def add_crater(c, r, col=CYAN):
    k = pulse_k(c)
    rr = np.random.default_rng(5)
    for rad, kk, dashed in [(r, 0.9, False), (r * 0.62, 0.55, True), (r * 0.3, 0.4, True)]:
        pts = [(c[0] + rad * (0.9 + 0.2 * rr.random()) * math.cos(a), c[1] + rad * (0.9 + 0.2 * rr.random()) * math.sin(a), 0.03)
               for a in np.linspace(0, 2 * math.pi, 25)]
        pts[-1] = pts[0]
        for i in range(24):
            if dashed and i % 2: continue
            wires.append((pts[i], pts[i + 1], col, kk * k, 1.5))

for kind, p in pieces:
    if kind == "box": add_box(p["c"], p["size"], p["yaw"])
    elif kind == "barricade": add_box(p["c"], p["size"], p["yaw"], k=0.9)
    elif kind == "ruin": add_ruin(p["c"], p["yaw"], p["legs"], p["h"])
    elif kind == "bunker": add_bunker(p["c"], p["r"], p["h"])
    elif kind == "forest": add_forest(p["c"], p["r"], p["trees"])
    elif kind == "crater": add_crater(p["c"], p["r"])

# ---------------- line-of-fire check: no shot or measure may cross a footprint ----------------
def seg_hits_poly(a, b, poly, pad=0.4):
    def seg_int(p, q, r, s):
        def cr(o, a_, b_): return (a_[0] - o[0]) * (b_[1] - o[1]) - (a_[1] - o[1]) * (b_[0] - o[0])
        d1, d2, d3, d4 = cr(r, s, p), cr(r, s, q), cr(p, q, r), cr(p, q, s)
        return (d1 > 0) != (d2 > 0) and (d3 > 0) != (d4 > 0)
    # pad the polygon outward from its centroid
    cx = sum(x for x, _ in poly) / len(poly); cy = sum(y for _, y in poly) / len(poly)
    pp = []
    for x, y in poly:
        dx, dy = x - cx, y - cy; L = math.hypot(dx, dy) or 1
        pp.append((x + dx / L * pad, y + dy / L * pad))
    for i in range(len(pp)):
        if seg_int(a, b, pp[i], pp[(i + 1) % len(pp)]): return True
    def inside(pt):
        c = False
        for i in range(len(pp)):
            (x1, y1), (x2, y2) = pp[i], pp[(i + 1) % len(pp)]
            if (y1 > pt[1]) != (y2 > pt[1]) and pt[0] < (x2 - x1) * (pt[1] - y1) / (y2 - y1) + x1: c = not c
        return c
    return inside(a) or inside(b)

def formation(name, u):
    n, r, c = u["n"], u["r"], u["c"]
    if n == 1: return [c]
    face = 1 if u["col"] == FRIEND else -1
    rr = np.random.default_rng(sum(map(ord, name)))
    sp = 2 * r + 0.55
    cols = min(n, 5)
    pts = []
    for i in range(n):
        row, col = divmod(i, cols)
        cnt = min(cols, n - row * cols)
        x = (col - (cnt - 1) / 2) * sp + (sp * 0.5 if row % 2 else 0)
        y = -row * sp * 0.9 * face
        pts.append((x + rr.uniform(-0.2, 0.2), y + rr.uniform(-0.2, 0.2)))
    mx = sum(p[0] for p in pts) / n; my = sum(p[1] for p in pts) / n
    return [(c[0] + x - mx, c[1] + y - my) for x, y in pts]

for name, u in units.items():
    u["m"] = formation(name, u)
    u["alive"] = u["m"][u.get("dead", 0):]
    u["dead_m"] = u["m"][:u.get("dead", 0)]

def nearest(models, to, k):
    return sorted(models, key=lambda p: math.hypot(p[0] - to[0], p[1] - to[1]))[:k]

def shot_segments(s_name, t_name):
    su, tu = units[s_name], units[t_name]
    S_ = nearest(su["alive"], tu["c"], 2); T_ = nearest(tu["alive"], su["c"], 2)
    return [(S_[i % len(S_)], T_[i % len(T_)]) for i in range(2)]

def measure_segment(a_name, b_name):
    au, bu = units[a_name], units[b_name]
    best = min(((p, q) for p in au["alive"] for q in bu["alive"]), key=lambda pq: math.hypot(pq[1][0] - pq[0][0], pq[1][1] - pq[0][1]))
    (p, q) = best
    d = math.hypot(q[0] - p[0], q[1] - p[1]); ux, uy = (q[0] - p[0]) / d, (q[1] - p[1]) / d
    return (p[0] + ux * au["r"], p[1] + uy * au["r"]), (q[0] - ux * bu["r"], q[1] - uy * bu["r"]), d - au["r"] - bu["r"]

checks = [((s_, t_), seg) for s_, t_ in fire_lines for seg in shot_segments(s_, t_)]
ma, mb, _ = measure_segment(*measure_pair)
checks.append((measure_pair, (ma, mb)))
blocked = []
for label, (a, b) in checks:
    if any(seg_hits_poly(a, b, fp) for fp in footprints): blocked.append(label)
for name, u in units.items():
    for p in u["m"]:
        if any(seg_hits_poly(p, p, fp, pad=u["r"] + 0.3) for fp in footprints): blocked.append((name, "model in terrain"))
print("checked", len(checks), "lines of fire/measure and", sum(len(u["m"]) for u in units.values()), "models against", len(footprints), "footprints")
if blocked:
    print("BLOCKED:", blocked); sys.exit(2)

for pts, col, k in fills: poly3(pts, col, k)
for a, b, col, k, w in wires: line3(a, b, col, k, w)

# ---------------- units, trails, fire ----------------
def draw_unit(name, u):
    col, r = u["col"], u["r"]
    face = 1 if col == FRIEND else -1
    for p in u["dead_m"]:
        ground_circle(p, r, col, 0.3, w=1.5, dashed=True, n=24)
    for p in u["alive"]:
        ground_circle(p, r, col, 0.9, w=2, n=32 if r < 1 else 64)
        if r >= 1.2:   # vehicle: inner ring + facing tick
            ground_circle(p, r * 0.6, col, 0.5, w=1.5, dashed=True)
            line3((p[0], p[1] + face * r * 0.6, 0), (p[0], p[1] + face * r * 1.35, 0), col, 1.0, 3)
    lead = nearest(u["alive"], u["c"], 1)[0]
    hgt = 5.5 if r >= 1.2 else 4.0
    base = project((lead[0], lead[1], 0)); topp = project((lead[0], lead[1], hgt))
    dr.line([S(base), S(topp)], fill=C(col, 0.9), width=2 * SS)
    fw = 20 * face
    dr.polygon([S(topp), S((topp[0] + fw, topp[1] + 6)), S((topp[0], topp[1] + 12))], fill=C(col, 0.55), outline=C(col))
    tag = f"{name} x{len(u['alive'])}" if u["n"] > 1 else name
    tw = dr.textlength(tag, font=f_lbl) / SS
    dr.text(S((topp[0] + (6 if face > 0 else -6 - tw), topp[1] - 17)), tag, font=f_lbl, fill=C(col, 0.85))

for name, path in trails.items():
    pts = path + [units[name]["c"]]
    col = units[name]["col"]
    for i in range(len(pts) - 1):
        for s in np.linspace(0, 1, 5, endpoint=False):
            q = project((pts[i][0] + (pts[i + 1][0] - pts[i][0]) * s, pts[i][1] + (pts[i + 1][1] - pts[i][1]) * s, 0))
            dot(q, 2.0, col, 0.45)

def tracers(src, dst, phases):
    a = project((src[0], src[1], 0.9)); b = project((dst[0], dst[1], 0.8))
    dr.line([S(a), S(b)], fill=C(TRACER, 0.18), width=SS)
    for ph in phases:
        dr.line([S((a[0] + (b[0] - a[0]) * ph, a[1] + (b[1] - a[1]) * ph)),
                 S((a[0] + (b[0] - a[0]) * (ph + 0.08), a[1] + (b[1] - a[1]) * (ph + 0.08)))], fill=C(TRACER), width=3 * SS)
    dot(a, 3.5, TRACER, 0.9)   # muzzle flash

def fire(wp):
    b = project((wp[0], wp[1], 0))
    rr = np.random.default_rng(3)
    for k, (col, hgt, wid) in enumerate([((1.0, 0.25, 0.08), 50, 30), ((1.0, 0.55, 0.12), 36, 21), ((1.0, 0.9, 0.5), 20, 10)]):
        for j in range(3):
            ox = (j - 1) * wid * 0.55 + rr.uniform(-3, 3)
            hh = hgt * rr.uniform(0.75, 1.1)
            poly = [(b[0] + ox - wid * 0.5, b[1]), (b[0] + ox - wid * 0.2, b[1] - hh * 0.55),
                    (b[0] + ox + rr.uniform(-5, 5), b[1] - hh), (b[0] + ox + wid * 0.25, b[1] - hh * 0.5), (b[0] + ox + wid * 0.5, b[1])]
            dr.polygon([S(p) for p in poly], fill=C(col, 0.75 if k == 0 else 0.9))
    for _ in range(12):
        dot((b[0] + rr.uniform(-30, 30), b[1] - rr.uniform(50, 115)), rr.uniform(1.4, 2.6), (1.0, 0.6, 0.2), 0.8)
    for rad, k in [(2.0, 0.6), (3.0, 0.35)]:
        ground_circle(wp, rad, (1.0, 0.45, 0.15), k)

for s_, t_ in fire_lines:
    for i, (a, b) in enumerate(shot_segments(s_, t_)):
        tracers(a, b, [0.22 + 0.1 * i, 0.6 + 0.08 * i])
bu = units[burning]
fire_at = bu["dead_m"] or bu["alive"][:1]
fire((sum(p[0] for p in fire_at) / len(fire_at), sum(p[1] for p in fire_at) / len(fire_at)))

for name, u in units.items():
    draw_unit(name, u)

def measure(pa3, pb3, inches):
    pa, pb = project((pa3[0], pa3[1], 0.05)), project((pb3[0], pb3[1], 0.05))
    n = 30
    for i in range(0, n, 2):
        s0, s1 = i / n, (i + 1) / n
        dr.line([S((pa[0] + (pb[0] - pa[0]) * s0, pa[1] + (pb[1] - pa[1]) * s0)),
                 S((pa[0] + (pb[0] - pa[0]) * s1, pa[1] + (pb[1] - pa[1]) * s1))], fill=C(WHITE), width=2 * SS)
    for p in (pa, pb): dot(p, 3, WHITE)
    text = f'{inches:.1f}"'
    m = ((pa[0] + pb[0]) / 2, (pa[1] + pb[1]) / 2)
    tw = dr.textlength(text, font=f_meas) / SS
    dr.rectangle([S((m[0] + 10, m[1] - 14)), S((m[0] + 26 + tw, m[1] + 14))], fill=(0, 0, 0), outline=C(WHITE, 0.7), width=SS)
    dr.text(S((m[0] + 18, m[1] - 12)), text, font=f_meas, fill=C((0.95, 1.0, 1.0)))
measure(*measure_segment(*measure_pair))

ov = ov.resize((W, H), Image.LANCZOS)
o = np.asarray(ov, float) / 255
og1 = np.asarray(ov.filter(ImageFilter.GaussianBlur(4)), float) / 255
og2 = np.asarray(ov.filter(ImageFilter.GaussianBlur(16)), float) / 255
img += o + og1 * 0.8 + og2 * 0.8

img *= (0.9 + 0.1 * (np.arange(H) % 3 != 0))[:, None, None]
vx, vy = xs / W - 0.5, ys / H - 0.5
img *= np.clip(1.15 - 0.9 * (vx * vx + vy * vy), 0, 1)[..., None]
img *= np.clip(0.35 + 0.65 * (xs - 380) / 520, 0.35, 1.0)[..., None]
base = Image.fromarray((np.clip(img, 0, 1) ** (1 / 1.1) * 255).astype(np.uint8)).convert("RGBA")

# ---------------- UI ----------------
ui = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
d2 = ImageDraw.Draw(ui)
sans_b, sans = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
CY = (90, 200, 255)
f_title = ImageFont.truetype(sans_b, 58 * SS)
d2.text(S((66, 44)), "FUTURE of", font=f_title, fill=(225, 245, 255, 255))
d2.text(S((66, 112)), "DARK GRIMNESS", font=f_title, fill=(225, 245, 255, 255))
d2.line([S((70, 196)), S((610, 196))], fill=CY + (200,), width=2 * SS)
px0, py0, pw, pb = 70, 250, 380, 740
d2.rectangle([S((px0 - 20, py0 - 20)), S((px0 + pw + 20, pb))], fill=(4, 12, 22, 150), outline=CY + (90,), width=SS)
for (cx, cy, sx, sy) in [(px0 - 20, py0 - 20, 1, 1), (px0 + pw + 20, py0 - 20, -1, 1), (px0 - 20, pb, 1, -1), (px0 + pw + 20, pb, -1, -1)]:
    d2.line([S((cx, cy)), S((cx + 24 * sx, cy))], fill=CY + (255,), width=3 * SS)
    d2.line([S((cx, cy)), S((cx, cy + 24 * sy))], fill=CY + (255,), width=3 * SS)
def button(y, hgt, label, size, hot=False, x0=None, w=None, ghost=False):
    x0 = px0 if x0 is None else x0; w = pw if w is None else w
    if ghost:   # tertiary: outline only, dimmer, regular weight, centred
        d2.rectangle([S((x0, y)), S((x0 + w, y + hgt))], fill=(10, 40, 70, 40), outline=CY + (110,), width=SS)
        f = ImageFont.truetype(sans, size * SS)
        tw = d2.textlength(label, font=f) / SS
        d2.text(S((x0 + (w - tw) / 2, y + (hgt - size * 1.15) / 2)), label, font=f, fill=(175, 210, 235, 235))
        return
    d2.rectangle([S((x0, y)), S((x0 + w, y + hgt))], fill=(40, 140, 210, 150) if hot else (10, 40, 70, 110),
                 outline=CY + (230 if hot else 150,), width=(2 if hot else 1) * SS)
    f = ImageFont.truetype(sans_b, size * SS)
    d2.text(S((x0 + 22, y + (hgt - size * 1.15) / 2)), label, font=f, fill=(235, 248, 255, 255))
    if hot: d2.text(S((px0 + pw - 34, y + (hgt - size * 1.15) / 2)), ">", font=f, fill=(235, 248, 255, 255))
y = py0
button(y, 78, "HOST/SOLO", 34, hot=True); y += 92
button(y, 78, "JOIN", 34); y += 112
for lbl in ["Army Forge", "Combat Calculator", "Load Game"]:
    button(y, 54, lbl, 24); y += 66
y += 22
half = (pw - 12) / 2
button(y, 44, "Options", 20, ghost=True, x0=px0, w=half)
button(y, 44, "Quit", 20, ghost=True, x0=px0 + half + 12, w=half)
disc = "This game is a fan-made project and is not an official product of OnePageRules."
fd = ImageFont.truetype(sans, 16 * SS)
tw = d2.textlength(disc, font=fd) / SS
d2.text(S(((W - tw) / 2, H - 36)), disc, font=fd, fill=(150, 160, 170, 220))
ui = ui.resize((W, H), Image.LANCZOS)
Image.alpha_composite(base, ui).convert("RGB").save(OUT)
print("ok", OUT, "camera distance", round(D, 1))
