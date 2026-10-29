#!/usr/bin/env python3
"""Settings and notes of the GitHub Release that .github/workflows/release.yml publishes.

Every branch publishes its own release: main the community launcher (tags v1.2.0, ...), a branded branch its own
(konsor: konsor-v1.2.0, ...). The settings live in release.env; a branded branch overrides some of its keys in
brand/release.env, and replaces the notes template release-notes.md with brand/release-notes.md (the same rule as
every other brand/ file: main never edits them, so merging main never conflicts).

Usage: python tools/release.py settings [--tag <tag>]   KEY=value lines for $GITHUB_ENV; a tag that does not start
                                                        with this branch's TAG_PREFIX is refused
       python tools/release.py notes KEY=value ...      the notes template with each {KEY} filled in
"""
import argparse
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

REQUIRED = ["TAG_PREFIX", "PACKAGE_NAME", "RELEASE_TITLE", "RO3EMU_REPO", "RO3EMU_TAG", "RO3EMU_ZIP_SHA256"]
OPTIONAL = ["CLOUDFLARED_TAG", "CLOUDFLARED_SHA256"]

SHA256 = re.compile(r"^[0-9a-f]{64}$")
CHECKS = {
    "TAG_PREFIX": (re.compile(r"^[a-z0-9-]*v$"), "letters, digits and dashes ending in v, for example v or konsor-v"),
    "PACKAGE_NAME": (re.compile(r"^[a-z0-9][a-z0-9-]*$"), "lowercase letters, digits and dashes"),
    "RO3EMU_REPO": (re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"), "owner/repository"),
    "RO3EMU_ZIP_SHA256": (SHA256, "64 lowercase hex digits"),
    "CLOUDFLARED_SHA256": (SHA256, "64 lowercase hex digits"),
}


class ReleaseError(Exception):
    pass


def read_env(path):
    """KEY=value lines; blank lines and # comments are skipped. Anything else is an error."""
    values = {}
    with open(path, encoding="utf-8") as f:
        for number, line in enumerate(f, 1):
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            match = re.match(r"^([A-Z0-9_]+)=(.*)$", line)
            if not match:
                raise ReleaseError(f"{path}:{number}: not a KEY=value line")
            if match.group(1) not in REQUIRED + OPTIONAL:
                raise ReleaseError(f"{path}:{number}: unknown setting {match.group(1)}")
            values[match.group(1)] = match.group(2).strip()
    return values


def settings(root, tag=None):
    values = {key: "" for key in OPTIONAL}
    values.update(read_env(os.path.join(root, "release.env")))
    brand = os.path.join(root, "brand", "release.env")
    if os.path.exists(brand):
        values.update(read_env(brand))

    missing = [key for key in REQUIRED if not values.get(key)]
    if missing:
        raise ReleaseError(f"missing settings: {', '.join(missing)}")
    for key, (pattern, meaning) in CHECKS.items():
        if values.get(key) and not pattern.match(values[key]):
            raise ReleaseError(f"{key}={values[key]}: expected {meaning}")
    if bool(values["CLOUDFLARED_TAG"]) != bool(values["CLOUDFLARED_SHA256"]):
        raise ReleaseError("CLOUDFLARED_TAG and CLOUDFLARED_SHA256 go together")
    if tag is not None and not re.match(re.escape(values["TAG_PREFIX"]) + r"[0-9]", tag):
        raise ReleaseError(f"tag {tag} is not a release of this branch: its tags are {values['TAG_PREFIX']}<version>")
    return values


def notes(root, pairs):
    template = os.path.join(root, "brand", "release-notes.md")
    if not os.path.exists(template):
        template = os.path.join(root, "release-notes.md")
    with open(template, encoding="utf-8") as f:
        text = f.read()
    values = dict(pair.split("=", 1) for pair in pairs)
    missing = sorted(set(re.findall(r"\{([A-Z0-9_]+)\}", text)) - values.keys())
    if missing:
        raise ReleaseError(f"{os.path.relpath(template, root)} uses {', '.join(missing)}, which has no value")
    return re.sub(r"\{([A-Z0-9_]+)\}", lambda m: values[m.group(1)], text)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=ROOT)
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("settings").add_argument("--tag")
    commands.add_parser("notes").add_argument("pairs", nargs="*", metavar="KEY=value")
    args = parser.parse_args()
    try:
        if args.command == "settings":
            for key, value in settings(args.root, args.tag).items():
                print(f"{key}={value}")
        else:
            sys.stdout.write(notes(args.root, args.pairs))
    except (ReleaseError, OSError, ValueError) as e:
        print(f"[ERROR] {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
