# AR Cardgame Sim

An AR referee for physical *Cardfight!! Vanguard* cards, made as a final year project.
A webcam over the playmat recognises cards with Vuforia, and a rules engine follows the game
from how the cards move. Voice keywords or on-screen buttons fill in what the camera can't see.

Built with Unity **6000.3.10f1** (URP, UWP build target). Build and install steps are in
[Assets/Scipts/BUILD_README.md](Assets/Scipts/BUILD_README.md).

## Online code is not in this repository

The `Assets/Scipts/Online/` folder is **left out on purpose** and isn't in the commit history.
It holds the two-PC online mode and its backend access:

- Lobby, matchmaking and private rooms (`LobbyController`)
- Match session and seat sync between the two PCs (`MatchSession`, `OnlineMatch`)
- The opponent's camera video feed (`CameraFeedLink`)
- One login per account (`AccountLock`)
- Realtime Database access and saving finished matches (`RtdbRest`, `MatchArchive`)
- A debug card placer (`DebugCardPlacer`)

**Because many other scripts use these classes, the project won't compile from a fresh clone.**
To build it, put the `Online` folder back at `Assets/Scipts/Online/`. It's available from the
author on request.

The Firebase and EmailJS config files in `Assets/StreamingAssets/` are also not included, for the
same reason.
