#!/usr/bin/env python3
"""RO3 Launcher DevKit - server connectivity check.

Usage: python tools/test_server.py [SERVER_URL] [--micro-port 20000] [--game-port 33100]

SERVER_URL defaults to the "server" of launcher.json next to the repository root.
Checks the HTTP endpoints the launcher uses and the TCP ports of the server host.
"""
import argparse
import json
import os
import socket
import sys
import urllib.error
import urllib.parse
import urllib.request

HEADERS = {"User-Agent": "Ro3Launcher-DevKit/1.0"}
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load_launcher_json():
    try:
        with open(os.path.join(ROOT, "launcher.json"), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def check_http(name, url):
    try:
        req = urllib.request.Request(url, headers=HEADERS)
        with urllib.request.urlopen(req, timeout=5) as resp:
            print(f" [OK] {name}: HTTP {resp.status}")
            return True
    except urllib.error.HTTPError as e:
        print(f" [WARN] {name}: HTTP {e.code}")
    except Exception as e:
        print(f" [FAIL] {name}: {e}")
    return False


def check_port(host, port):
    try:
        with socket.create_connection((host, port), timeout=2.0):
            return True
    except OSError:
        return False


def main():
    config = load_launcher_json()
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("server", nargs="?", default=config.get("server"))
    parser.add_argument("--micro-port", type=int, default=20000)
    parser.add_argument("--game-port", type=int, default=33100)
    args = parser.parse_args()

    parsed = urllib.parse.urlparse(args.server or "")
    if parsed.scheme not in ("http", "https") or not parsed.hostname:
        print('Pass a server URL (http(s)://host:port) or set "server" in launcher.json.')
        return 1

    server = args.server.rstrip("/")
    repository = (config.get("repository") or server + "/").rstrip("/")
    print(f"Checking {server}\n")

    print("[1] HTTP endpoints:")
    check_http("Server status", server + "/ro3emu/status")
    check_http("Gate key", server + "/ro3emu/gate-key.pem")
    check_http("News feed", repository + "/news.json")

    print("\n[2] TCP ports on the server host:")
    for port, desc in ((parsed.port or (443 if parsed.scheme == "https" else 80), "HTTP"), (args.micro_port, "MicroGate"), (args.game_port, "GameGate")):
        state = "open" if check_port(parsed.hostname, port) else "closed or unreachable"
        print(f"     {desc:<10} {parsed.hostname}:{port} -> {state}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
