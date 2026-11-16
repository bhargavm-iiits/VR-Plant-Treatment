"""Generate the 'simple but effective' Treatment Lab concept boards.

Each board is a hand-built SVG illustration rendered to a 1920x1080 PNG with
headless Chrome/Edge. Run:  python -I generate_concepts.py
"""
import math
import pathlib
import shutil
import subprocess
import tempfile
from html import escape

from PIL import Image, ImageDraw, ImageFont

OUT = pathlib.Path(__file__).resolve().parent
W, H, SCENE_H = 1920, 1080, 940

CYAN = "#2fd3e6"
CHAR = "#262b31"
CHAR2 = "#353c44"
TXT = "#eef3f5"
SUB = "#a9b6bd"
DARK = "#2a2f35"
GOOD = "#5cc46a"
WARN = "#f0b43c"
GLOVE = "#9aa1a8"
GLOVE_D = "#6c737a"

_n = [0]


def uid(p):
    _n[0] += 1
    return f"{p}{_n[0]}"


def mix(c1, c2, t):
    a = [int(c1[i:i + 2], 16) for i in (1, 3, 5)]
    b = [int(c2[i:i + 2], 16) for i in (1, 3, 5)]
    return "#" + "".join(f"{round(x + (y - x) * t):02x}" for x, y in zip(a, b))


def T(x, y, txt, size=24, fill=TXT, weight=400, anchor="start"):
    return (f'<text x="{x:.0f}" y="{y:.0f}" font-size="{size:.0f}" fill="{fill}" '
            f'font-weight="{weight}" text-anchor="{anchor}">{escape(txt)}</text>')


# ---------------------------------------------------------------- scenery

def distant_plant(x, y, s=1.0):
    g = [f'<g transform="translate({x},{y}) scale({s})">',
         '<path d="M-20 0 L20 0 L16 -26 L-16 -26Z" fill="#a3abb0"/>']
    for dx, dy, r in [(-14, -46, 15), (13, -52, 16), (0, -70, 17), (-3, -36, 13), (18, -34, 11)]:
        g.append(f'<circle cx="{dx}" cy="{dy}" r="{r}" fill="#79ab5e"/>')
    for dx, dy in [(9, -42), (-11, -58), (4, -30)]:
        g.append(f'<circle cx="{dx}" cy="{dy}" r="4.5" fill="#d9573f"/>')
    g.append("</g>")
    return "".join(g)


def background(floor_y=610):
    s = [f'<rect width="{W}" height="{SCENE_H}" fill="url(#sky)"/>',
         '<path d="M0 470 C 300 400, 520 440, 760 420 S 1300 380, 1560 430 S 1840 410, 1920 430 L1920 560 L0 560Z" fill="#c3dbb3"/>',
         '<path d="M0 505 C 400 470, 700 500, 1000 480 S 1600 470, 1920 500 L1920 560 L0 560Z" fill="#a6c996"/>',
         f'<rect x="0" y="520" width="{W}" height="{floor_y - 520}" fill="#e4e9e9"/>']
    for x in range(0, W + 1, 240):
        s.append(f'<rect x="{x - 7}" y="0" width="14" height="520" fill="#fbfcfc"/>'
                 f'<rect x="{x + 5}" y="0" width="3" height="520" fill="#d6dde0"/>')
    s.append(f'<rect x="0" y="66" width="{W}" height="12" fill="#fbfcfc"/>')
    s.append(f'<rect x="0" y="512" width="{W}" height="10" fill="#ffffff"/>')
    for i, x in enumerate(range(120, W, 330)):
        s.append(distant_plant(x, 512, 0.8 + 0.2 * (i % 2)))
    s.append(f'<rect x="0" y="{floor_y}" width="{W}" height="{SCENE_H - floor_y}" fill="url(#floor)"/>')
    vx, vy = 960, 300
    t = (floor_y - vy) / (SCENE_H - vy)
    for k in range(-16, 17):
        xb = 960 + k * 170
        xt = vx + (xb - vx) * t
        s.append(f'<line x1="{xt:.0f}" y1="{floor_y}" x2="{xb}" y2="{SCENE_H}" stroke="#d3dadc" stroke-width="2"/>')
    y, step = floor_y, 16.0
    while y < SCENE_H:
        s.append(f'<line x1="0" y1="{y:.0f}" x2="{W}" y2="{y:.0f}" stroke="#d3dadc" stroke-width="2"/>')
        y += step
        step *= 1.32
    s.append(f'<rect x="0" y="{floor_y}" width="{W}" height="4" fill="#c4cdd0"/>')
    return "".join(s)


# ---------------------------------------------------------------- leaf

DEFAULT_LES = [(0.45, 0.30, 1.0), (-0.5, 0.42, 1.15), (0.35, 0.58, 0.9), (-0.38, 0.66, 0.8),
               (0.62, 0.46, 0.7), (-0.55, 0.24, 0.85), (0.12, 0.80, 0.6), (-0.12, 0.50, 0.55)]


def hw_at(u, wd):
    u = min(max(u, 0.0), 0.999)
    return wd / 2 * (math.sin(math.pi * u) ** 0.75) * (1 - 0.3 * u)


def leaf(cx, cy, L, wd, health=0.0, rot=0, lesions=DEFAULT_LES, rings=(), glow=False, stem=0, dashed=()):
    gid, cid, hid = uid("lg"), uid("lc"), uid("lh")
    right = []
    for i in range(73):
        u = i / 72
        h = hw_at(u, wd)
        if 0.06 < u < 0.94:
            h *= 1 + 0.075 * ((u * 14) % 1)
        right.append((h, L / 2 - u * L))
    pts = right + [(-x, y) for x, y in reversed(right)]
    d = "M" + " L".join(f"{x:.1f} {y:.1f}" for x, y in pts) + "Z"
    c_in = mix("#c6c752", "#7cbc55", health)
    c_out = mix("#a8963a", "#3f8b36", health)
    edge = mix("#7a5a26", "#2f6b2b", health)
    vein = mix("#e2dc86", "#a8db86", health)
    s = [f'<defs><radialGradient id="{gid}" cx="45%" cy="45%" r="65%"><stop offset="0" stop-color="{c_in}"/>'
         f'<stop offset="1" stop-color="{c_out}"/></radialGradient><clipPath id="{cid}"><path d="{d}"/></clipPath>'
         f'<radialGradient id="{hid}"><stop offset="0.35" stop-color="#ead83e" stop-opacity="{0.85 * (1 - health):.2f}"/>'
         f'<stop offset="1" stop-color="#ead83e" stop-opacity="0"/></radialGradient></defs>',
         f'<g transform="translate({cx},{cy}) rotate({rot})">']
    if stem:
        s.append(f'<line x1="0" y1="{L / 2 - 6:.0f}" x2="0" y2="{L / 2 + stem:.0f}" stroke="#4d6b2c" '
                 f'stroke-width="{max(4, wd * 0.022):.0f}" stroke-linecap="round"/>')
    if glow:
        s.append(f'<path d="{d}" fill="none" stroke="{CYAN}" stroke-width="12" opacity="0.6" filter="url(#glow)"/>')
    s.append(f'<path d="{d}" fill="url(#{gid})" stroke="{edge}" stroke-width="3" stroke-linejoin="round"/>')
    s.append(f'<g clip-path="url(#{cid})">')
    s.append(f'<line x1="0" y1="{L / 2:.0f}" x2="0" y2="{-L / 2 + L * 0.04:.1f}" stroke="{vein}" '
             f'stroke-width="{wd * 0.014:.1f}" stroke-linecap="round" opacity="0.9"/>')
    u = 0.12
    while u < 0.86:
        y = L / 2 - u * L
        h2 = hw_at(u + 0.13, wd) * 0.85
        for sg in (1, -1):
            s.append(f'<path d="M0 {y:.1f} Q {sg * h2 * 0.45:.1f} {y - 0.03 * L:.1f} {sg * h2:.1f} {y - 0.13 * L:.1f}" '
                     f'fill="none" stroke="{vein}" stroke-width="{wd * 0.008:.1f}" opacity="0.75"/>')
        u += 0.095
    pos = []
    rad = math.radians(rot)
    for fx, uu, sz in lesions:
        lx, ly, r = fx * hw_at(uu, wd), L / 2 - uu * L, wd * 0.048 * sz
        if health < 0.999:
            s.append(f'<circle cx="{lx:.1f}" cy="{ly:.1f}" r="{r * 2.3:.1f}" fill="url(#{hid})"/>')
        rc = r * (1 - 0.38 * health)
        ring = mix("#8f5c2c", "#7a644a", health)
        s.append(f'<ellipse cx="{lx:.1f}" cy="{ly:.1f}" rx="{rc:.1f}" ry="{rc * 0.88:.1f}" fill="{mix("#5e3218", "#a68c6c", health)}" '
                 f'stroke="{mix("#4a2510", "#5a4632", health)}" stroke-width="{1.5 + 1.5 * health:.1f}"/>')
        s.append(f'<ellipse cx="{lx:.1f}" cy="{ly:.1f}" rx="{rc * 0.55:.1f}" ry="{rc * 0.48:.1f}" fill="none" stroke="{ring}" stroke-width="1.6"/>')
        s.append(f'<circle cx="{lx:.1f}" cy="{ly:.1f}" r="{rc * 0.18:.1f}" fill="{ring}"/>')
        pos.append((cx + lx * math.cos(rad) - ly * math.sin(rad), cy + lx * math.sin(rad) + ly * math.cos(rad), r))
    s.append("</g>")
    for i in rings:
        fx, uu, sz = lesions[i]
        s.append(f'<circle cx="{fx * hw_at(uu, wd):.1f}" cy="{L / 2 - uu * L:.1f}" r="{wd * 0.048 * sz * 1.9:.1f}" '
                 f'fill="none" stroke="{CYAN}" stroke-width="5"/>')
    for i in dashed:
        fx, uu, sz = lesions[i]
        s.append(f'<circle cx="{fx * hw_at(uu, wd):.1f}" cy="{L / 2 - uu * L:.1f}" r="{wd * 0.048 * sz * 2.3:.1f}" '
                 f'fill="{CYAN}" fill-opacity="0.12" stroke="{CYAN}" stroke-width="5" stroke-dasharray="14 9" filter="url(#glow)"/>')
    s.append("</g>")
    return "".join(s), pos

# ---------------------------------------------------------------- props

def pedestal(x, top, w=240, h=200, ring=True):
    pid = uid("pd")
    ring_attr = f'stroke="{CYAN}" stroke-width="6" filter="url(#glow)"' if ring else 'stroke="#c9d1d4" stroke-width="2"'
    return (f'<defs><linearGradient id="{pid}" x1="0" x2="1"><stop offset="0" stop-color="#b8c0c4"/>'
            f'<stop offset=".45" stop-color="#eef2f3"/><stop offset="1" stop-color="#aab3b7"/></linearGradient></defs>'
            f'<ellipse cx="{x}" cy="{top + h + 6}" rx="{w * 0.75:.0f}" ry="{w * 0.12:.0f}" fill="#000" opacity="0.08"/>'
            f'<ellipse cx="{x}" cy="{top + h}" rx="{w / 2}" ry="{w * 0.13:.0f}" fill="#aab3b7"/>'
            f'<rect x="{x - w / 2}" y="{top}" width="{w}" height="{h}" fill="url(#{pid})"/>'
            f'<ellipse cx="{x}" cy="{top}" rx="{w / 2}" ry="{w * 0.13:.0f}" fill="#f4f7f8" {ring_attr}/>')


def panel(x, y, w, h, accent=True, op=0.95, rx=22):
    s = (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{rx}" fill="{CHAR}" fill-opacity="{op}" '
         f'filter="url(#shadow)"/>')
    if accent:
        s += f'<rect x="{x}" y="{y + 24}" width="7" height="{h - 48}" rx="3.5" fill="{CYAN}"/>'
    return s


def button(x, y, w, h, label, kind="primary", size=26):
    if kind == "primary":
        body = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{h / 2}" fill="{CYAN}" filter="url(#shadow)"/>'
        tc = CHAR
    else:
        body = f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{h / 2}" fill="{CHAR2}" stroke="#56606a" stroke-width="2"/>'
        tc = TXT
    return body + T(x + w / 2, y + h / 2 + size * 0.36, label, size, tc, 700, "middle")


def pill(x, y, label, fill=CHAR, tc=TXT, size=22, pad=22, h=44, anchor="start", weight=700):
    w = len(label) * size * 0.56 + pad * 2
    if anchor == "middle":
        x -= w / 2
    return (f'<rect x="{x:.0f}" y="{y}" width="{w:.0f}" height="{h}" rx="{h / 2}" fill="{fill}"/>'
            + T(x + w / 2, y + h / 2 + size * 0.36, label, size, tc, weight, "middle"))


def ring_gauge(cx, cy, r, val, col=GOOD, width=18, label=None, sub=None, track="#3b424a", tc=TXT, sc=SUB):
    c = 2 * math.pi * r
    s = (f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="{track}" stroke-width="{width}"/>'
         f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="{col}" stroke-width="{width}" stroke-linecap="round" '
         f'stroke-dasharray="{c * val:.1f} {c:.1f}" transform="rotate(-90 {cx} {cy})"/>')
    s += T(cx, cy + r * 0.2, label or f"{round(val * 100)}%", r * 0.55, tc, 700, "middle")
    if sub:
        s += T(cx, cy + r * 0.55, sub, max(14, r * 0.19), sc, 400, "middle")
    return s


def icon(kind, x, y, r=30, col=CYAN):
    s = f'<circle cx="{x}" cy="{y}" r="{r}" fill="none" stroke="{col}" stroke-width="3"/>'
    k = r / 30
    g = f'<g transform="translate({x},{y}) scale({k})" stroke="{col}" fill="none" stroke-width="3" stroke-linecap="round">'
    if kind == "cut":
        g += '<circle cx="-8" cy="9" r="6"/><circle cx="8" cy="9" r="6"/><line x1="-4" y1="4" x2="9" y2="-14"/><line x1="4" y1="4" x2="-9" y2="-14"/>'
    elif kind == "drop":
        g += f'<path d="M0 -15 C 10 -2, 12 4, 12 7 A12 12 0 0 1 -12 7 C -12 4, -10 -2, 0 -15Z" fill="{col}" fill-opacity="0.25"/>'
    elif kind == "spray":
        g += '<rect x="-12" y="-4" width="14" height="18" rx="3"/><path d="M-8 -4 L-8 -11 L4 -11"/>'
        for dx, dy in [(10, -14), (15, -8), (11, -2), (16, 3)]:
            g += f'<circle cx="{dx}" cy="{dy}" r="1.6" fill="{col}"/>'
    elif kind == "check":
        g += '<path d="M-11 1 L-3 9 L12 -9" stroke-width="4.5"/>'
    elif kind == "warn":
        g += f'<line x1="0" y1="-11" x2="0" y2="3" stroke-width="4.5"/><circle cx="0" cy="11" r="2.5" fill="{col}"/>'
    g += "</g>"
    return s + g


def hand_point(tip_x, tip_y, ang=0, s=1.0):
    px, py = -34 * s, -112 * s
    a = math.radians(ang)
    rx, ry = px * math.cos(a) - py * math.sin(a), px * math.sin(a) + py * math.cos(a)
    tx, ty = tip_x - rx, tip_y - ry
    st = f'fill="{GLOVE}" stroke="{GLOVE_D}" stroke-width="3"'
    return (f'<g transform="translate({tx:.1f},{ty:.1f}) rotate({ang}) scale({s})" filter="url(#shadow)">'
            f'<rect x="-64" y="108" width="128" height="600" rx="16" fill="#f1f3f5" stroke="#cfd6da" stroke-width="3"/>'
            f'<rect x="-58" y="100" width="116" height="34" rx="10" fill="#e1e6e9" stroke="#c4ccd0" stroke-width="3"/>'
            f'<rect x="-56" y="-2" width="112" height="118" rx="40" {st}/>'
            f'<rect x="-50" y="-118" width="32" height="140" rx="16" {st}/>'
            f'<rect x="-16" y="-26" width="28" height="52" rx="14" {st}/>'
            f'<rect x="12" y="-20" width="26" height="48" rx="13" {st}/>'
            f'<rect x="36" y="-10" width="22" height="42" rx="11" {st}/>'
            f'<rect x="-92" y="20" width="34" height="78" rx="17" transform="rotate(-35 -75 59)" {st}/>'
            f'</g>')


def fist(x, y, ang=0, s=1.0):
    st = f'fill="{GLOVE}" stroke="{GLOVE_D}" stroke-width="3"'
    g = (f'<g transform="translate({x:.1f},{y:.1f}) rotate({ang}) scale({s})" filter="url(#shadow)">'
         f'<rect x="-66" y="96" width="132" height="600" rx="16" fill="#f1f3f5" stroke="#cfd6da" stroke-width="3"/>'
         f'<rect x="-60" y="78" width="120" height="36" rx="10" fill="#e1e6e9" stroke="#c4ccd0" stroke-width="3"/>'
         f'<rect x="-54" y="-44" width="108" height="130" rx="36" {st}/>')
    for i in range(4):
        g += f'<rect x="-62" y="{-42 + i * 27}" width="96" height="25" rx="12.5" fill="#a9b0b7" stroke="{GLOVE_D}" stroke-width="3"/>'
    g += f'<rect x="20" y="-62" width="34" height="74" rx="17" transform="rotate(25 37 -25)" {st}/></g>'
    return g


def shears(x, y, ang=0, s=1.0):
    return (f'<g transform="translate({x:.1f},{y:.1f}) rotate({ang}) scale({s})">'
            '<path d="M-4 0 L-20 -100 Q-8 -110 4 -96 Z" fill="#cfd6db" stroke="#6f7a82" stroke-width="2.5"/>'
            '<path d="M4 0 L22 -96 Q11 -108 -2 -98 Z" fill="#e8ecef" stroke="#6f7a82" stroke-width="2.5"/>'
            '<path d="M-6 4 Q-42 50 -48 118 Q-37 128 -26 118 Q-22 62 4 12 Z" fill="#d8392f" stroke="#9e2720" stroke-width="2"/>'
            '<path d="M6 4 Q42 50 48 118 Q37 128 26 118 Q22 62 -4 12 Z" fill="#c42f27" stroke="#9e2720" stroke-width="2"/>'
            '<circle cx="0" cy="2" r="8" fill="#59636a"/></g>')


def spray_bottle(x, y, s=1.0, label=True):
    g = (f'<g transform="translate({x},{y}) scale({s})">'
         '<rect x="-55" y="-170" width="110" height="170" rx="20" fill="#f4f6f7" stroke="#bfc8cc" stroke-width="3"/>'
         '<rect x="-22" y="-198" width="44" height="32" rx="6" fill="#e5e9eb" stroke="#bfc8cc" stroke-width="3"/>'
         '<path d="M-36 -198 L40 -198 L40 -236 L-28 -236 Q-46 -236 -46 -218 Z" fill="#d8392f"/>'
         '<rect x="38" y="-232" width="26" height="12" rx="3" fill="#d8392f"/>'
         '<path d="M-4 -198 Q-10 -172 -34 -160 L-30 -150 Q0 -162 10 -198Z" fill="#b92c25"/>')
    if label:
        g += ('<rect x="-40" y="-120" width="80" height="54" rx="6" fill="#fff" stroke="#dde2e4"/>'
              + T(0, -98, "Training", 16, DARK, 600, "middle") + T(0, -78, "spray", 16, DARK, 600, "middle"))
    return g + "</g>"


def drip_valve(x, y, s=1.0):
    g = (f'<g transform="translate({x},{y}) scale({s})">'
         '<rect x="-95" y="-14" width="190" height="26" rx="8" fill="#8c9ca6" stroke="#6b7a83" stroke-width="2"/>'
         '<circle cx="0" cy="0" r="26" fill="#a6b3ba" stroke="#6b7a83" stroke-width="3"/>'
         '<rect x="-6" y="-62" width="12" height="40" fill="#6b7a83"/>'
         '<ellipse cx="0" cy="-64" rx="40" ry="13" fill="#d8392f" stroke="#9e2720" stroke-width="2"/>'
         '<rect x="80" y="-2" width="12" height="40" rx="5" fill="#2f3a40"/>')
    for dy in (54, 76):
        g += f'<path d="M86 {dy - 8} C 92 {dy}, 92 {dy + 4}, 86 {dy + 6} C 80 {dy + 4}, 80 {dy}, 86 {dy - 8}Z" fill="{CYAN}"/>'
    return g + "</g>"


def fan(x, y, s=1.0):
    g = (f'<g transform="translate({x},{y}) scale({s})">'
         '<rect x="-10" y="-70" width="20" height="70" fill="#59636a"/><rect x="-50" y="-10" width="100" height="14" rx="7" fill="#59636a"/>'
         '<circle cx="0" cy="-130" r="72" fill="#eef2f4" stroke="#59636a" stroke-width="8"/>')
    for a in (0, 120, 240):
        g += f'<ellipse cx="0" cy="-160" rx="17" ry="30" fill="#8fd9e3" stroke="#3aa9b8" stroke-width="2" transform="rotate({a} 0 -130)"/>'
    g += '<circle cx="0" cy="-130" r="12" fill="#59636a"/>'
    for i in range(-3, 4):
        g += f'<line x1="{i * 18}" y1="-196" x2="{i * 18}" y2="-64" stroke="#59636a" stroke-width="1.5" opacity="0.4"/>'
    return g + "</g>"


def magnifier(x, y, s=1.0, ang=0):
    return (f'<g transform="translate({x},{y}) rotate({ang}) scale({s})">'
            '<rect x="-10" y="40" width="20" height="100" rx="10" fill="#2b3036"/>'
            '<circle cx="0" cy="0" r="46" fill="#cfeaf5" fill-opacity="0.55" stroke="#2b3036" stroke-width="12"/>'
            '<path d="M-24 -18 Q-14 -30 2 -32" stroke="#fff" stroke-width="5" fill="none" stroke-linecap="round"/></g>')


def bench(x0, x1, y0, depth=40, bottom=SCENE_H):
    return (f'<rect x="{x0 - 30}" y="{y0 + depth}" width="{x1 - x0 + 60}" height="{bottom - y0 - depth}" fill="#d3d9dc"/>'
            f'<polygon points="{x0},{y0} {x1},{y0} {x1 + 30},{y0 + depth} {x0 - 30},{y0 + depth}" fill="#f3f5f6" stroke="#cdd4d7" stroke-width="2"/>'
            f'<rect x="{x0 - 30}" y="{y0 + depth}" width="{x1 - x0 + 60}" height="6" fill="#bfc7cb"/>')


def pot(x, y, s=1.0, color="#b9c1c5"):
    return (f'<g transform="translate({x},{y}) scale({s})">'
            f'<ellipse cx="0" cy="4" rx="95" ry="16" fill="#000" opacity="0.08"/>'
            f'<path d="M-70 0 L70 0 L84 -110 L-84 -110Z" fill="{color}"/>'
            f'<rect x="-92" y="-128" width="184" height="24" rx="6" fill="{mix(color, "#ffffff", 0.3)}"/>'
            f'<ellipse cx="0" cy="-122" rx="80" ry="10" fill="#5b4433"/></g>')


def plant(x, y, s=1.0, health=1.0, curl=0.0, height=260):
    col = mix("#c8bf4a", "#4f9a3c", health)
    g = [f'<g transform="translate({x},{y}) scale({s})">',
         f'<path d="M0 0 C 6 -{height * 0.4:.0f}, -6 -{height * 0.7:.0f}, 0 -{height}" stroke="{mix("#7b8a3a", "#3f7a2c", health)}" stroke-width="9" fill="none" stroke-linecap="round"/>']
    for i in range(6):
        yy = -height * (0.18 + 0.13 * i)
        side = 1 if i % 2 == 0 else -1
        size = 1.0 - 0.08 * i
        rx, ry = 58 * size * (1 - 0.35 * curl), 22 * size * (1 + 0.4 * curl)
        ang = side * (-22 - 35 * curl)
        g.append(f'<ellipse cx="{side * rx * 0.85:.0f}" cy="{yy:.0f}" rx="{rx:.0f}" ry="{ry:.0f}" fill="{col}" '
                 f'stroke="{mix("#9a8a2a", "#2f6b2b", health)}" stroke-width="2.5" transform="rotate({ang:.0f} 0 {yy:.0f})"/>')
        g.append(f'<line x1="0" y1="{yy:.0f}" x2="{side * rx * 1.6:.0f}" y2="{yy:.0f}" stroke="{mix("#e2dc86", "#a8db86", health)}" '
                 f'stroke-width="2" transform="rotate({ang:.0f} 0 {yy:.0f})" opacity="0.8"/>')
    g.append(f'<ellipse cx="0" cy="-{height + 10}" rx="{20 * (1 - 0.3 * curl):.0f}" ry="{14 * (1 + 0.3 * curl):.0f}" fill="{col}"/>')
    g.append("</g>")
    return "".join(g)


def arrow(x0, y0, x1, y1, col=CYAN, w=8, head=26):
    a = math.atan2(y1 - y0, x1 - x0)
    bx, by = x1 - head * math.cos(a), y1 - head * math.sin(a)
    lx, ly = bx + head * 0.6 * math.sin(a), by - head * 0.6 * math.cos(a)
    rx, ry = bx - head * 0.6 * math.sin(a), by + head * 0.6 * math.cos(a)
    return (f'<line x1="{x0}" y1="{y0}" x2="{bx:.0f}" y2="{by:.0f}" stroke="{col}" stroke-width="{w}" stroke-linecap="round"/>'
            f'<polygon points="{x1},{y1} {lx:.0f},{ly:.0f} {rx:.0f},{ry:.0f}" fill="{col}"/>')


def arc(cx, cy, r, a0, a1):
    x0, y0 = cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0))
    x1, y1 = cx + r * math.cos(math.radians(a1)), cy + r * math.sin(math.radians(a1))
    return f"M{x0:.1f} {y0:.1f} A{r} {r} 0 {1 if a1 - a0 > 180 else 0} 1 {x1:.1f} {y1:.1f}"


def caption(num, title, lines):
    s = (f'<rect x="0" y="{SCENE_H}" width="{W}" height="{H - SCENE_H}" fill="#1b1f24"/>'
         f'<rect x="0" y="{SCENE_H}" width="{W}" height="4" fill="{CYAN}"/>')
    s += T(48, SCENE_H + 98, num, 76, CYAN, 800)
    s += T(190, SCENE_H + 56, title, 36, TXT, 700)
    for i, line in enumerate(lines):
        s += T(190, SCENE_H + 94 + i * 30, line, 22, SUB)
    s += T(W - 48, SCENE_H + 56, "Treatment Lab · simple concept", 20, "#6f7d85", 600, "end")
    return s


DEFS = ('<defs><linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#bfe0f3"/>'
        '<stop offset="1" stop-color="#eef6ee"/></linearGradient>'
        '<linearGradient id="floor" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#dfe5e6"/>'
        '<stop offset="1" stop-color="#f5f7f7"/></linearGradient>'
        '<filter id="glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="5" result="b"/>'
        '<feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>'
        '<filter id="shadow" x="-20%" y="-20%" width="140%" height="140%"><feDropShadow dx="0" dy="8" stdDeviation="10" '
        'flood-color="#000" flood-opacity="0.22"/></filter></defs>')


def seg_bar(x, y, val, rmin, rmax, n=10, sw=25, gap=5, h=22):
    s = [f'<rect x="{x + (rmin - 1) * (sw + gap) - 5}" y="{y - 6}" width="{(rmax - rmin + 1) * (sw + gap) - gap + 10}" '
         f'height="{h + 12}" rx="8" fill="none" stroke="{GOOD}" stroke-width="2.5" stroke-dasharray="6 4"/>']
    ok = rmin <= val <= rmax
    for i in range(1, n + 1):
        fill = (CYAN if ok else WARN) if i <= val else "#3d444c"
        s.append(f'<rect x="{x + (i - 1) * (sw + gap)}" y="{y}" width="{sw}" height="{h}" rx="5" fill="{fill}"/>')
    return "".join(s)


def round_btn(cx, cy, label, r=28):
    return (f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{CHAR2}" stroke="#5a646e" stroke-width="2"/>'
            + T(cx, cy + 12, label, 34, TXT, 700, "middle"))

# ---------------------------------------------------------------- concepts

def concept_01():
    s = [background()]
    s.append(pedestal(720, 712, 250, 200))
    lf, _ = leaf(720, 400, 520, 380, health=0.15, rings=(0, 1, 2), stem=60)
    s.append(lf)
    s.append(panel(540, 46, 360, 64, accent=False))
    s.append(T(568, 88, "Leaf health", 22, SUB, 600))
    s.append('<rect x="700" y="70" width="170" height="16" rx="8" fill="#3d444c"/><rect x="700" y="70" width="82" height="16" rx="8" fill="#e0a43c"/>')
    x0, y0 = 1120, 100
    s.append(panel(x0, y0, 600, 810))
    s.append(T(x0 + 44, y0 + 68, "Tomato · Early blight", 38, TXT, 700))
    s.append(T(x0 + 44, y0 + 104, "Fungal leaf spot. Spreads with splashing water.", 21, SUB))
    xs = [x0 + 100, x0 + 300, x0 + 500]
    cy = y0 + 170
    s.append(f'<line x1="{xs[0]}" y1="{cy}" x2="{xs[1]}" y2="{cy}" stroke="{GOOD}" stroke-width="5"/>')
    s.append(f'<line x1="{xs[1]}" y1="{cy}" x2="{xs[2]}" y2="{cy}" stroke="#4a525a" stroke-width="5"/>')
    s.append(f'<circle cx="{xs[0]}" cy="{cy}" r="24" fill="{GOOD}"/><path d="M{xs[0] - 11} {cy} l8 8 l14 -16" stroke="#1b1f24" stroke-width="5" fill="none"/>')
    s.append(f'<circle cx="{xs[1]}" cy="{cy}" r="24" fill="{CYAN}" filter="url(#glow)"/>' + T(xs[1], cy + 10, "2", 26, CHAR, 800, "middle"))
    s.append(f'<circle cx="{xs[2]}" cy="{cy}" r="24" fill="none" stroke="#5a646e" stroke-width="4"/>' + T(xs[2], cy + 10, "3", 26, SUB, 700, "middle"))
    for xx, lab, c in zip(xs, ["Diagnose", "Treat", "Recover"], [GOOD, CYAN, SUB]):
        s.append(T(xx, y0 + 230, lab, 22, c, 700, "middle"))
    s.append(f'<line x1="{x0 + 44}" y1="{y0 + 262}" x2="{x0 + 556}" y2="{y0 + 262}" stroke="#3d444c" stroke-width="2"/>')
    rows = [("cut", "Remove spotted leaves", 6, 6, 8, "Good amount"),
            ("drop", "Drip watering, no splash", 8, 7, 10, "Good amount"),
            ("spray", "Protect healthy leaves", 3, 5, 7, "Too little. Aim for the green zone")]
    for i, (ic, name, v, a, b, status) in enumerate(rows):
        ry = y0 + 292 + i * 140
        s.append(icon(ic, x0 + 76, ry + 46, 32))
        s.append(T(x0 + 126, ry + 34, name, 25, TXT, 700))
        s.append(seg_bar(x0 + 126, ry + 56, v, a, b))
        s.append(round_btn(x0 + 476, ry + 66, "−"))
        s.append(round_btn(x0 + 546, ry + 66, "+"))
        ok = a <= v <= b
        s.append(T(x0 + 126, ry + 112, ("✓ " if ok else "▲ ") + status, 20, GOOD if ok else WARN, 600))
    s.append(button(x0 + 44, y0 + 716, 512, 66, "Apply treatment", size=28))
    s.append(caption("01", "One leaf, one card", [
        "Replace the six floating panels (spread from −98° to +80°) with one card beside the specimen. No head turning.",
        "Three plain steps: Diagnose → Treat → Recover. Green dashed zone = the right amount; big −/+ for Quest 2 ray or poke."]))
    return s


def station_sign(x, y, num, title, sub, active=False):
    w = 380
    s = [f'<line x1="{x - 120}" y1="0" x2="{x - 120}" y2="{y}" stroke="#9aa5ab" stroke-width="3"/>',
         f'<line x1="{x + 120}" y1="0" x2="{x + 120}" y2="{y}" stroke="#9aa5ab" stroke-width="3"/>',
         panel(x - w / 2, y, w, 110, accent=False)]
    c = CYAN if active else "#5a646e"
    s.append(f'<circle cx="{x - w / 2 + 52}" cy="{y + 55}" r="30" fill="{c}"/>' + T(x - w / 2 + 52, y + 67, num, 32, CHAR if active else TXT, 800, "middle"))
    s.append(T(x - w / 2 + 100, y + 50, title, 30, TXT, 700))
    s.append(T(x - w / 2 + 100, y + 84, sub, 19, SUB))
    return "".join(s)


def concept_02():
    s = [background(600)]
    s.append(f'<path d="M960 900 C 960 840, 380 880, 380 810" stroke="{CYAN}" stroke-width="40" fill="none" opacity="0.18" stroke-linecap="round"/>')
    s.append(f'<path d="M380 810 C 520 770, 820 790, 960 790 C 1100 790, 1400 780, 1540 810" stroke="{CYAN}" stroke-width="40" fill="none" opacity="0.18" stroke-linecap="round"/>')
    for x, y, rot in [(760, 862, 175), (560, 852, 190), (700, 790, 0), (1220, 789, 0), (1400, 790, 5)]:
        s.append(f'<path d="M-12 -12 L6 0 L-12 12" transform="translate({x},{y}) rotate({rot})" stroke="{CYAN}" stroke-width="7" fill="none" stroke-linecap="round"/>')
    s.append(pedestal(380, 660, 200, 140))
    lf, _ = leaf(380, 470, 330, 240, health=0.1, stem=40, rings=(1,))
    s.append(lf)
    s.append(magnifier(500, 420, 0.9, 30))
    s.append(station_sign(380, 140, "1", "Look", "Find the spots", True))
    s.append(bench(800, 1120, 640, bottom=800))
    s.append('<rect x="790" y="800" width="20" height="40" fill="#b8c0c4"/><rect x="1110" y="800" width="20" height="40" fill="#b8c0c4"/>')
    s.append(spray_bottle(850, 652, 0.55))
    s.append(shears(950, 668, 90, 0.6))
    s.append(drip_valve(1065, 630, 0.55))
    s.append(f'<ellipse cx="960" cy="905" rx="90" ry="20" fill="none" stroke="{CYAN}" stroke-width="5"/>')
    s.append(T(960, 878, "You start here", 22, DARK, 700, "middle"))
    s.append(station_sign(960, 140, "2", "Treat", "Pick the right care"))
    s.append(pedestal(1540, 660, 200, 140))
    lf, _ = leaf(1540, 470, 330, 240, health=0.85, stem=40)
    s.append(lf)
    s.append(f'<circle cx="1700" cy="330" r="62" fill="{CHAR}" fill-opacity="0.95" filter="url(#shadow)"/>')
    s.append(ring_gauge(1700, 330, 46, 0.72, width=10, label="72%"))
    s.append(station_sign(1540, 140, "3", "Result", "See the leaf recover"))
    s.append(caption("02", "Walk the treatment: three stations", [
        "Each step lives at its own station, so the room explains the order: Look → Treat → Result.",
        "A cyan floor path shows what is next. No menus to read, and each station shows only what that step needs."]))
    return s


def concept_03():
    s = [background()]
    s.append(pedestal(900, 722, 250, 200))
    lf, pos = leaf(900, 400, 560, 410, health=0.1, stem=60, dashed=(0,))
    s.append(lf)
    lx, ly, _ = pos[0]
    a = math.radians(-40)
    sc = 1.15
    tipx, tipy = 100 * math.sin(a) * sc, -100 * math.cos(a) * sc
    px, py = lx + 38 - tipx, ly + 30 - tipy
    s.append(shears(px, py, -40, sc))
    hx, hy = px + (-70 * math.sin(a)) * sc, py + (70 * math.cos(a)) * sc
    s.append(fist(hx, hy, -40, 1.0))
    s.append(pill(lx + 72, ly - 82, "Cut here", fill=CYAN, tc=CHAR, size=22))
    s.append(bench(90, 680, 770))
    s.append(spray_bottle(170, 790, 0.55))
    s.append(drip_valve(320, 778, 0.7))
    s.append(fan(470, 790, 0.6))
    s.append(magnifier(600, 735, 0.6, 25))
    for x, lab in [(170, "Spray"), (320, "Drip"), (470, "Fan"), (610, "Look")]:
        s.append(T(x, 880, lab, 22, DARK, 700, "middle"))
    s.append('<rect x="1500" y="700" width="170" height="200" rx="14" fill="#59636a"/><rect x="1488" y="684" width="194" height="28" rx="10" fill="#454d55"/>')
    s.append(panel(1515, 760, 140, 60, accent=False))
    s.append(T(1585, 798, "Plant waste", 18, TXT, 700, "middle"))
    s.append(f'<path d="M1260 600 Q 1470 520 1580 670" stroke="{CYAN}" stroke-width="5" stroke-dasharray="12 10" fill="none"/>')
    s.append(T(1450, 520, "then drop it here", 22, DARK, 600, "middle"))
    s.append(panel(640, 26, 640, 92))
    s.append(f'<circle cx="704" cy="72" r="28" fill="{CYAN}"/>' + T(704, 83, "1", 30, CHAR, 800, "middle"))
    s.append(T(752, 70, "Cut the spotted leaves", 30, TXT, 700))
    s.append(T(752, 100, "Grab the shears from the tray", 19, SUB))
    for i in range(3):
        s.append(f'<circle cx="{1190 + i * 28}" cy="72" r="9" fill="{GOOD if i == 0 else "#4a525a"}"/>')
    s.append(caption("03", "Grab a tool, touch the leaf", [
        "Treat with the hands, not sliders: cut with shears, open the drip valve, run the fan, spray healthy leaves.",
        "One short instruction at a time, and a cyan ring marks the exact target. The tray holds only this case's tools."]))
    return s


def dial(cx, cy, r, val, a, b, name, status, ok):
    s = [T(cx, cy - r - 40, name, 26, TXT, 700, "middle")]
    s.append(f'<path d="{arc(cx, cy, r, 135, 405)}" stroke="#3b424a" stroke-width="20" fill="none" stroke-linecap="round"/>')
    s.append(f'<path d="{arc(cx, cy, r, 135 + 27 * a, 135 + 27 * b)}" stroke="{GOOD}" stroke-width="20" fill="none"/>')
    for i in range(11):
        an = math.radians(135 + 27 * i)
        s.append(f'<line x1="{cx + (r + 16) * math.cos(an):.1f}" y1="{cy + (r + 16) * math.sin(an):.1f}" '
                 f'x2="{cx + (r + 26) * math.cos(an):.1f}" y2="{cy + (r + 26) * math.sin(an):.1f}" stroke="#6b757e" stroke-width="3"/>')
    s.append(f'<circle cx="{cx}" cy="{cy}" r="{r * 0.62:.0f}" fill="#4a525a" stroke="#626c75" stroke-width="4" filter="url(#shadow)"/>')
    an = math.radians(135 + 27 * val)
    col = CYAN if ok else WARN
    s.append(f'<line x1="{cx + r * 0.2 * math.cos(an):.1f}" y1="{cy + r * 0.2 * math.sin(an):.1f}" x2="{cx + r * 0.58 * math.cos(an):.1f}" '
             f'y2="{cy + r * 0.58 * math.sin(an):.1f}" stroke="{col}" stroke-width="9" stroke-linecap="round"/>')
    s.append(f'<circle cx="{cx + r * math.cos(an):.1f}" cy="{cy + r * math.sin(an):.1f}" r="13" fill="{col}" filter="url(#glow)"/>')
    s.append(T(cx, cy + r + 30, status, 22, GOOD if ok else WARN, 700, "middle"))
    return "".join(s)


def concept_04():
    s = [background()]
    s.append(pedestal(450, 712, 240, 200))
    lf, _ = leaf(450, 400, 520, 370, health=0.15, stem=60)
    s.append(lf)
    s.append(pill(450, 92, "Leaf reacts when you press Apply", fill=CHAR, size=20, anchor="middle"))
    s.append('<rect x="1060" y="860" width="24" height="80" fill="#59636a"/><rect x="1640" y="860" width="24" height="80" fill="#59636a"/>')
    x0, y0 = 830, 250
    s.append(panel(x0, y0, 1040, 620))
    s.append(T(x0 + 50, y0 + 62, "Treatment mix", 34, TXT, 700))
    s.append(T(x0 + 290, y0 + 62, "Tomato · Early blight", 24, SUB))
    s.append(dial(x0 + 190, y0 + 300, 105, 6, 6, 8, "Sanitation", "✓ Good", True))
    s.append(dial(x0 + 520, y0 + 300, 105, 8, 7, 10, "Drip water", "✓ Good", True))
    s.append(dial(x0 + 850, y0 + 300, 105, 3, 5, 7, "Spray", "▲ Turn up", False))
    s.append(button(x0 + 310, y0 + 510, 420, 70, "Apply", size=30))
    s.append(hand_point(x0 + 905, y0 + 290, -70, 0.85))
    s.append(caption("04", "Turn the dials", [
        "Each treatment is one big dial with a green 'good zone'. Easier to read than numbers, and the gesture is natural in VR.",
        "Mistakes show before you press Apply (amber pointer). Same data as today: rec_min / rec_max from treatment_controls."]))
    return s


def concept_05():
    s = [background()]
    s.append(arrow(230, 120, 1700, 120, CYAN, 8, 30))
    s.append(pill(965, 140, "Same leaf after one good treatment", fill=CHAR, size=22, anchor="middle"))
    s.append('<rect x="110" y="690" width="1700" height="28" rx="8" fill="#f6f8f9" stroke="#cdd4d7" stroke-width="2" filter="url(#shadow)"/>')
    stages = [("Day 0", 0.0, "48%", "Active spots, yellow halos"),
              ("Week 1", 0.35, "57%", "Halos fading"),
              ("Week 3", 0.65, "65%", "Spots dry, no new ones"),
              ("Week 6", 0.9, "72%", "Old spots stay: managed")]
    for i, (lab, hp, pct, note) in enumerate(stages):
        x = 330 + i * 420
        s.append(f'<rect x="{x - 6}" y="640" width="12" height="52" fill="#7d878e"/><ellipse cx="{x}" cy="690" rx="50" ry="9" fill="#9aa5ab"/>')
        lf, _ = leaf(x, 430, 390, 285, health=hp, stem=30, glow=(i == 3))
        s.append(lf)
        s.append(pill(x, 200, lab, fill=CYAN if i == 3 else CHAR, tc=CHAR if i == 3 else TXT, size=24, anchor="middle"))
        s.append(T(x, 770, "Health " + pct, 28, DARK, 800, "middle"))
        s.append(T(x, 804, note, 20, "#56626a", 600, "middle"))
    s.append('<rect x="330" y="868" width="1260" height="10" rx="5" fill="#c9d1d4"/>')
    s.append(f'<rect x="330" y="868" width="1260" height="10" rx="5" fill="{CYAN}" opacity="0.7"/>')
    for i in range(4):
        s.append(f'<circle cx="{330 + i * 420}" cy="873" r="11" fill="{CYAN}"/>')
    s.append(f'<circle cx="1590" cy="873" r="24" fill="#fff" stroke="{CYAN}" stroke-width="7" filter="url(#shadow)"/>')
    s.append(T(240, 882, "Time", 22, DARK, 700, "middle"))
    s.append(caption("05", "Fast-forward the recovery", [
        "After Apply, slide through time and watch the same leaf recover week by week, instead of a 3.5 s colour fade.",
        "Honest biology: managed leaves stop spreading and dry out, but old spots never vanish and health stays below 100%."]))
    return s


def concept_06():
    s = [background()]
    s.append(pedestal(280, 712, 200, 200, ring=False))
    lf, _ = leaf(280, 430, 440, 310, health=0.0, stem=60)
    s.append(lf)
    s.append(pedestal(720, 712, 200, 200))
    lf, _ = leaf(720, 430, 440, 310, health=0.85, stem=60, glow=True)
    s.append(lf)
    s.append(pill(280, 130, "Before", fill=CHAR, size=24, anchor="middle"))
    s.append(pill(720, 130, "After", fill=CYAN, tc=CHAR, size=24, anchor="middle"))
    s.append(arrow(445, 440, 555, 440, CYAN, 8, 28))
    x0, y0 = 1000, 80
    s.append(panel(x0, y0, 800, 820))
    s.append(pill(x0 + 50, y0 + 44, "MANAGED", fill=GOOD, tc="#10261a", size=22))
    s.append(T(x0 + 50, y0 + 140, "Tomato · Early blight", 40, TXT, 700))
    s.append(T(x0 + 50, y0 + 176, "Session complete in 0:52", 21, SUB))
    s.append(ring_gauge(x0 + 170, y0 + 330, 105, 0.72, width=20, sub="leaf health"))
    s.append(T(x0 + 320, y0 + 300, "Up from 48%", 34, GOOD, 800))
    s.append(T(x0 + 320, y0 + 340, "Spots dried and stopped spreading.", 22, TXT))
    s.append(T(x0 + 320, y0 + 370, "Old spots stay. That is normal.", 22, SUB))
    rows = [("check", GOOD, "Sanitation", "Right amount"),
            ("check", GOOD, "Drip watering", "Right amount: leaves stayed dry"),
            ("warn", WARN, "Protective spray", "A little low: healthy leaves exposed")]
    for i, (ic, c, name, msg) in enumerate(rows):
        ry = y0 + 490 + i * 72
        s.append(icon(ic, x0 + 80, ry, 24, c))
        s.append(T(x0 + 125, ry + 9, name, 24, TXT, 700))
        s.append(T(x0 + 350, ry + 9, msg, 21, SUB))
    s.append(button(x0 + 50, y0 + 718, 330, 66, "Try again", kind="ghost"))
    s.append(button(x0 + 410, y0 + 718, 340, 66, "Next case"))
    s.append(caption("06", "One clear result", [
        "Before and after side by side, one health score, and one line of feedback per choice. Nothing else to read.",
        "The amber line tells the learner exactly what to change on 'Try again'."]))
    return s


def concept_07():
    s = [background()]
    s.append(f'<ellipse cx="1400" cy="806" rx="190" ry="40" fill="none" stroke="{CYAN}" stroke-width="6" filter="url(#glow)"/>')
    s.append(pot(520, 800, 1.0, "#a7b0b5"))
    s.append(plant(520, 680, 1.0, health=0.12, curl=0.55, height=250))
    for dx, dy in [(-40, -120), (35, -170), (-20, -220), (50, -90)]:
        s.append(f'<circle cx="{520 + dx}" cy="{680 + dy}" r="5" fill="#f4f4ee" opacity="0.9"/>')
    s.append('<path d="M370 820 Q350 520 430 360 Q500 290 520 300 Q540 290 610 360 Q690 520 670 820 Z" fill="#ffffff" fill-opacity="0.38" stroke="#c4d0d6" stroke-width="4"/>')
    s.append('<path d="M420 760 Q410 560 470 420" stroke="#ffffff" stroke-width="6" fill="none" opacity="0.7"/>')
    s.append('<path d="M500 300 L520 268 L540 300 Z" fill="#c4d0d6"/><rect x="505" y="282" width="30" height="12" rx="5" fill="#d8392f"/>')
    s.append(pill(520, 870, "Sealed: no insects in or out", fill=CHAR, size=20, anchor="middle"))
    s.append(arrow(780, 600, 1150, 600, CYAN, 12, 40))
    s.append(T(965, 570, "Replace", 34, DARK, 800, "middle"))
    s.append(pot(1400, 800, 1.0, "#c9d1d4"))
    s.append(plant(1400, 680, 0.85, health=1.0, curl=0.0, height=230))
    s.append(pill(1400, 870, "Clean stock", fill=CYAN, tc=CHAR, size=22, anchor="middle"))
    s.append(panel(400, 36, 1120, 150))
    s.append(T(450, 100, "Tomato · Yellow leaf curl virus", 38, TXT, 700))
    s.append(T(450, 144, "No spray cures a virus. Contain the plant, clean up, then replant.", 24, SUB))
    s.append(pill(1320, 66, "REPLACE", fill=WARN, tc="#2a1d05", size=22))
    for i, lab in enumerate(["1 · Bag the plant", "2 · Clean hands and tools", "3 · Plant clean stock"]):
        s.append(pill(560 + i * 400, 230, lab, fill="#ffffff", tc=DARK, size=21, anchor="middle"))
    s.append(caption("07", "When the right answer is 'replace'", [
        "Five cases (both late blights, both tomato viruses, grape Esca) can't be cured. Show it visually, not only in a score.",
        "The learner bags the sick plant and swaps in a clean pot, so they learn that containment is the treatment."]))
    return s


CONCEPTS = [
    ("01_one_leaf_one_card", concept_01),
    ("02_three_stations", concept_02),
    ("03_grab_tool_touch_leaf", concept_03),
    ("04_treatment_dials", concept_04),
    ("05_recovery_timeline", concept_05),
    ("06_clear_result_card", concept_06),
    ("07_contain_and_replace", concept_07),
]


def find_browser():
    for p in [r"C:\Program Files\Google\Chrome\Application\chrome.exe",
              r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
              shutil.which("chrome") or "", shutil.which("msedge") or ""]:
        if p and pathlib.Path(p).exists():
            return p
    raise SystemExit("Chrome or Edge is required")


def render(name, parts, browser, tmp):
    svg = (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" '
           f'font-family="Segoe UI, Arial, sans-serif">{DEFS}{"".join(parts)}</svg>')
    html = tmp / f"{name}.html"
    html.write_text('<!doctype html><html><head><meta charset="utf-8"><style>html,body{margin:0;overflow:hidden}'
                    f'svg{{display:block}}</style></head><body>{svg}</body></html>', encoding="utf-8")
    out = OUT / f"{name}.png"
    subprocess.run([browser, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                    f"--user-data-dir={tmp / 'profile'}", f"--window-size={W},{H}", f"--screenshot={out}", html.as_uri()],
                   check=True, capture_output=True, timeout=120)
    img = Image.open(out)
    if img.size != (W, H):
        img.crop((0, 0, W, H)).save(out)
    return out


def contact_sheet(files):
    tw, th, gap, top, cols = 600, 338, 30, 150, 3
    rows = math.ceil(len(files) / cols)
    sheet = Image.new("RGB", (cols * tw + (cols + 1) * gap, top + rows * (th + gap) + gap), "#1b1f24")
    d = ImageDraw.Draw(sheet)
    try:
        f1 = ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", 52)
        f2 = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 26)
    except OSError:
        f1 = f2 = ImageFont.load_default()
    d.text((gap, 34), "Treatment Lab: simple concepts", font=f1, fill=TXT)
    d.text((gap, 100), "Seven ideas to make Treatment.unity easier to understand at a glance", font=f2, fill=SUB)
    for i, f in enumerate(files):
        r, c = divmod(i, cols)
        x, y = gap + c * (tw + gap), top + r * (th + gap)
        sheet.paste(Image.open(f).convert("RGB").resize((tw, th), Image.LANCZOS), (x, y))
    sheet.save(OUT / "00_overview.png")


def main():
    browser = find_browser()
    with tempfile.TemporaryDirectory() as t:
        files = [render(n, fn(), browser, pathlib.Path(t)) for n, fn in CONCEPTS]
    contact_sheet(files)
    for f in files:
        print(f.name)


if __name__ == "__main__":
    main()