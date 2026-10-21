#!/usr/bin/env python3
"""
enter the gungeon 4:3 aspect ratio patcher (launcher)

runs the mono.cecil patcher against your legally owned Assembly-CSharp.dll.
no copyrighted binaries are distributed here. python 3.8+, stdlib only; the
.net sdk is NOT needed when a prebuilt patcher is available.

usage:
  python3 patch.py "/path/to/Managed" [--target win|mac|linux] [--no-install]
  python3 patch.py "/path/to/Assembly-CSharp.dll" [--target win|mac|linux] [--no-install]

without --target the patcher reads which build (Windows / Mac / Linux) the dll is and
patches it as that. that is safe here because the dll is patched in place, inside the
install it came from. pass --target when you patch a dll for another machine: the builds
are different compiles, and a mac dll in a Windows install hangs the boot (the audio
never starts), so a mismatch is refused.

install mode (default):
  1. patches Assembly-CSharp.dll into a temp file (the patcher verifies it and only
     writes the file if every check passed)
  2. copies the original to Assembly-CSharp.dll.bak (an existing .bak is never overwritten)
  3. moves the patched temp file over Assembly-CSharp.dll
  a failed run leaves Assembly-CSharp.dll untouched.

patcher lookup order: ./patcher[.exe] next to this script (the release zips),
dist/<mac|mac-x64|win|linux>/patcher[.exe] for this machine, then
`dotnet run --project patcher` if dotnet is on PATH.

patcher exit codes: 0 ok, 1 verify failed / error, 2 usage, 3 already patched,
4 not Enter the Gungeon's dll, 5 --target does not match the dll's platform.
"""

import hashlib
import os
import platform
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def rid_dir():
    system, machine = platform.system(), platform.machine().lower()
    if system == "Darwin":
        return "mac" if machine in ("arm64", "aarch64") else "mac-x64"
    if system == "Windows":
        return "win"
    if system == "Linux":
        return "linux"
    return None


def stale(binary):
    """dist/ is gitignored build output and can lag the sources; an old patcher could
    skip a guard, so a binary older than any patcher/verify source is ignored."""
    newest = 0.0
    for d in ("patcher", "verify"):
        for root, dirs, files in os.walk(os.path.join(HERE, d)):
            dirs[:] = [x for x in dirs if x not in ("bin", "obj", "remote")]
            for f in files:
                if f.endswith((".cs", ".csproj")) or f == "GungeonHelper.dll":
                    newest = max(newest, os.path.getmtime(os.path.join(root, f)))
    if newest > os.path.getmtime(binary):
        print(f"[!] ignoring {binary}: older than the patcher sources (rebuild it with dotnet publish).")
        return True
    return False


def patcher_command():
    exe = "patcher.exe" if platform.system() == "Windows" else "patcher"
    # the release zips ship the binary next to this script; in a clone, patcher/ is the source dir
    beside = os.path.join(HERE, exe)
    if os.path.isfile(beside):
        return [beside], beside
    d = rid_dir()
    if d:
        prebuilt = os.path.join(HERE, "dist", d, exe)
        if os.path.isfile(prebuilt) and not stale(prebuilt):
            return [prebuilt], prebuilt
    patcher_dir = os.path.join(HERE, "patcher")
    if shutil.which("dotnet") and os.path.isdir(patcher_dir):
        return ["dotnet", "run", "--project", patcher_dir, "--"], "dotnet run --project patcher"
    print("[!] no prebuilt patcher for this machine and no .net sdk (`dotnet` on PATH) to build one.")
    sys.exit(1)


def remove_quiet(path):
    try:
        os.remove(path)
    except OSError:
        pass


def explain(rc):
    msgs = {
        3: "this Assembly-CSharp.dll is already patched. restore the .bak (or let steam verify the game files) and run again.",
        4: "that is not Enter the Gungeon's Assembly-CSharp.dll. check you pointed at the right Managed folder.",
        5: "--target does not match this dll's platform. a mac dll in a Windows install hangs the boot: pass the target that belongs to the dll.",
        1: "patch or verification failed. nothing was installed.",
    }
    print("[!] " + msgs.get(rc, f"patcher exited with code {rc}. nothing was installed."))


def main():
    print("==================================================")
    print("   enter the gungeon 4:3 aspect ratio patcher")
    print("==================================================")

    args = sys.argv[1:]
    install, target, paths = True, "auto", []
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--no-install":
            install = False
        elif a == "--target" and i + 1 < len(args):
            i += 1
            target = args[i]
        elif a in ("-h", "--help"):
            print(__doc__)
            sys.exit(0)
        elif a.startswith("--"):
            print(f"[!] unknown option {a}")
            sys.exit(2)
        else:
            paths.append(a)
        i += 1

    if len(paths) != 1 or target not in ("win", "mac", "linux", "auto"):
        print('[!] usage: python3 patch.py "<Managed dir or Assembly-CSharp.dll>" [--target win|mac|linux] [--no-install]')
        sys.exit(2)

    src = os.path.abspath(paths[0])
    if os.path.isdir(src):
        managed_dir = src
        input_file = os.path.join(src, "Assembly-CSharp.dll")
    else:
        input_file = src
        managed_dir = os.path.dirname(src)
    final_file = os.path.join(managed_dir, "Assembly-CSharp-43.dll")
    tmp_file = final_file + ".tmp"
    backup_file = input_file + ".bak"

    if not os.path.isfile(input_file):
        print(f"[!] error: '{input_file}' not found.")
        sys.exit(1)

    cmd, how = patcher_command()
    out_file = tmp_file if install else final_file
    print(f"[*] input:   {input_file}")
    print(f"[*] target:  {target}")
    print(f"[*] patcher: {how}")

    remove_quiet(out_file)
    try:
        rc = subprocess.run(cmd + [input_file, "--target", target, "--refs", managed_dir, "--out", out_file]).returncode
    except OSError as e:
        print(f"[!] could not run the patcher: {e}")
        sys.exit(1)

    if rc != 0:
        remove_quiet(out_file)
        explain(rc)
        sys.exit(rc if rc > 0 else 1)
    if not os.path.isfile(out_file) or os.path.getsize(out_file) == 0:
        remove_quiet(out_file)
        print("[!] the patcher reported success but wrote no output. nothing was installed.")
        sys.exit(1)

    if not install:
        print(f"[+] done: {final_file}")
        print(f"    sha256 {sha256(final_file)}")
        return

    try:
        if os.path.exists(backup_file):
            print(f"[*] backup already exists, keeping it: {backup_file}")
        else:
            shutil.copy2(input_file, backup_file)
            print(f"[*] backed up original: {backup_file}")
        os.replace(out_file, input_file)
    except OSError as e:
        remove_quiet(out_file)
        print(f"[!] could not install the patched dll: {e}")
        print("    Assembly-CSharp.dll was not changed (is the game running, or the folder read-only?).")
        sys.exit(1)

    print(f"[+] installed: {input_file}")
    print(f"    sha256 {sha256(input_file)}")
    print(f"    to undo: copy {os.path.basename(backup_file)} back over Assembly-CSharp.dll")


if __name__ == "__main__":
    main()
