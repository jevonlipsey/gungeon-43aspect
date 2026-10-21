# Enter the Gungeon 4:3 Aspect Ratio Patch

Native 4:3 aspect ratio patch for **Enter the Gungeon**, aimed at the **Retroid Pocket Nova** (1280x960 4:3 AMOLED, Windows build via ARM64 Proton) and any other 4:3 or non-16:9 display. The game fills the screen with integer pixel scaling, and the HUD sits at the real screen edges.

The patcher rewrites `Assembly-CSharp.dll` at the IL level with [Mono.Cecil](https://github.com/jbevain/cecil). **No copyrighted game code is redistributed.** The tool ships only the patch logic and modifies your own legally owned copy.

> Unofficial fan project, not affiliated with or endorsed by Dodge Roll or Devolver Digital. Enter the Gungeon and its files belong to them. The code here is MIT licensed ([LICENSE](LICENSE)).

## Install (players)

You patch your own copy of the game's `Assembly-CSharp.dll`; nothing from the game is shipped here.

- **Web patcher** (any browser, including your phone): <https://jevonlipsey.github.io/gungeon-43aspect/>. Drop in your dll and download the patched one. It runs locally; nothing is uploaded.
- **Desktop patcher** (no .NET needed): download the zip for your system from the [Releases page](https://github.com/jevonlipsey/gungeon-43aspect/releases).
  - **On Windows, drag `Assembly-CSharp.dll` (from `EtG_Data\Managed`) onto `patcher.exe`.** Then rename the original to `Assembly-CSharp.dll.bak` and the new `Assembly-CSharp-43.dll` to `Assembly-CSharp.dll`.
  - On macOS and Linux, run `./patcher "/path/to/Managed"`.

  The releases contain only the patcher, never a game file.
- **`python3 patch.py "/path/to/EtG_Data/Managed"`**: patches, verifies, backs up the original to `Assembly-CSharp.dll.bak` and installs.

Then, in the game's **Options → Video**, pick your screen's resolution (1280x960 on the Nova) and fullscreen. On DroidDeck, set Resolution to **Match screen (1280x960)** and 16:9 game fit to **Stretch**.

Step-by-step instructions, per-platform folder locations, Android handheld notes, display settings and troubleshooting are in **[docs/INSTALL.md](docs/INSTALL.md)**.

**Patch the file from the install you will play.** The Windows, Mac and Linux versions of the game ship different `Assembly-CSharp.dll` files. A Mac file copied onto a Windows install or a handheld hangs on the first loading screen; [why](#the-boot-hang-is-a-platform-mismatch-not-an-aspect-bug).

## Why the game letterboxes

The engine already supports *ultrawide* screens: most of the camera code reads `BraveCameraUtility.ASPECT`, `H_PIXELS` and `V_PIXELS`. What pins it to 16:9 and wider:

1. **`BraveCameraUtility.ASPECT`** returns `Max(1.7777778, width / height)`, so a screen narrower than 16:9 is treated as 16:9. In the pixel-perfect scaling modes it is hard 16:9.
2. **The pixel budget** is 480x270 at 16:9. Pixel-perfect modes fit an integer multiple of it: on 1280x960, `min(1280/480, 960/270)` rounds down to 2, so the game draws a **960x540 box**. The uniform modes letterbox to a 1280x720 band.
3. **`Pixelator`** derives the HUD and tile scale from a hardcoded `/ 270f`, and the forced-16:9 UI cameras use a hardcoded 480x270 budget.

## What the patch changes (all in `Assembly-CSharp.dll`)

On screens narrower than 16:9 the game gets a 4:3-shaped pixel budget sized for integer scaling. On 16:9 and wider screens every patched method falls through to its original code, so those screens are unchanged.

- **Gameplay budget:** `k = max(1, round(height / 300))`. The budget is `(width / k) x (height / k)`, rounded down to even. On 1280x960 that's **426x320 at exactly 3x**, filling 1278x960. Other screens: 1024x768 → 340x256 at 3x, 960x720 → 480x360 at 2x, 1280x800 → 426x266 at 3x.
- **Menu and intro framing:** while the game sets its menu-time `OverrideAspect`, the budget is the vanilla 270 rows at the screen's shape (360x270 on 1280x960). The title-screen diorama is authored for exactly 270 rows; more rows showed past its edges as black bands with meteors streaking through them.

Six methods change, at five patch sites:

1. **`BraveCameraUtility.get_ASPECT`, `get_H_PIXELS`, `get_V_PIXELS`** get a prologue: on a narrow screen, return the helper's aspect and budget (the menu budget while `OverrideAspect` is set). `GetRect`, `GetRectSize` and `MaintainCameraAspect` are untouched; they follow from these three values.
2. **`BraveCameraUtility.MaintainCameraAspectForceAspect`** (used by the forced-16:9 UI cameras): the hardcoded `480/270` literals become the 4:3 budget's width and a 16:9 height. Those views become a 1278x720 band inside the 4:3 frame instead of a 960x540 box.
3. **`Pixelator.CheckSize`**: `ScaleTileScale = height / 270f` becomes `/ V_PIXELS`, so the HUD scale is the same integer multiple as the game (`GameUIRoot` sets `UIScale = ScaleTileScale / 3`).
4. **`GameOptions.GetRecommendedResolution`**: prefers the display's own aspect instead of 16:9 when picking a first-run resolution.

The logic lives in a small helper class, `Gungeon43` (source in `patcher/gungeonhelper/`). It is built against .NET 3.5 (`mscorlib 2.0`, the runtime of the game's Unity 2017.4 Mono) and cloned into the game assembly. Its policy half (`DisplayPolicy.cs`) is plain integer code that the verifier compiles and drives directly.

Nothing else is touched. The verifier checks that the other 40,500 or so methods are instruction-for-instruction identical to the input.

### The boot hang is a platform mismatch, not an aspect bug

An earlier attempt left the game looping its first loading animation on the device. The cause was not the aspect patch. A dll built from the **Mac** version had been copied onto the **Windows** install:

1. The Mac build hardcodes `AkBasePathGetter.GetPlatformName() => "Mac"`.
2. Under Proton, Wwise looked for `StreamingAssets/Audio/GeneratedSoundBanks/Mac/`, which only a Mac install has, and terminated the sound engine.
3. `FoyerPreloader` waits on `GameManager.AUDIO_ENABLED` before it loads the Breach, so it waited forever.

The patcher therefore reads that literal and reports the platform. With `--target win|mac|linux` it refuses a mismatch (exit 5). The verifier also checks that `AkBasePathGetter` and `FoyerPreloader` are byte-identical to the input.

## Usage

```text
patcher <Assembly-CSharp.dll>                   # writes Assembly-CSharp-43.dll next to it (reference-free verify)
patcher <ManagedDir>                            # same, with the full verifier (uses the folder's Unity dlls)
patcher <input.dll> [--target win|mac|linux|auto] [--refs <ManagedDir>] [--out <output.dll>]
```

- `--refs` supplies the game's Unity assemblies. **With it, the patcher runs the full verifier.** Without it, it runs every check that doesn't need them (this is what the browser patcher does) and reports the rest as skipped. Patching itself never needs them, and the output bytes are the same either way.
- `--target` names the version of the game you are installing into. A dll from another version is refused. `auto` (the default) patches any version and prints which one it is.
- The output file is written **only if verification passed**. A failed run leaves nothing behind.
- Exit codes:
  - `0` ok;
  - `1` verification failed or error;
  - `2` usage;
  - `3` input is already patched;
  - `4` input is not Enter the Gungeon's `Assembly-CSharp.dll`;
  - `5` `--target` doesn't match the dll's version.

`patch.py` is a stdlib-only launcher (Python 3.8+, no .NET SDK needed):

- **Finding a patcher:** it uses a prebuilt one (`./patcher[.exe]` next to the script, or `dist/<mac|mac-x64|win|linux>/`). If there is none, it falls back to `dotnet run --project patcher`.
- **Installing:** it patches into a temp file. Only if the patcher exits 0 does it copy the original to `Assembly-CSharp.dll.bak` (never overwriting an existing one) and replace `Assembly-CSharp.dll`.
- **`--no-install`** just writes `Assembly-CSharp-43.dll`.

## Web patcher

The same Cecil patcher and verifier, compiled to .NET WebAssembly (`web/wasm/PatcherWasm.csproj`), runs entirely in the browser. You drop in `Assembly-CSharp.dll`, it is patched and verified locally, and you download the patched dll. **Nothing is uploaded**, and no game code is hosted.

- **Build:** `bash tools/build_web.sh` builds it into `web/dist/` (`index.html` + `_framework/`).
- **Deploy:** `.github/workflows/pages.yml` publishes it to GitHub Pages on pushes to `main` that touch `web/`, `patcher/`, `verify/` or the build script. The embedded `GungeonHelper.dll` is committed, so CI needs no game files.
- **Unity enums without Unity:** Cecil needs the underlying type of a few Unity enums to write parameter defaults. `StubEnumResolver` rebuilds just those enum types from the constants already in the input, so neither the browser nor the CLI needs Unity's dlls to patch.
- **Release gate:** `node web/test/parity.mjs` checks that the browser output is byte-identical to the CLI's for both the Mac and Windows inputs. It needs your own game dlls, so it does not run in CI.

## Verification

`verify/` is a standalone harness that re-checks a patched dll before it ships. The patcher also runs it on every output, in memory.

- **IL:**
  - every method body decodes and every branch target lands on an instruction;
  - stack heights balance in every edited and helper method;
  - no method has a `leave` without exception handlers;
  - the output's assembly references are exactly the input's 23, with nothing from netstandard, `System.Runtime` or `System.Private.CoreLib`;
  - with `--refs`, every external member resolves against the game's own Managed folder and nothing else.

  A reference that resolves against the patcher's own .NET instead of Unity's Mono builds fine and then fails at JIT on the device. That is the failure this guard exists for.
- **Semantics:**
  - each of the six patched methods has exactly the expected shape;
  - the cloned helper matches the embedded `GungeonHelper.dll`, method for method and handler for handler;
  - every other method in the assembly is IL-identical to the input, `AkBasePathGetter` and `FoyerPreloader` included.
- **Simulation:** `DisplayPolicy.cs` is compiled into the harness and driven through:
  - the pixel-perfect and uniform-scaling camera rects;
  - the forced-16:9 bands;
  - the HUD scale;
  - the menu budget;
  - a sweep of narrow screen sizes.

  Pinned cases include 1280x960 (426x320 at 3x → 1278x960, menu 360x270), 1920x1080 (vanilla) and 2560x1080 (vanilla).

```bash
dotnet run --project verify/Verify.csproj -- Assembly-CSharp-43.dll --input Assembly-CSharp.dll --refs "/path/to/Managed"
```

Exit code 0 with `checks: 80 passed, 0 failed, 0 skipped` means it's safe to install. Gate every deploy on it.

## Install on the Retroid Pocket Nova

Tested on DroidDeck. With the patch, the game boots through the splash screens and story intro to the title screen and into the Breach. Gameplay fills 1278x960 at 3x with the HUD in the corners, and the log has no exceptions.

The device's Proton prefix has no .NET runtime and `scp` is disabled, so the Windows dll is patched off-device and pushed over ssh:

```bash
# 1. take the pristine windows dll (the device's .bak, or a backup you kept) - never the mac one
# 2. patch it; --target win refuses anything that isn't the windows build
patcher Assembly-CSharp.dll --target win --refs "/path/to/a/Managed" --out Assembly-CSharp-43.dll
# 3. push to the device (.new + hash check, then mv, so a partial transfer can never
#    replace the live dll)
ssh droiddeck 'cat > ".../Managed/Assembly-CSharp.dll.new"' < Assembly-CSharp-43.dll
ssh droiddeck 'sha256sum ".../Managed/Assembly-CSharp.dll.new"'   # must match the patcher's sha256
ssh droiddeck 'mv -f ".../Managed/Assembly-CSharp.dll.new" ".../Managed/Assembly-CSharp.dll"'
```

## Build from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download).

The helper is a separate project that the patcher embeds as a resource. After changing anything in `patcher/gungeonhelper/`, rebuild it **and copy the result over the embedded file**, then re-patch from the original dll. The patcher doesn't rebuild the helper, and the verifier fails a stale copy.

```bash
dotnet build -c Release patcher/gungeonhelper/GungeonHelper.csproj
cp patcher/gungeonhelper/bin/Release/net35/GungeonHelper.dll patcher/gungeonhelper/GungeonHelper.dll
```

```bash
dotnet publish patcher -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o dist/mac
dotnet publish patcher -c Release -r osx-x64   --self-contained -p:PublishSingleFile=true -o dist/mac-x64
dotnet publish patcher -c Release -r win-x64   --self-contained -p:PublishSingleFile=true -o dist/win
dotnet publish patcher -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o dist/linux
```

Publish the runtimes one at a time. Parallel `dotnet publish` runs share `patcher/obj` and can clobber each other's intermediate output.

Releases are built by `.github/workflows/release.yml` on a `v*` tag. They contain only the patcher, `patch.py`, the install guide and `README-FIRST.txt`, never a game or patched dll.

## Limits

- **Not yet checked on the device:** the pause menu, Ammonomicon, boss intro cards and the options menu. The forced-16:9 views should appear as a 1278x720 band inside the 4:3 frame.
- **Linux native build:** the patch applies to it, but it hasn't been played.
- **GameNative:** untested.
- **Updates:** a game update that changes any of the six patched methods makes the patcher refuse to write (verification fails) until the patch is updated.
