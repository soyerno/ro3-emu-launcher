#!/usr/bin/env python3
"""Packages the published launcher and the files it needs next to it into a patch ZIP.

Usage: python tools/package-patch.py [--src dist] [--out ro3-launcher-patch.zip] [--name ro3-launcher]
                                     [--exe Ro3Launcher.exe] [--extras brand/patch]

--src     folder holding the published launcher (publish.cmd writes dist) plus RO3Emu.exe
--out     ZIP to write
--name    top-level folder inside the ZIP
--exe     launcher executable (the AssemblyName of the build; a branded fork may rename it)
--extras  folder whose files are added to the ZIP at the same relative path (for example data/loc_en.tsv,
          LEEME.txt); a file there replaces the one of the same name taken from --src. Files whose name starts with a dot
          are skipped. Ignored when the folder does not exist.
"""
import argparse
import os
import sys
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# (path relative to --src, required); the launcher executable is added from --exe
FILES = [
    ("RO3Emu.exe", True),
    ("launcher.json", False),
    ("news.json", False),
    ("cloudflared.exe", False),
    ("lua54.dll", False),
    ("WebView2Loader.dll", False),
    ("e_sqlite3.dll", False),
    ("D3DCompiler_47_cor3.dll", False),
    ("PenImc_cor3.dll", False),
    ("PresentationNative_cor3.dll", False),
    ("vcruntime140_cor3.dll", False),
    ("wpfgfx_cor3.dll", False),
    (os.path.join("client", "Ro3Emu.ClientHooks.dll"), False),
    (os.path.join("client", "Ro3Emu.Loc.dll"), False),
    (os.path.join("assets", "hero-art.jpg"), False),
]


def posix(path: str) -> str:
    return path.replace(os.sep, "/")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--src", default="dist")
    parser.add_argument("--out", default="ro3-launcher-patch.zip")
    parser.add_argument("--name", default="ro3-launcher")
    parser.add_argument("--exe", default="Ro3Launcher.exe")
    parser.add_argument("--extras", default=os.path.join(ROOT, "brand", "patch"))
    args = parser.parse_args()
    files = [(args.exe, True)] + FILES

    missing = [rel for rel, required in files if required and not os.path.exists(os.path.join(args.src, rel))]
    if missing:
        print(f"[ERROR] Required files not found in {args.src}: {', '.join(missing)}")
        return 1

    entries = {}  # path inside the ZIP (relative) -> file on disk
    for rel, _ in files:
        full = os.path.join(args.src, rel)
        if os.path.exists(full):
            entries[posix(rel)] = full
        else:
            print(f"[WARN] Not found, skipped: {full}")
    if os.path.isdir(args.extras):
        for folder, _, names in os.walk(args.extras):
            for fname in sorted(names):
                if fname.startswith("."):
                    continue  # .gitattributes, .gitkeep...: repository files, not part of the patch
                full = os.path.join(folder, fname)
                rel = posix(os.path.relpath(full, args.extras))
                if rel in entries:
                    print(f"[INFO] {rel}: taken from {args.extras} instead of {args.src}")
                entries[rel] = full

    print(f"Writing {args.out}...")
    with zipfile.ZipFile(args.out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for rel in sorted(entries):
            z.write(entries[rel], f"{args.name}/{rel}")

    print(f"[OK] {args.out} ({os.path.getsize(args.out) / (1024 * 1024):.2f} MB, {len(entries)} files)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
