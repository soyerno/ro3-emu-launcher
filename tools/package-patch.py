#!/usr/bin/env python3
"""Packages the published launcher and the files it needs next to it into a patch ZIP.

Usage: python tools/package-patch.py [--src dist] [--out ro3-launcher-patch.zip] [--name ro3-launcher]

--src   folder holding the published launcher (publish.cmd writes dist) plus RO3Emu.exe
--out   ZIP to write
--name  top-level folder inside the ZIP
"""
import argparse
import os
import sys
import zipfile

# (path relative to --src, required)
FILES = [
    ("Ro3Launcher.exe", True),
    ("RO3Emu.exe", True),
    ("launcher.json", True),
    ("news.json", False),
    ("cloudflared.exe", False),
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--src", default="dist")
    parser.add_argument("--out", default="ro3-launcher-patch.zip")
    parser.add_argument("--name", default="ro3-launcher")
    args = parser.parse_args()

    missing = [rel for rel, required in FILES if required and not os.path.exists(os.path.join(args.src, rel))]
    if missing:
        print(f"[ERROR] Required files not found in {args.src}: {', '.join(missing)}")
        return 1

    print(f"Writing {args.out}...")
    with zipfile.ZipFile(args.out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for rel, _ in FILES:
            full = os.path.join(args.src, rel)
            if os.path.exists(full):
                z.write(full, f"{args.name}/{rel.replace(os.sep, '/')}")
            else:
                print(f"[WARN] Not found, skipped: {full}")

    print(f"[OK] {args.out} ({os.path.getsize(args.out) / (1024 * 1024):.2f} MB)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
