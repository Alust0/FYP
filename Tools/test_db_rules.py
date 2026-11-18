# Tests the Realtime Database rules against the live database: direct REST access and private room access
import hashlib, json, sys, time, urllib.request, urllib.error
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BASE = json.loads((ROOT / "Assets/StreamingAssets/google-services.json").read_text())["project_info"]["firebase_url"].rstrip("/")
CODE = "ZZZZ9"  # throwaway room code (valid characters, never handed out by the lobby's random codes in practice)
PASSWORD = "rules-test"
results = []


def req(method, path, body=None, query=""):
    # One REST call; returns (status, parsed body)
    url = f"{BASE}/{path}.json" + (f"?{query}" if query else "")
    data = None if body is None else json.dumps(body).encode()
    r = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(r, timeout=15) as resp:
            return resp.status, json.loads(resp.read() or b"null")
    except urllib.error.HTTPError as e:
        text = e.read().decode(errors="replace")
        try:
            return e.code, json.loads(text)
        except ValueError:
            return e.code, text


def check(test, name, expect_allowed, status, extra_ok=True):
    # Records one case: allowed = 2xx, denied = 401/403
    allowed = 200 <= status < 300
    passed = (allowed == expect_allowed) and extra_ok
    results.append((test, name, "allowed" if expect_allowed else "denied", f"{'allowed' if allowed else 'denied'} ({status})", passed))
    print(f"[{'PASS' if passed else 'FAIL'}] {test} {name}: expected {'allowed' if expect_allowed else 'denied'}, got {status}")
    return passed


def room(status="waiting", public=True):
    now = int(time.time() * 1000)
    return {"status": status, "isPublic": public, "createdMs": now, "host": {"name": "RulesTest", "beatMs": now}}


phash = hashlib.sha256(f"{CODE}:{PASSWORD}".encode()).hexdigest()
wrong = hashlib.sha256(f"{CODE}:wrong-password".encode()).hexdigest()
room_path, private_path = f"rooms/{CODE}", f"privateRooms/{phash}"

if req("GET", room_path)[1] is not None:
    sys.exit(f"A real room {CODE} exists right now; try again later.")

try:
    print(f"Database: {BASE}\n\n== Test 1: direct REST access ==")
    s, _ = req("PUT", room_path, room())
    if not check("1.1", "Valid room written to rooms/CODE (control)", True, s):
        sys.exit("Control write refused: requests aren't reaching the database as expected; stopping.")
    s, _ = req("PATCH", room_path, {"seat2": {"x": 1}})
    rules_live = check("1.2", "Unknown field seat2 added to a room", False, s)
    s, _ = req("PATCH", room_path, {"status": "hacked"})
    check("1.3", "Room status set to a value outside waiting/playing/closed", False, s)
    s, _ = req("PUT", "rooms/abc!x", room())
    check("1.4", "Room written under a key that isn't a room code", False, s)
    s, _ = req("PATCH", room_path, {"jpg": "AAAA"})
    check("1.5", "Video picture (jpg) written on a non-video room node", False, s)
    s, _ = req("GET", "sessions")
    check("1.6", "Whole sessions list read", False, s)
    if rules_live:
        # PUT of the list just read: if the rules were wrong this would only rewrite the same data, never wipe it
        _, snapshot = req("GET", "rooms")
        s, _ = req("PUT", "rooms", snapshot if snapshot else {CODE: room()})
        check("1.7", "Whole rooms node overwritten in one request", False, s)
    else:
        print("[SKIP] 1.7 whole-rooms write: 1.2 shows the new rules aren't published, so this could change real rooms")

    print("\n== Test 2: private room access ==")
    s, _ = req("PUT", room_path, room(public=False))
    check("2.1", "Private room's list entry (no password data) written", True, s)
    s, _ = req("PATCH", room_path, {"passwordHash": phash})
    check("2.2", "passwordHash stored on the public list entry", False, s)
    s, _ = req("PUT", private_path, room(public=False))
    check("2.3", "Host creates privateRooms/<sha256(code:password)>", True, s)
    s, _ = req("GET", "privateRooms")
    check("2.4", "Whole privateRooms list read", False, s)
    s, _ = req("GET", "privateRooms", query="shallow=true")
    check("2.5", "privateRooms keys listed (shallow)", False, s)
    s, body = req("GET", private_path)
    check("2.6", "Join with the right password finds the room", True, s, isinstance(body, dict) and body.get("status") == "waiting")
    s, body = req("GET", f"privateRooms/{wrong}")
    # A wrong password leads to an empty path: 200 with null (the lobby treats it as not found)
    check("2.7", "Join with a wrong password finds nothing", True, s, body is None)
    s, _ = req("PUT", "privateRooms/not-a-hash", room(public=False))
    check("2.8", "Private room written under a key that isn't a 64-hex hash", False, s)
finally:
    req("DELETE", room_path)
    req("DELETE", private_path)
    print("\nCleaned up test nodes.")

passed = sum(r[4] for r in results)
print(f"\n{passed}/{len(results)} passed")
out = ROOT / "Tools/test_db_rules_results.md"
lines = [f"# Database rules test ({time.strftime('%Y-%m-%d %H:%M')})", "", "| # | Case | Expected | Got | Result |", "|---|---|---|---|---|"]
lines += [f"| {t} | {n} | {e} | {g} | {'PASS' if p else 'FAIL'} |" for t, n, e, g, p in results]
lines += ["", f"{passed}/{len(results)} passed"]
out.write_text("\n".join(lines) + "\n")
print(f"Results written to {out}")
