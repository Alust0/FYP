# Database rules test (2026-11-18 20:40)

| # | Case | Expected | Got | Result |
|---|---|---|---|---|
| 1.1 | Valid room written to rooms/CODE (control) | allowed | allowed (200) | PASS |
| 1.2 | Unknown field seat2 added to a room | denied | denied (401) | PASS |
| 1.3 | Room status set to a value outside waiting/playing/closed | denied | denied (401) | PASS |
| 1.4 | Room written under a key that isn't a room code | denied | denied (401) | PASS |
| 1.5 | Video picture (jpg) written on a non-video room node | denied | denied (401) | PASS |
| 1.6 | Whole sessions list read | denied | denied (401) | PASS |
| 1.7 | Whole rooms node overwritten in one request | denied | denied (401) | PASS |
| 2.1 | Private room's list entry (no password data) written | allowed | allowed (200) | PASS |
| 2.2 | passwordHash stored on the public list entry | denied | denied (401) | PASS |
| 2.3 | Host creates privateRooms/<sha256(code:password)> | allowed | allowed (200) | PASS |
| 2.4 | Whole privateRooms list read | denied | denied (401) | PASS |
| 2.5 | privateRooms keys listed (shallow) | denied | denied (401) | PASS |
| 2.6 | Join with the right password finds the room | allowed | allowed (200) | PASS |
| 2.7 | Join with a wrong password finds nothing | allowed | allowed (200) | PASS |
| 2.8 | Private room written under a key that isn't a 64-hex hash | denied | denied (401) | PASS |

15/15 passed
