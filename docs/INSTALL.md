# Installing the Enter the Gungeon 4:3 patch

This patch makes Enter the Gungeon fill a 4:3 screen, such as the Retroid Pocket Nova's, with pixel-perfect scaling and the HUD at the screen edges. Without it the game draws a 16:9 box with black bars above and below. You patch **your own copy** of the game's `Assembly-CSharp.dll`; nothing from the game is downloaded or shared. This is an unofficial fan tool, not affiliated with or endorsed by Dodge Roll or Devolver Digital.

## Quick summary

- The patched file **replaces** `Assembly-CSharp.dll` in the game's `Managed` folder.
- **Always keep a backup** of the original (`Assembly-CSharp.dll.bak`).
- **Patch the file from the install you will play.** The Windows, Mac and Linux versions of the game have different `Assembly-CSharp.dll` files. Handhelds (DroidDeck, GameNative) run the Windows version. A Mac file copied onto a handheld stops at the first loading screen. See [Troubleshooting](#6-troubleshooting).
- **Re-patch after every game update.** Steam updates overwrite the file, and the patcher refuses a file that is already patched, so always patch the fresh original.

## 1. Get the patched dll

Pick one. All three produce the same patch.

### a) Web patcher (any device, including your phone)

1. Open the web patcher: <https://jevonlipsey.github.io/gungeon-43aspect/>
2. Drop in (or pick) your `Assembly-CSharp.dll`.
3. Wait for it to patch and verify. Everything runs in your browser; the file is not uploaded. The page also tells you which version of the game (Windows, Mac or Linux) the file came from.
4. Download the patched `Assembly-CSharp.dll` and put it in the `Managed` folder (see [section 2](#2-where-the-managed-folder-is)).

The page reports something like "N checks passed, K skipped". The browser runs every check that doesn't need the game's Unity libraries; the skipped ones need them. The desktop patcher below runs the full set when you point it at the `Managed` folder.

### b) Desktop patcher (Windows, macOS, Linux)

1. Download the zip for your system from the Releases page: <https://github.com/jevonlipsey/gungeon-43aspect/releases>

| System | Asset |
|---|---|
| Windows | `gungeon-43aspect-<version>-windows-x64.zip` |
| macOS, Apple silicon | `gungeon-43aspect-<version>-macos-arm64.zip` |
| macOS, Intel | `gungeon-43aspect-<version>-macos-x64.zip` |
| Linux / Steam Deck | `gungeon-43aspect-<version>-linux-x64.zip` |

2. Unzip it. Each zip also has `patch.py`, this guide and a `README-FIRST.txt`.

**Windows: drag and drop.** Drag `Assembly-CSharp.dll` from the game's `EtG_Data\Managed` folder onto `patcher.exe`. Dragging the whole `Managed` folder works too and runs the full verification. A window shows the progress and waits for Enter when it is done. It writes `Assembly-CSharp-43.dll` next to the original and never changes the original itself. Then, in the `Managed` folder:

1. Rename `Assembly-CSharp.dll` to `Assembly-CSharp.dll.bak` (your backup).
2. Rename `Assembly-CSharp-43.dll` to `Assembly-CSharp.dll`.

If the run ends with an explanation instead (already patched, not Enter the Gungeon's dll, or verification failed), nothing was written; see [Troubleshooting](#6-troubleshooting). If Windows SmartScreen warns about an unknown app, choose **More info**, then **Run anyway**.

**Terminal (all systems).** Open a terminal or command prompt in the unzipped folder and run the patcher against the `Managed` folder:

| System | Command |
|---|---|
| Windows | `patcher.exe "C:\Program Files (x86)\Steam\steamapps\common\Enter the Gungeon\EtG_Data\Managed"` |
| macOS | `./patcher "$HOME/Library/Application Support/Steam/steamapps/common/Enter the Gungeon/EtG_OSX.app/Contents/Resources/Data/Managed"` |
| Linux / Steam Deck | `./patcher "$HOME/.local/share/Steam/steamapps/common/Enter the Gungeon/EtG_Data/Managed"` |

If macOS says the file cannot be opened, run `xattr -d com.apple.quarantine ./patcher` once. On Linux run `chmod +x patcher` if needed.

With the `Managed` folder as the argument, the patcher runs the **full verification** and writes `Assembly-CSharp-43.dll` next to the original only if every check passed. Back up `Assembly-CSharp.dll` as `Assembly-CSharp.dll.bak`, then rename the `-43` file to `Assembly-CSharp.dll`.

### c) `python3 patch.py` (easiest on a desktop)

From the release zip (or a clone of this repo):

```
python3 patch.py "/path/to/EtG_Data/Managed"
```

This runs the patcher, verifies the result, **backs up the original to `Assembly-CSharp.dll.bak`** (an existing backup is never overwritten) and then installs the patched file. Add `--no-install` to only write `Assembly-CSharp-43.dll` and leave the original alone.

## 2. Where the Managed folder is

| Platform | Folder |
|---|---|
| DroidDeck (internal proot environment) | `/root/.local/share/Steam/steamapps/common/Enter the Gungeon/EtG_Data/Managed/` |
| Windows (Steam) | `C:\Program Files (x86)\Steam\steamapps\common\Enter the Gungeon\EtG_Data\Managed\` |
| Linux / Steam Deck | `~/.local/share/Steam/steamapps/common/Enter the Gungeon/EtG_Data/Managed/` |
| macOS | `~/Library/Application Support/Steam/steamapps/common/Enter the Gungeon/EtG_OSX.app/Contents/Resources/Data/Managed/` |

The easiest way to open it: in Steam, right-click Enter the Gungeon, then **Manage**, then **Browse local files**.

**macOS is different:** the game is an app bundle. Right-click `EtG_OSX.app` and choose **Show Package Contents**, then go to `Contents/Resources/Data/Managed/`.

**Linux and Steam Deck:** Steam can run either the native Linux version or the Windows version through Proton. Both keep the file at the same path. Patch it after the version you play is installed: switching to or from Proton makes Steam download the other version's files over it.

## 3. Android handhelds

Both apps below run the Windows game inside their own Linux/Wine environment, so the game's files are usually **not** in a normal Android folder.

The easiest route on Android is the web patcher on the handheld itself:

1. Copy `Assembly-CSharp.dll` out of the game's `EtG_Data/Managed/` folder to somewhere your browser can pick it (for example `Download/`). Keep this copy as your backup.
2. Open the web patcher (<https://jevonlipsey.github.io/gungeon-43aspect/>) in the handheld's browser and pick that file. It should report the **Windows** version.
3. Download the patched file.
4. Copy it back over the original in `EtG_Data/Managed/`.

Or patch on a PC and copy the patched file over, but patch the **device's** copy of the file (or a Windows install's), never the Mac version's.

### DroidDeck

DroidDeck runs Valve's ARM64 Steam client inside an in-app proot Linux runtime, with Windows games running through Proton. See the [DroidDeck site](https://droiddeck.app/).

- Inside DroidDeck's Linux environment (its desktop-mode terminal or file manager), the file is at
  `/root/.local/share/Steam/steamapps/common/Enter the Gungeon/EtG_Data/Managed/Assembly-CSharp.dll`
- The game's log is inside the Proton prefix:
  `/root/.local/share/Steam/steamapps/compatdata/311690/pfx/drive_c/users/steamuser/AppData/LocalLow/Dodge Roll/Enter the Gungeon/output_log.txt`

Display settings (the settings cog): **Resolution** and **16:9 game fit**. See [Display settings](#4-display-settings) below. These names come from testing on a Retroid Pocket Nova; other DroidDeck versions may label them differently.

### GameNative

[GameNative](https://github.com/utkarshdalal/GameNative) runs games you own through Wine with Box64. It stores containers in the app's private data directory, and whether your version exposes them to a file manager varies by build (see [this discussion](https://github.com/utkarshdalal/GameNative/discussions/237)). **This patch has not been tested on GameNative.** The goal is the same as below: give the game a 4:3 screen at the panel's native size, with no 16:9 letterboxing.

## 4. Display settings

In the game, open **Options → Video**:

1. **Resolution:** your screen's native size (1280x960 on the Retroid Pocket Nova).
2. **Display:** Fullscreen.
3. **Scaling mode:** any of them works.
   - The two pixel-perfect modes give the sharpest picture: on 1280x960 the game renders at an exact 3x and fills 1278x960.
   - The uniform modes fill the screen too.
   - The title screen uses the vanilla vertical framing so its background art fits. In the pixel-perfect modes that shows as a 1080x810 picture with a thin border; in the uniform modes it fills the screen.

On a 4:3 handheld the container must hand the game a 4:3 surface, or bars will remain:

| Setting (DroidDeck) | Value |
|---|---|
| Resolution | `Match screen (1280x960)` on the Retroid Pocket Nova |
| 16:9 game fit | `Stretch` (off). `Keep proportions` letterboxes the game into a 16:9 box |

Both apply on the **next session**: fully exit and relaunch the container app **and** the game after changing them.

On a 16:9 or wider screen the patch does nothing, and the game looks exactly as it always did.

## 5. Checking it worked

- In the Breach and in rooms, the picture reaches all four edges of a 4:3 screen, and the hearts, keys and minimap sit in the screen corners.
- On the title screen, the background art fills the screen (with the uniform scaling modes).

Unity's log is the place to look if something goes wrong:

| Platform | Log |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\Dodge Roll\Enter the Gungeon\output_log.txt` |
| Wine / Proton | inside the prefix, `C:\users\steamuser\AppData\LocalLow\Dodge Roll\Enter the Gungeon\output_log.txt` |
| macOS | `~/Library/Logs/Unity/Player.log` |
| Linux (native) | `~/.config/unity3d/Dodge Roll/Enter the Gungeon/Player.log` |

## 6. Troubleshooting

| Problem | What to do |
|---|---|
| The game sits on the first loading screen forever | The `Assembly-CSharp.dll` belongs to a different version of the game, almost always a **Mac** file on a Windows install or handheld. The log shows `GeneratedSoundBanks\Mac\` and "Terminate sound engine". Restore the `.bak` and patch the file that came with this install. The patcher refuses a mismatch when you name the target (`--target`), and the web patcher shows which version a file is. |
| Black bars remain on a 4:3 screen | Almost always the container, not the game. Set the container's resolution to the panel's native size and turn 16:9 fit to Stretch (section 4), then fully relaunch both apps. Also check the game's own resolution is the panel's size. |
| Game will not start or shows a black screen | Restore `Assembly-CSharp.dll.bak` over `Assembly-CSharp.dll`. If the problem goes away, open an issue with your log (section 5). |
| "already patched" (exit code 3) | The file in the folder is already patched. Restore the original (`.bak`, or Steam **Verify integrity of game files**) and patch that. |
| "not Enter the Gungeon's dll" (exit code 4) | You picked the wrong file or folder. On macOS use the path inside `EtG_OSX.app` (section 2). |
| Verification fails | The game was probably updated and the patch no longer matches. Nothing is written when verification fails, so your install is safe. Open an issue. |
| It stopped working after a game update | Steam replaced the file. Patch the new original again. |

## 7. Uninstall

Copy `Assembly-CSharp.dll.bak` back over `Assembly-CSharp.dll`, or in Steam right-click Enter the Gungeon, then **Properties → Installed Files → Verify integrity of game files**.
