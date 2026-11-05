import sys, os
from xml.sax.saxutils import escape

W, HH, RH = 290, 38, 28
NAVY = "#2f4f7f"
BLUE, TEAL, AMBER = "#3a6ea5", "#2c8c7c", "#b7791f"
BG = "#ffffff"

class Table:
    def __init__(self, name, x, y, rows):
        self.name, self.x, self.y, self.rows = name, x, y, rows
        self.h = HH + RH * len(rows)
    @property
    def l(self): return self.x
    @property
    def r(self): return self.x + W
    def ry(self, field):
        for i, row in enumerate(self.rows):
            if row[0] == field:
                return self.y + HH + i * RH + RH / 2
        raise KeyError(field)

def table_svg(t):
    o = []
    x, y = t.x, t.y
    o.append(f'<g filter="url(#sh)"><rect x="{x}" y="{y}" width="{W}" height="{t.h}" rx="7" fill="#fff"/></g>')
    o.append(f'<clipPath id="c_{t.name}"><rect x="{x}" y="{y}" width="{W}" height="{t.h}" rx="7"/></clipPath>')
    o.append(f'<g clip-path="url(#c_{t.name})">')
    o.append(f'<rect x="{x}" y="{y}" width="{W}" height="{HH}" fill="{NAVY}"/>')
    for i, (name, typ, key) in enumerate(t.rows):
        ry = y + HH + i * RH
        if i % 2 == 1:
            o.append(f'<rect x="{x}" y="{ry}" width="{W}" height="{RH}" fill="#f5f7fb"/>')
        if i:
            o.append(f'<line x1="{x}" y1="{ry}" x2="{x+W}" y2="{ry}" stroke="#e6eaf0"/>')
    o.append('</g>')
    o.append(f'<rect x="{x}" y="{y}" width="{W}" height="{t.h}" rx="7" fill="none" stroke="#b9c3d1" stroke-width="1.2"/>')
    o.append(f'<text x="{x+14}" y="{y+HH/2}" class="th">{escape(t.name)}</text>')
    for i, (name, typ, key) in enumerate(t.rows):
        cy = y + HH + i * RH + RH / 2
        if key:
            fill, ink = ("#f3c64b", "#5a4300") if key == "PK" else ("#dce7f7", "#1e4a8a")
            o.append(f'<rect x="{x+10}" y="{cy-8}" width="25" height="16" rx="3" fill="{fill}"/>')
            o.append(f'<text x="{x+22.5}" y="{cy}" class="badge" fill="{ink}">{key}</text>')
        cls = "fn pk" if key == "PK" else "fn"
        o.append(f'<text x="{x+44}" y="{cy}" class="{cls}">{escape(name)}</text>')
        o.append(f'<text x="{x+W-12}" y="{cy}" class="ty">{typ}</text>')
    return "\n".join(o)

def path_d(pts, r=7):
    d = f"M{pts[0][0]},{pts[0][1]}"
    for i in range(1, len(pts) - 1):
        (x0, y0), (x1, y1), (x2, y2) = pts[i - 1], pts[i], pts[i + 1]
        l1 = max(abs(x1 - x0), abs(y1 - y0)); l2 = max(abs(x2 - x1), abs(y2 - y1))
        rr = min(r, l1 / 2, l2 / 2)
        def toward(a, b, dist):
            ln = max(abs(b[0] - a[0]), abs(b[1] - a[1])) or 1
            return (a[0] + (b[0] - a[0]) * dist / ln, a[1] + (b[1] - a[1]) * dist / ln)
        p = toward((x1, y1), (x0, y0), rr); q = toward((x1, y1), (x2, y2), rr)
        d += f" L{p[0]:.1f},{p[1]:.1f} Q{x1},{y1} {q[0]:.1f},{q[1]:.1f}"
    d += f" L{pts[-1][0]},{pts[-1][1]}"
    return d

def marker(x, y, d, kind, col):
    # d = +1 when the line leaves the table to the right, -1 to the left
    o = []
    bar = lambda at: o.append(f'<line x1="{x+d*at}" y1="{y-7}" x2="{x+d*at}" y2="{y+7}" stroke="{col}" stroke-width="1.8"/>')
    ring = lambda at: o.append(f'<circle cx="{x+d*at}" cy="{y}" r="5" fill="#fff" stroke="{col}" stroke-width="1.6"/>')
    def foot():
        for dy in (-8, 8):
            o.append(f'<line x1="{x}" y1="{y+dy}" x2="{x+d*14}" y2="{y}" stroke="{col}" stroke-width="1.6"/>')
    if kind == "one": bar(9); bar(15)
    elif kind == "many0": foot(); ring(21)
    elif kind == "many1": foot(); bar(19)
    elif kind == "zero1": bar(9); ring(19)
    return "\n".join(o)

def edges_svg(groups):
    """groups: list of (colour, [edge]); edge = (points, child marker, parent marker).
     Later groups get a white under-stroke so they read as passing over earlier ones."""
    o, marks = [], []
    for gi, (col, edges) in enumerate(groups):
        if gi:
            for pts, _, _ in edges:
                o.append(f'<path d="{path_d(pts)}" fill="none" stroke="{BG}" stroke-width="8"/>')
        for pts, cm, pm in edges:
            o.append(f'<path d="{path_d(pts)}" fill="none" stroke="{col}" stroke-width="1.6" stroke-linejoin="round"/>')
            for (px, py), (nx, _), kind in ((pts[0], pts[1], cm), (pts[-1], pts[-2], pm)):
                marks.append(marker(px, py, 1 if nx > px else -1, kind, col))
    return "\n".join(o), "\n".join(marks)

def legend_svg(x, y, note=None):
    col = "#4b5563"
    o = [f'<line x1="{x-20}" y1="{y-26}" x2="{x+1110}" y2="{y-26}" stroke="#e2e6ec"/>']
    cx = x
    for key, label in (("PK", "Primary key"), ("FK", "Foreign key")):
        fill, ink = ("#f3c64b", "#5a4300") if key == "PK" else ("#dce7f7", "#1e4a8a")
        o.append(f'<rect x="{cx}" y="{y-8}" width="25" height="16" rx="3" fill="{fill}"/>')
        o.append(f'<text x="{cx+12.5}" y="{y}" class="badge" fill="{ink}">{key}</text>')
        o.append(f'<text x="{cx+33}" y="{y}" class="lg">{label}</text>')
        cx += 140
    for kind, label in (("one", "Exactly one"), ("many1", "One or many"), ("many0", "Zero or many")):
        o.append(f'<line x1="{cx}" y1="{y}" x2="{cx+44}" y2="{y}" stroke="{col}" stroke-width="1.6"/>')
        o.append(marker(cx + 44, y, -1, kind, col))
        o.append(f'<text x="{cx+54}" y="{y}" class="lg">{label}</text>')
        cx += 165
    if note:
        o.append(f'<text x="{cx+10}" y="{y}" class="lg note">{escape(note)}</text>')
    return "\n".join(o)

def svg(width, height, tables, groups, legend):
    lines, marks = edges_svg(groups)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">
<defs>
<filter id="sh" x="-10%" y="-10%" width="120%" height="125%"><feDropShadow dx="0" dy="2" stdDeviation="3" flood-color="#1f2a44" flood-opacity="0.14"/></filter>
<style>
text {{ font-family: "Segoe UI", Arial, sans-serif; dominant-baseline: central; }}
.th {{ font-size: 15.5px; font-weight: 600; fill: #fff; letter-spacing: .2px; }}
.fn {{ font-size: 14px; fill: #1f2937; }}
.pk {{ font-weight: 700; }}
.ty {{ font-family: Consolas, "Courier New", monospace; font-size: 12.5px; fill: #6b7280; text-anchor: end; }}
.badge {{ font-size: 9.5px; font-weight: 700; text-anchor: middle; }}
.lg {{ font-size: 13px; fill: #374151; }}
.note {{ fill: #6b7280; font-style: italic; }}
</style>
</defs>
<rect width="100%" height="100%" fill="{BG}"/>
{lines}
{"".join(table_svg(t) for t in tables)}
{marks}
{legend}
</svg>'''

V, I, B, T = "varchar", "integer", "boolean", "timestamp"

def fig16():
    users = Table("users", 80, 360, [("uid", V, "PK"), ("email", V, ""), ("passwordHash", V, ""), ("createdAt", T, "")])
    profiles = Table("profiles", 80, 100, [("profileId", V, "PK"), ("uid", V, "FK"), ("displayName", V, ""),
                                           ("avatarUrl", V, ""), ("wins", I, ""), ("losses", I, "")])
    mh = Table("match_history", 480, 40, [("matchId", V, "PK"), ("player1Uid", V, "FK"), ("player2Uid", V, "FK"),
                                          ("winnerUid", V, "FK"), ("player1DeckId", V, "FK"), ("player2DeckId", V, "FK"),
                                          ("durationSeconds", I, ""), ("playedAt", T, "")])
    decks = Table("decks", 480, 360, [("deckId", V, "PK"), ("uid", V, "FK"), ("deckName", V, ""),
                                      ("isRideDeck", B, ""), ("createdAt", T, ""), ("lastModified", T, "")])
    cards = Table("cards", 880, 40, [("cardId", V, "PK"), ("name", V, ""), ("grade", I, ""), ("power", I, ""),
                                     ("critical", I, ""), ("skillType", V, ""), ("triggerType", V, ""),
                                     ("nation", V, ""), ("clan", V, ""), ("imageUrl", V, "")])
    dc = Table("deck_cards", 880, 420, [("deckCardId", V, "PK"), ("deckId", V, "FK"), ("cardId", V, "FK"), ("quantity", I, "")])

    uy = users.ry("uid"); laneU, laneL = 425, 40
    to_users = [([(mh.l, mh.ry(f)), (laneU, mh.ry(f)), (laneU, uy), (users.r, uy)], "many0", "one")
                for f in ("player1Uid", "player2Uid", "winnerUid")]
    to_users.append(([(decks.l, decks.ry("uid")), (laneU, decks.ry("uid")), (laneU, uy), (users.r, uy)], "many0", "one"))
    to_users.append(([(profiles.l, profiles.ry("uid")), (laneL, profiles.ry("uid")), (laneL, uy), (users.l, uy)], "one", "one"))

    dy = decks.ry("deckId"); laneD = 825
    to_decks = [([(mh.r, mh.ry(f)), (laneD, mh.ry(f)), (laneD, dy), (decks.r, dy)], "many0", "one")
                for f in ("player1DeckId", "player2DeckId")]
    to_decks.append(([(dc.l, dc.ry("deckId")), (laneD, dc.ry("deckId")), (laneD, dy), (decks.r, dy)], "many1", "one"))

    laneR = 1215
    to_cards = [([(dc.r, dc.ry("cardId")), (laneR, dc.ry("cardId")), (laneR, cards.ry("cardId")), (cards.r, cards.ry("cardId"))], "many0", "one")]

    return svg(1260, 680, [users, profiles, mh, decks, cards, dc],
               [(BLUE, to_users), (TEAL, to_decks), (AMBER, to_cards)], legend_svg(80, 640))

def fig17():
    ps = Table("player_states", 80, 40, [("playerStateId", V, "PK"), ("gameId", V, "FK"), ("playerUid", V, "FK"),
                                         ("deckId", V, ""), ("damageCount", I, ""), ("handCount", I, ""),
                                         ("hasRideDeck", B, ""), ("vanguardCardId", V, "FK")])
    dz = Table("damage_zone", 80, 350, [("damageId", V, "PK"), ("gameId", V, "FK"), ("playerUid", V, "FK"),
                                        ("cardId", V, "FK"), ("position", I, ""), ("revealedAt", T, "")])
    dr = Table("drop_zone", 80, 604, [("dropId", V, "PK"), ("gameId", V, "FK"), ("playerUid", V, "FK"),
                                      ("cardId", V, "FK"), ("position", I, "")])
    gs = Table("game_states", 500, 40, [("gameId", V, "PK"), ("roomId", V, ""), ("activePlayerUid", V, ""),
                                        ("turnNumber", I, ""), ("currentPhase", V, ""), ("startedAt", T, "")])
    cards = Table("cards", 500, 464, [("cardId", V, "PK"), ("name", V, ""), ("grade", I, ""), ("power", I, ""),
                                      ("critical", I, ""), ("skillType", V, ""), ("triggerType", V, ""),
                                      ("nation", V, ""), ("clan", V, ""), ("imageUrl", V, "")])
    fu = Table("field_units", 920, 40, [("unitId", V, "PK"), ("gameId", V, "FK"), ("ownerUid", V, "FK"),
                                        ("cardId", V, "FK"), ("zone", V, ""), ("isRested", B, ""),
                                        ("isAttacking", B, ""), ("powerBonus", I, "")])
    ol = Table("order_log", 920, 350, [("orderId", V, "PK"), ("gameId", V, "FK"), ("playerUid", V, "FK"),
                                       ("cardId", V, "FK"), ("turnNumber", I, ""), ("playedAt", T, "")])
    gz = Table("g_zone", 920, 604, [("gZoneId", V, "PK"), ("gameId", V, "FK"), ("playerUid", V, "FK"),
                                    ("cardId", V, "FK"), ("isFaceUp", B, "")])

    gy, cy = gs.ry("gameId"), cards.ry("cardId")
    to_game, to_cards = [], []
    for t in (ps, dz, dr):  # left column: game lane next to the tables, card lane next to the centre
        to_game.append(([(t.r, t.ry("gameId")), (410, t.ry("gameId")), (410, gy), (gs.l, gy)],
                        "many1" if t is ps else "many0", "one"))
        f = "vanguardCardId" if t is ps else "cardId"
        to_cards.append(([(t.r, t.ry(f)), (450, t.ry(f)), (450, cy), (cards.l, cy)], "many0", "one"))
    for t in (fu, ol, gz):
        to_game.append(([(t.l, t.ry("gameId")), (880, t.ry("gameId")), (880, gy), (gs.r, gy)], "many0", "one"))
        to_cards.append(([(t.l, t.ry("cardId")), (840, t.ry("cardId")), (840, cy), (cards.r, cy)], "many0", "one"))

    return svg(1290, 870, [ps, dz, dr, gs, cards, fu, ol, gz],
               [(BLUE, to_game), (AMBER, to_cards)],
               legend_svg(80, 832, "playerUid / ownerUid reference users.uid (Figure 16)"))

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
for name, make in (("Figure 16 - ERD Before Joining Match", fig16), ("Figure 17 - ERD During the Match", fig17)):
    with open(os.path.join(out, name + ".svg"), "w", encoding="utf8") as f:
        f.write(make())
