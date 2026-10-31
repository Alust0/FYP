# Running AR Cardgame Sim on another computer

The game is built for **Universal Windows Platform (UWP)**, not as a normal Windows `.exe`.
Vuforia doesn't support standalone Windows builds, so UWP is the only way it runs outside the Unity Editor.

So you can't copy the build folder to another PC and double-click it.
You make an **app package** (`.msix`), copy that, and **install** it.

---

## 1. Build from Unity

1. Go to **File → Build Profiles** and choose **Universal Windows Platform**. Set the architecture to **x64**.
2. Under **Project Settings → Player → UWP tab → Publishing Settings → Capabilities**, check these are ticked:
   - **InternetClient** and **InternetClientServer** (Firestore, online rooms, Cloud Save)
   - **WebCam** (card tracking)
   - **Microphone** (voice commands; without it, voice works in the Editor but not in a build)
3. In **Player → Resolution and Presentation**, check that **Run In Background** is on. Without it, the game freezes when the window loses focus.
4. Build into the `build/` folder in the project root.
   This produces a Visual Studio solution: `build/AR Cardgame Sim.sln`.

## 2. Make the app package in Visual Studio

1. Open `build/AR Cardgame Sim.sln`.
2. Set the toolbar to **Release** and **x64**.
3. Go to **Project → Publish → Create App Packages…** and answer each page like this:

| Page | Choose |
|---|---|
| Distribution method | **Sideloading**. **Untick "Enable automatic updates"**. |
| Signing method | **Yes, use the current certificate** (CN=DefaultCompany). Leave the timestamp empty, SHA256. |
| Select and configure packages | Tick **x64 → Release (x64)** only. Untick x86, ARM and ARM64. Set app bundle to **Never**. Keep **Automatically increment** ticked. Untick public symbol files. |

4. Press **Create**. The package is saved to:

```
build/AppPackages/AR Cardgame Sim/AR Cardgame Sim_<version>_x64_Test/
```

Before packaging, check that `build/AR Cardgame Sim/Package.appxmanifest` lists
`<DeviceCapability Name="webcam" />` and `<DeviceCapability Name="microphone" />`.
If you build into a folder that already has a build in it, Unity may keep an old manifest, so add a missing line by hand.

## 3. Install on the other computer

**Requirements:** Windows 10 (19041 or later) or Windows 11, an **x64** CPU (not ARM), a webcam and a microphone.

1. Copy the whole `AR Cardgame Sim_<version>_x64_Test` folder to the other PC, e.g. on a USB stick.
   You can skip the `.appxsym` file if there is one. It's debug symbols only.
2. On that PC, go to **Settings → System → For developers** and turn **Developer Mode** on.
3. Open PowerShell inside the copied folder and run:

   ```
   powershell -ExecutionPolicy Bypass -File .\Install.ps1
   ```

   It asks for admin once, trusts the test certificate, installs the VCLibs dependency, then installs the game.
4. Start **AR Cardgame Sim** from the **Start menu**. There's no `.exe` to click.
5. On first launch, **allow camera and microphone access**.

**If `Install.ps1` fails, install by hand:**
1. Double-click the `.cer` file → **Install Certificate** → **Local Machine** → **Place all certificates in the following store** → **Trusted People**.
2. If Windows says a dependency is missing, install `Dependencies/x64/Microsoft.VCLibs.x64.14.00.appx` first.
3. Double-click the `.msix` → **Install**.

## 4. Updating to a newer build

- Each package needs a **higher version** than the one installed. "Automatically increment" handles this.
- If the install says the same version is already installed, uninstall the old one first:
  **Settings → Apps → Installed apps → AR Cardgame Sim → Uninstall**.
- Account data and decks are kept in the cloud (Unity Cloud Save / Firestore), so reinstalling doesn't lose them.
  Board calibration is stored per device, so do it again on a new PC.

## Known limitations

- **Don't minimise the window** during play. Windows suspends a minimised UWP app after a few seconds. Leaving it open behind other windows is fine.
- The certificate is a self-signed **test** certificate, valid until 15/10/2027. It's only for sideloading, not for the Microsoft Store.
- Vuforia's own UWP camera code can't choose between cameras.
  The build feeds Vuforia frames from the camera picked in the lobby instead (`BuildWebcamDriver.cs`, using `Plugins/WSA/x64/UnityDriver.dll`). If that fails, it falls back to Vuforia's default camera.
