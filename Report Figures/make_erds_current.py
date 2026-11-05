"""ERDs of the data the built system stores (Unity Authentication, Cloud Save, Realtime Database, Firestore).
 python make_erds_current.py "<output folder>" writes the SVGs; render them to PNG in a browser. """
import sys, os
from xml.sax.saxutils import escape

W, HH, RH = 300, 50, 28
BG = "#ffffff"
AUTH, SAVE, RTDB, FS = "#5b4b8a", "#2c7a6e", "#b3541e", "#2f4f7f"
S, I, B, T, L, M, J = "string", "integer", "boolean", "timestamp", "list", "map", "json"

class Table:
    def __init__(self, name, sub, color, x, y, rows, nested=False):
        self.name, self.sub, self.color, self.x, self.y, self.rows, self.nested = name, sub, color, x, y, rows, nested
        self.h = HH + RH * len(rows)
    l = property(lambda s: s.x)
    r = property(lambda s: s.x + W)
    hy = property(lambda s: s.y + HH / 2)
    def ry(self, field):
        for i, row in enumerate(self.rows):
            if row[0] == field:
                return self.y + HH + i * RH + RH / 2
        raise KeyError(field)

def badge(x, cy, key):
    fill, ink = ("#f3c64b", "#5a4300") if key == "PK" else ("#dce7f7", "#1e4a8a")
    return (f'<rect x="{x}" y="{cy-8}" width="21" height="16" rx="3" fill="{fill}"/>'
            f'<text x="{x+10.5}" y="{cy}" class="badge" fill="{ink}">{key}</text>')

def table_svg(t):
    o, x, y = [], t.x, t.y
    o.append(f'<g filter="url(#sh)"><rect x="{x}" y="{y}" width="{W}" height="{t.h}" rx="7" fill="#fff"/></g>')
    o.append(f'<clipPath id="c_{id(t)}"><rect x="{x}" y="{y}" width="{W}" height="{t.h}" rx="7"/></clipPath>')
    o.append(f'<g clip-path="url(#c_{id(t)})">')
    o.append(f'<rect x="{x}" y="{y}" width="{W}" height="{HH}" fill="{t.color}" opacity="{0.82 if t.nested else 1}"/>')
    for i in range(len(t.rows)):
        ry = y + HH + i * RH
        if i % 2 == 1:
            o.append(f'<rect x="{x}" y="{ry}" width="{W}" height="{RH}" fill="#f5f7fb"/>')
        if i:
            o.append(f'<line x1="{x}" y1="{ry}" x2="{x+W}" y2="{ry}" stroke="#e6eaf0"/>')
    o.append('</g>')
    dash = ' stroke-dasharray="6 4"' if t.nested else ""
    o.append(f'<rect x="{x}" y="{y}" width="{W}" height="{t.h}" rx="7" fill="none" stroke="{t.color if t.nested else "#b9c3d1"}" stroke-width="{1.6 if t.nested else 1.2}"{dash}/>')
    o.append(f'<text x="{x+14}" y="{y+19}" class="th">{escape(t.name)}</text>')
    o.append(f'<text x="{x+14}" y="{y+38}" class="sub">{escape(t.sub)}</text>')
    for i, (name, typ, key) in enumerate(t.rows):
        cy = y + HH + i * RH + RH / 2
        if key == "PK FK":
            o.append(badge(x + 8, cy, "PK") + badge(x + 31, cy, "FK"))
        elif key:
            o.append(badge(x + 10, cy, key))
        o.append(f'<text x="{x+58}" y="{cy}" class="{"fn pk" if "PK" in key else "fn"}">{escape(name)}</text>')
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
    return d + f" L{pts[-1][0]},{pts[-1][1]}"

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

def link(child, parent, lane, cm, pm):
    """child/parent = (x, y) on a table edge; the line runs out to x = lane, along it, then in."""
    return ([child, (lane, child[1]), (lane, parent[1]), parent], cm, pm)

def edges_svg(groups):
    # Later groups get a white under-stroke so they read as passing over earlier ones
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

STORES = {AUTH: "Unity Authentication", SAVE: "Unity Cloud Save", RTDB: "Realtime Database", FS: "Firestore"}

def legend_svg(x, y, width, notes, stores=(AUTH, SAVE, RTDB, FS)):
    """Wraps its items into rows inside a box of the given width."""
    col = "#4b5563"
    items = []
    for c, label in ((c, STORES[c]) for c in stores):
        items.append((28 + len(label) * 7.4, lambda cx, cy, c=c, label=label:
                      f'<rect x="{cx}" y="{cy-8}" width="20" height="16" rx="3" fill="{c}"/><text x="{cx+28}" y="{cy}" class="lg">{label}</text>'))
    items.append((28 + 26 * 7.2, lambda cx, cy:
                  f'<rect x="{cx}" y="{cy-8}" width="20" height="16" rx="3" fill="#fff" stroke="{col}" stroke-width="1.4" stroke-dasharray="4 3"/>'
                  f'<text x="{cx+28}" y="{cy}" class="lg">Nested in its parent\'s data</text>'))
    items.append(None)
    for key, label in (("PK", "Key / path id"), ("FK", "Refers to another entity")):
        items.append((30 + len(label) * 7.3, lambda cx, cy, key=key, label=label: badge(cx, cy, key) + f'<text x="{cx+29}" y="{cy}" class="lg">{label}</text>'))
    for kind, label in (("one", "Exactly one"), ("zero1", "Zero or one"), ("many1", "One or many"), ("many0", "Zero or many")):
        items.append((54 + len(label) * 7.4, lambda cx, cy, kind=kind, label=label:
                      f'<line x1="{cx}" y1="{cy}" x2="{cx+40}" y2="{cy}" stroke="{col}" stroke-width="1.6"/>'
                      + marker(cx + 40, cy, -1, kind, col) + f'<text x="{cx+50}" y="{cy}" class="lg">{label}</text>'))
    o, cx, cy = [], x + 18, y + 26
    for item in items:
        if item is None or cx + item[0] > x + width - 10:
            cx, cy = x + 18, cy + 30
            if item is None: continue
        o.append(item[1](cx, cy))
        cx += item[0] + 26
    for note in notes:
        cy += 26
        o.append(f'<text x="{x+18}" y="{cy}" class="lg note">{escape(note)}</text>')
    h = cy - y + 24
    o.insert(0, f'<rect x="{x}" y="{y}" width="{width}" height="{h}" rx="8" fill="#fafbfc" stroke="#e2e6ec"/>')
    return "\n".join(o), y + h

def svg(width, tables, groups, legend):
    lines, marks = edges_svg(groups)
    legend_body, legend_bottom = legend
    height = max([t.y + t.h for t in tables] + [legend_bottom]) + 36
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height:.0f}" viewBox="0 0 {width} {height:.0f}">
<defs>
<filter id="sh" x="-10%" y="-10%" width="120%" height="125%"><feDropShadow dx="0" dy="2" stdDeviation="3" flood-color="#1f2a44" flood-opacity="0.14"/></filter>
<style>
text {{ font-family: "Segoe UI", Arial, sans-serif; dominant-baseline: central; }}
.th {{ font-size: 15.5px; font-weight: 600; fill: #fff; letter-spacing: .2px; }}
.sub {{ font-family: Consolas, "Courier New", monospace; font-size: 11.5px; fill: #fff; opacity: .85; }}
.fn {{ font-size: 14px; fill: #1f2937; }}
.pk {{ font-weight: 700; }}
.ty {{ font-family: Consolas, "Courier New", monospace; font-size: 12.5px; fill: #6b7280; text-anchor: end; }}
.badge {{ font-size: 9px; font-weight: 700; text-anchor: middle; }}
.lg {{ font-size: 13px; fill: #374151; }}
.note {{ fill: #6b7280; font-style: italic; }}
</style>
</defs>
<rect width="100%" height="100%" fill="{BG}"/>
{lines}
{"".join(table_svg(t) for t in tables)}
{marks}
{legend_body}
</svg>'''

def fig16():
    c1, c2, c3 = 60, 490, 920
    emails = Table("emails", "RTDB · emails/{emailId}", RTDB, c1, 40, [
        ("emailId", S, "PK"), ("username", S, ""), ("playerId", S, "FK"), ("createdMs", T, "")])
    sessions = Table("sessions", "RTDB · sessions/{playerId}", RTDB, c1, 242, [
        ("playerId", S, "PK FK"), ("clientId", S, ""), ("device", S, ""), ("beatMs", T, "")])
    match = Table("matches", "Firestore · matches/{auto id}", FS, c1, 444, [
        ("docId", S, "PK"), ("playerId", S, "FK"), ("player", S, ""), ("opponent", S, ""), ("mode", S, ""),
        ("roomCode", S, ""), ("seat", I, ""), ("deckId", S, "FK"), ("result", S, ""), ("winner", S, ""),
        ("reason", S, ""), ("damageDealt", I, ""), ("damageTaken", I, ""), ("turns", I, ""), ("time", S, ""),
        ("startedMs", T, ""), ("endedMs", T, ""), ("gameVersion", S, ""), ("log", S, ""), ("logCut", B, "")])
    player = Table("player", "Unity Authentication", AUTH, c2, 40, [("playerId", S, "PK"), ("username", S, "")])
    save = Table("cloud_save", "Cloud Save · player data", SAVE, c2, 196, [
        ("playerId", S, "PK FK"), ("email", S, ""), ("decks", J, "")])
    deck = Table("deck", "Cloud Save · inside decks", SAVE, c2, 380, [
        ("id", S, "PK"), ("deckName", S, ""), ("cards", L, ""), ("rideDeck", L, ""), ("extras", L, ""),
        ("deckSleeve", S, ""), ("rideDeckSleeve", S, "")], nested=True)
    card = Table("card", "Firestore · sets/{setId}/cards/{cardDocId}", FS, c3, 40, [
        ("cardDocId", S, "PK"), ("setId", S, ""), ("number", S, ""), ("name / nameEn", S, ""), ("type / typeEn", S, ""),
        ("grade", I, ""), ("power", I, ""), ("shield", I, ""), ("critical", I, ""), ("triggerType", S, ""),
        ("nation / nationEn", S, ""), ("clan / clanEn", S, ""), ("skill / skillEn", S, ""), ("effect / effectEn", S, ""),
        ("rarity", S, ""), ("productName", S, "")])
    entry = Table("deck_entry", "inside deck.cards / rideDeck / extras", SAVE, c3, 590, [
        ("cardNumber", S, "FK"), ("count", I, "")], nested=True)

    pid = (player.l, player.ry("playerId"))
    to_player = [link((t.r, t.ry("playerId")), pid, 400, cm, "one")
                 for t, cm in ((emails, "one"), (sessions, "zero1"), (match, "many0"))]
    to_player.append(link((save.r, save.ry("playerId")), (player.r, player.ry("playerId")), 855, "one", "one"))
    to_deck = [link((match.r, match.ry("deckId")), (deck.l, deck.ry("id")), 445, "many0", "zero1"),
               link((deck.r, deck.hy), (save.r, save.ry("decks")), 855, "many0", "one")]
    to_entry = [link((entry.l, entry.hy), (deck.r, deck.ry(f)), 855, "many0", "one") for f in ("cards", "rideDeck", "extras")]
    to_card = [link((entry.r, entry.ry("cardNumber")), (card.r, card.ry("number")), 1260, "many0", "one")]

    legend = legend_svg(c2, 760, 730, [
        "emailId = SHA-256 of the lower-case email (one account per email).",
        "cardDocId = card number in upper case with / → -; deck_entry.cardNumber and",
        " matches.deckId are matched by value, as NoSQL stores have no foreign keys.",
        "Each deck's cards, rideDeck and extras lists hold deck_entry items."])
    return svg(1300, [emails, sessions, match, player, save, deck, card, entry],
               [(AUTH, to_player), (SAVE, to_deck + to_entry), (FS, to_card)], legend)

def fig17():
    c1, c2, c3 = 60, 490, 920
    member = Table("room_member", "rooms/{code}/host | guest", RTDB, c1, 40, [
        ("role", S, "PK"), ("id", S, "FK"), ("name", S, ""), ("beatMs", T, "")], nested=True)
    turn = Table("turn", "rooms/{code}/turn", RTDB, c1, 242, [
        ("player", I, ""), ("number", I, ""), ("phase", I, "")], nested=True)
    undo = Table("undo", "rooms/{code}/undo", RTDB, c1, 416, [
        ("at", T, ""), ("seat", I, ""), ("sinceMs", T, ""), ("player", I, ""), ("number", I, ""), ("phase", I, "")], nested=True)
    video = Table("video_frame", "RTDB · rooms/{code}_video{seat}", RTDB, c1, 674, [
        ("code", S, "PK FK"), ("seat", I, "PK"), ("jpg", S, ""), ("ms", T, "")])
    room = Table("room", "RTDB · rooms/{code}", RTDB, c2, 40, [
        ("code", S, "PK"), ("status", S, ""), ("isPublic", B, ""), ("passwordHash", S, ""), ("createdMs", T, ""),
        ("deck", S, ""), ("cover", S, "FK"), ("gameVersion", S, ""), ("cardsVersion", I, ""),
        ("timerMinutes", I, ""), ("noSkills", B, ""), ("video", M, "")])
    seat = Table("seat", "rooms/{code}/seat0 | seat1", RTDB, c2, 476, [
        ("seatIndex", I, "PK"), ("cards", L, ""), ("counts", M, ""), ("attackTarget", S, ""), ("pick / pickN", S, ""),
        ("said / saidN", S, ""), ("skill / skillText", S, ""), ("skillN", I, ""), ("oppStep", S, ""),
        ("moves", S, ""), ("turned", B, "")], nested=True)
    card = Table("card", "Firestore · via CardCache.json", FS, c3, 40, [
        ("cardDocId", S, "PK"), ("number", S, ""), ("name / nameEn", S, ""), ("grade", I, ""), ("power", I, ""),
        ("shield", I, ""), ("critical", I, ""), ("triggerType", S, ""), ("effect / effectEn", S, "")])
    scard = Table("seat_card", "inside seat.cards[]", RTDB, c3, 420, [
        ("id", S, "PK"), ("number", S, "FK"), ("zone", S, ""), ("rest", B, ""), ("faceDown", B, "")], nested=True)
    count = Table("zone_count", "inside seat.counts{}", RTDB, c3, 680, [
        ("zone", S, "PK"), ("count", I, "")], nested=True)

    code = (room.l, room.ry("code"))
    to_room = [link((t.r, t.hy), code, 420, cm, "one")
               for t, cm in ((member, "many1"), (turn, "zero1"), (undo, "zero1"))]
    to_room.append(link((video.r, video.ry("code")), code, 420, "many0", "one"))
    to_room.append(link((seat.l, seat.hy), code, 420, "many0", "one"))
    to_seat = [link((scard.l, scard.hy), (seat.r, seat.ry("cards")), 855, "many0", "one"),
               link((count.l, count.hy), (seat.r, seat.ry("counts")), 855, "many0", "one")]
    to_card = [link((room.r, room.ry("cover")), (card.l, card.ry("number")), 855, "many0", "zero1"),
               link((scard.r, scard.ry("number")), (card.r, card.ry("number")), 1260, "many0", "one")]

    legend = legend_svg(c1, 900, 1160, [
        "room_member.id is the PC's client id, the same as sessions.clientId (Figure 16); a finished match is saved to matches (Figure 16).",
        "seat_card holds only cards the camera sees; zone_count holds the untracked ones (duplicates, soul, deck, damage)."],
        stores=(RTDB, FS))
    return svg(1300, [member, turn, undo, video, room, seat, card, scard, count],
               [(RTDB, to_room + to_seat), (FS, to_card)], legend)

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
for name, make in (("Figure 16 - ERD Before Joining Match (as built)", fig16), ("Figure 17 - ERD During the Match (as built)", fig17)):
    with open(os.path.join(out, name + ".svg"), "w", encoding="utf8") as f:
        f.write(make())
