# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

The patcher is built on the chefrpg-43aspect architecture (`../chefrpg-43aspect/`; read its `CLAUDE.md` before making structural changes here). Device-tested on the Nova (2026-10-20): the Windows output `60d072d9…` boots through the splashes and the story intro to the title screen and into the Breach. Gameplay fills 1278x960, the HUD sits at the real screen edges, and the log is clean.

Target: Retroid Pocket Nova (1280x960, Windows build under Proton via DroidDeck), plus the native mac build for development. Pixel budget on narrow (<16:9) screens:

- **gameplay:** 426x320 at integer 3x on 1280x960 (1278x960);
- **menu/intro framing** (while `BraveCameraUtility.OverrideAspect` is set): 360x270. The title diorama art is authored for exactly 270 rows, and more rows showed black bands with meteors flickering through them.

16:9 and wider screens stay exactly vanilla (480x270).

Open items:

- The pause menu, Ammonomicon, boss cards and options menu haven't been seen on the device yet, because adb key events don't reach the gamescope window. Check them by hand.
- Linux native build and GameNative are untested.

The public repo is github.com/jevonlipsey/gungeon-43aspect (v1.0). The web patcher is at https://jevonlipsey.github.io/gungeon-43aspect/, deployed by `pages.yml`. The repo is listed in the `../nova-43aspect` meta repo; a separate agent maintains that launchpad.

## The platform rule (read before patching anything)

The mac and Windows `Assembly-CSharp.dll` are **different compiles**, not the same file. About 800 decompiled types differ, most of them Wwise/platform code. Always patch a platform's own dll and never deploy a mac-derived dll to the device.

| input | path | sha256 | size |
|---|---|---|---|
| mac (dev) | `original/Assembly-CSharp.dll`, copied from `EtG_OSX.app/Contents/Resources/Data/Managed/` | `88981a5b…` | 7,867,392 |
| Windows (device) | `verify/remote/Assembly-CSharp-win-orig.dll` = device `Managed/Assembly-CSharp.dll.bak` | `c6c0eca9…` | 7,888,896 |

**The past failure this rule comes from (2026-10-15):** the "splash/boot animation loops forever" bug was not an aspect problem. A patched *mac* dll (`59a230bf…`, kept as `verify/remote/Assembly-CSharp-device-live-macderived.dll`) was pushed to the device. The mac build hardcodes `AkBasePathGetter.GetPlatformName() => "Mac"`, so under Proton Wwise looked in `StreamingAssets/Audio/GeneratedSoundBanks/Mac/` (the device only has `Windows/`) and terminated the sound engine. The async `SFX.bnk` callback (`GameManager.BankCallback`) then never set `GameManager.AUDIO_ENABLED`, and `FoyerPreloader.AsyncLoadFoyer` spun forever on `while (!GameManager.AUDIO_ENABLED) yield return null;`. The device log is `verify/remote/output_log-hang-2026-10-15.txt`. If a future boot hang shows `GeneratedSoundBanks\Mac\` in `output_log.txt`, this is why. The patcher must read that literal and refuse a mismatch, and the patch must never modify `AkBasePathGetter`.

## Commands

Run from the repo root (the verifier finds `patcher/gungeonhelper/GungeonHelper.dll` by walking up from the cwd).

```bash
# patch. writes the output only if the built-in verifier passes. patching never needs unity's dlls
# (StubEnumResolver rebuilds the few enum types cecil needs from the input's own constants), so the
# bytes are identical with or without --refs; --refs only upgrades the verify to Full.
# exit codes: 0 ok, 1 verify failed/error, 2 usage, 3 already patched, 4 not EtG's dll, 5 --target mismatch
dotnet run --project patcher -- verify/remote/Assembly-CSharp-win-orig.dll --target win --refs verify/remote/win-managed --out Assembly-CSharp-43-win.dll
dotnet run --project patcher -- original/Assembly-CSharp.dll --target mac --refs "<mac Managed>" --out Assembly-CSharp-43-mac.dll
# other forms: `patcher <dll>` (auto target, reference-free, writes Assembly-CSharp-43.dll beside it)
#              `patcher <ManagedDir>` (auto target, full verify against that dir)
# expected hashes: win 60d072d9... (device-tested), mac 62d261fe...

# verify a patched dll. gate every deploy on it.
dotnet run --project verify/Verify.csproj -- Assembly-CSharp-43-win.dll --input verify/remote/Assembly-CSharp-win-orig.dll --refs verify/remote/win-managed
# expected: exit 0 and "checks: 80 passed, 0 failed, 0 skipped" (without --refs: 79 passed, 1 skipped)

# end-user launcher (stdlib python; uses ./patcher beside it, then dist/, then dotnet run)
python3 patch.py "<Managed dir or dll>" [--target win|mac|linux] [--no-install]

# browser patcher: patcher + verifier compiled to .net wasm, built into web/dist/ (gitignored)
bash tools/build_web.sh            # --helper also rebuilds GungeonHelper.dll first
# release gate for the web build: browser output byte-identical to the CLI's for both inputs,
# plus already-patched / junk-input cases. needs the game dlls, so it does not run in CI.
node web/test/parity.mjs --chrome  # expected: PARITY OK
node web/test/e2e.mjs --input verify/remote/Assembly-CSharp-win-orig.dll --shot <png>   # drives the real UI
# web/app/index.html?mock swaps in mock-runtime.js for UI work without the wasm runtime

# release binaries (one runtime at a time; parallel publishes share patcher/obj). CI does this on a v* tag.
dotnet publish patcher -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true -o dist/mac

# after changing anything in patcher/gungeonhelper/: rebuild, re-embed, re-patch from pristine
dotnet build -c Release patcher/gungeonhelper/GungeonHelper.csproj
cp patcher/gungeonhelper/bin/Release/net35/GungeonHelper.dll patcher/gungeonhelper/GungeonHelper.dll

# decompile a dll for reading (output is gitignored; never commit it)
ilspycmd -p -o decomp-win -r "<Managed dir>" verify/remote/Assembly-CSharp-win-orig.dll
ilspycmd -p -o decomp-mac -r "<Managed dir>" original/Assembly-CSharp.dll
ilspycmd -t BraveCameraUtility -r "<Managed dir>" <dll>      # a single type

# IL-level diff of two dlls (opcode/constant changes per method)
dotnet run --project tools/inspector -- <original.dll> <patched.dll>
```

`tools/inspector/` builds two entry points. `Program.cs` is the IL differ. `DumpMethod.cs` decompiles one hardcoded method and is the csproj's `StartupObject`, so switch that before using the differ.

There is no linter and no per-test selector. To investigate a failing check, read it in `verify/IlVerifier.cs`, `verify/SemanticVerifier.cs` or `verify/Simulator.cs` and rerun the harness. The legacy dlls in `orig/legacy-attempts/` came from the earlier unverified patcher. Never deploy them.

## Engine facts

- Unity **2017.4.27f1**, Mono on the **net35** profile (`mscorlib 2.0.0.0`). Everything the patcher emits or clones must resolve against that mscorlib and the Unity 2017 module dlls in `Managed/`. A reference that resolves against the patcher's own .NET (CoreLib, netstandard) builds fine and then fails at JIT on device. That is chefrpg's v5.4 failure; reject it in the verifier.
- UI is **Daikon Forge** (`dfGUIManager`, `dfGUICamera`, `dfControl`), not uGUI. HUD root is `GameUIRoot`.
- Scaling modes (`GameOptions.PreferredScalingMode`): `PIXEL_PERFECT`, `FORCE_PIXEL_PERFECT` (the default), `UNIFORM_SCALING`, `UNIFORM_SCALING_FAST`. Pixel-perfect modes size the camera rect to an integer multiple of `H_PIXELS x V_PIXELS`. Uniform modes letterbox to `ASPECT`.
- The game already supports *ultrawide*: most code is parameterised by `BraveCameraUtility.ASPECT`, `H_PIXELS` and `V_PIXELS`. What blocks 4:3 is `ASPECT = Max(1.7777778, w/h)` plus scattered `1.7777778f` / `270f` literals. Vanilla on 1280x960 draws a 960x540 box (k=2).

### Where things live (Windows decomp; the mac build has the same methods)

| concern | members |
|---|---|
| aspect and pixel budget | `BraveCameraUtility.get_ASPECT / get_H_PIXELS / get_V_PIXELS / GetRect / GetRectSize / MaintainCameraAspect / MaintainCameraAspectForceAspect / GetTargetScreenResolution`; `OverrideAspect` is set to 16:9 by `GameManager.LoadMainMenu` and `Foyer` (menu/intro) and cleared in `GameManager` / `MainMenuFoyerController` |
| render scale | `Pixelator.CheckSize` (`ScaleTileScale = h * rect.height / 270f`), `NUM_MACRO_PIXELS_*`, `m_currentMacroResolution*`; `CameraController` zoom requantises from `H_PIXELS/V_PIXELS` |
| UI cameras | `dfGUICamera.Awake/Update` (`ForceToSixteenNine` → `MaintainCameraAspectForceAspect` with hardcoded 480/270); `dfGUIManager.RenderAspect` (fixed `1.7777778f` when `FixedAspect`); `FullOptionsMenuController.DisableHierarchy` |
| resolution and options | `GameOptions.GetRecommendedResolution` (prefers 16:9), `GetRecommendedScalingMode`; `BraveOptionsMenuItem`; `GameManager.DoSetResolution` |
| already ASPECT-relative; check before touching | `Minimap`, `MinimapRenderer`, `GameUIBossHealthController`, `EndTimesNebulaController`, `BossCardUIController`, `dfInputManager.PreprocessUltrawideAmmonomiconMousePosition`, `AmmonomiconController`, `AmmonomiconDeathPageController`, `TextBoxManager` |
| boot flow | `FoyerPreloader` (first load screen, waits on audio) → `tt_foyer`; `IntroSequenceManager.HandleElement` (Dodge Roll / intro cards, calls `MaintainCameraAspect` every frame; an exception in it kills the coroutine and freezes the card); `IntroMovieClipPlayer` |

`270`/`480` literals in bullet scripts (angles), `DarknessChallengeModifier` (±270°) and `BraveUtility` hex `% 16` are false positives in a literal grep.

## Architecture

The tool rewrites the game's IL; it ships no game code. The pipeline mirrors chefrpg:

1. **Patcher** (`patcher/Program.cs` + `PatchApi.cs`, Mono.Cecil 0.11.6). It reads the input with `StubEnumResolver` (`patcher/StubEnumResolver.cs`).
   - After the read, `Prime` collects every external value-type constant (parameter, field, property). `Resolve` returns synthesized enum stubs for exactly those types and throws for anything else, so nothing binds to the host .NET and the browser needs no Unity dlls.
   - The verifier uses `RefsResolver` (`verify/RefsResolver.cs`: only `--refs`, no default dirs) in Full mode. In ReferenceFree mode it skips the one external-resolution check.
   - **Identity guard.** EtG types must be present (else exit 4), a `Gungeon43` type means already patched (exit 3), and the `GetPlatformName` literal must match `--target` (else exit 5).
   - **Helper clone.** The helper type is cloned into the game module (two-pass declare-then-copy with exception handlers, ported from chefrpg's `InjectSafeAreaHelper`). It reuses the module's existing reference rows, so the output has the same 23 AssemblyReferences and no new MemberReference rows.
   - **Branches.** Short branches are widened in every edited method, and the written bytes are re-read to check branch targets.
   - **Gate.** The verifier runs in memory on the exact bytes, and the output is written only on pass.
2. **Patch sites** (`verify/Sites.cs`, found by IL pattern, never by offset, with exact match counts asserted; the same code patches both inputs):
   - S1 `BraveCameraUtility.get_ASPECT` and S2 `get_H_PIXELS` / `get_V_PIXELS` get the prologue `if (Gungeon43.Active()) return Gungeon43.X(OverrideAspect.HasValue);`. It runs before the vanilla `OverrideAspect` check, so the menu's forced 16:9 becomes the 270-row menu budget instead of a letterbox.
   - S3 `MaintainCameraAspectForceAspect`: the `480f/270f/480/270` literals → `ForceH/ForceV/ForceHi/ForceVi` (the `ForceToSixteenNine` UI cameras get a k-fit 16:9 band inside the 4:3 frame).
   - S4 `Pixelator.CheckSize`: `/ 270f` → `/ V_PIXELS`, so `ScaleTileScale` (and `GameUIRoot.UpdateScale`'s `UIScale = ScaleTileScale/3`) equals the integer multiple.
   - S5 `GameOptions.GetRecommendedResolution`: `1.7777778f` → `NativeAspect()`.

   Everything else is untouched. `GetRect` / `GetRectSize` / `MaintainCameraAspect` follow from the new `ASPECT` / `H_PIXELS` / `V_PIXELS`.
3. **Helper** (`patcher/gungeonhelper/`, net35, refs only `mscorlib 2.0` and `UnityEngine.CoreModule`). It is a `public static partial class Gungeon43` split in two files:
   - `DisplayPolicy.cs` holds pure integer math with no usings and no `System.Math`, because `verify/` compiles the same file:
     - narrow = `w*9 < h*16`;
     - gameplay `k = max(1, (h+150)/300)`, `V = (h/k)&~1`, `H = (w/k)&~1`;
     - menu `V = 270`, `H = (270*w/h)&~1`;
     - 16:9 band `ForcedV = ((H*9+8)/16)&~1`.
   - `Gungeon43.cs` is the Unity-facing half (`Screen` reads only).

   `GungeonHelper.dll` is committed, prebuilt and embedded in the patcher. The verifier reads the on-disk copy, so a stale embed fails.
4. **Verify** (`verify/`, 80 checks, run inside the patcher and standalone):
   - **`IlVerifier`:**
     - bodies decode;
     - branch targets land;
     - stack balances in edited and helper methods;
     - no `leave` without handlers;
     - AssemblyReference set identical to the input's;
     - no netstandard / System.Runtime / CoreLib;
     - every external member resolves through `--refs` only.
   - **`SemanticVerifier`:**
     - S1–S5 shapes and counts;
     - the cloned helper equals the embedded dll;
     - every other method (about 40.5k, including `AkBasePathGetter` and `FoyerPreloader`) is IL-identical to the input.
   - **`Simulator`:** the policy table, pixel-perfect and uniform rects, force bands, menu cases, and a sweep of narrow sizes.

   Pinned gameplay cases:

   | screen | budget | k | result |
   |---|---|---|---|
   | 1280x960 | 426x320 | 3 | 1278x960, uniform rect 1278/1280 x 1 |
   | 1024x768 | 340x256 | 3 | |
   | 960x720 | 480x360 | 2 | |
   | 1920x1080 | vanilla | | |

   Pinned menu case: 1280x960 → 360x270, uniform full rect; pixel-perfect 1080x810.

Cecil's rewrite makes the output about 160 KB smaller: it dedups the string heap (about 82 KB) and packs method bodies tighter. Custom attributes, resources and references are unchanged, so the size drop is expected.

## Device and deploy

The Windows build runs under Proton on the Nova (`droiddeck` in ssh, appid 311690). Patching and verifying happen on the mac, and the result is pushed over ssh: write `.new`, compare sha256 on both ends, then `mv`. Never replace the live dll without the hash check, and never touch the pristine `.bak`. Access, device paths, the log location, launching and stopping the game, and how to run a device test are all in `CLAUDE.local.md` (gitignored, personal device details).

Delegate device runs to a Sonnet subagent with an exact script. Review every screenshot yourself, because a subagent can't judge whether 4:3 looks right.

## Directories you should not edit

- `original/`, `orig/` (including `orig/legacy-attempts/`, the unlabeled dlls from the earlier attempt), `verify/remote/`: pristine inputs and device captures. They're gitignored.
- `decomp*/`: decompiler output, reference only, gitignored.
- `dist/`, `bin/`, `obj/`: build output.
- `Assembly-CSharp-43-*.dll` at the root: build outputs, gitignored.
- `patcher/gungeonhelper/GungeonHelper.dll`: only ever replaced by the rebuild and copy step above.

## Web patcher

`web/` is a copy of chefrpg's browser patcher.

- `web/app/style.css` is the suite's shared design system and must stay byte-identical to `../chefrpg-43aspect/web/app/style.css` and celeste's.
- Only copy, paths and failure reasons are game-specific. The JS global is `globalThis.Etg43`.
- `web/wasm/Interop.cs` calls `PatchApi.Patch(input, "auto", refs: null)` and adds the detected `platform` to the JSON. The success view names the version of the game the file came from.
- `.github/workflows/pages.yml` builds and deploys `web/dist` on pushes to `main` that touch `web/`, `patcher/`, `verify/` or the build script. `release.yml` builds the four self-contained patcher zips on a `v*` tag.

## Publishing

The repo is public, like chefrpg. Never commit a game assembly (original or patched), decompiler output, device logs or screenshots. `.gitignore` blocks `*.dll`, `*.log`, `*.png`, `original/`, `orig/`, `verify/remote/`, `decomp*/` and `CLAUDE.local.md`. The one `!` exception is `patcher/gungeonhelper/GungeonHelper.dll`, built with `DebugType=none` and a `PathMap` so no local paths end up inside it. Releases ship only patcher binaries, never a patched dll.
