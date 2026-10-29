"""Tests for tools/release.py: python -m unittest tools/test_release.py (from the repository root)."""
import os
import subprocess
import sys
import tempfile
import unittest

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "release.py")
SHA = "a" * 64

BASE = f"""# community release
TAG_PREFIX=v
PACKAGE_NAME=ro3-launcher-patch
RELEASE_TITLE=RO3 Launcher
RO3EMU_REPO=aethai/ro3emu-server
RO3EMU_TAG=v1.0.19
RO3EMU_ZIP_SHA256={SHA}
"""


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.root = tempfile.mkdtemp(prefix="ro3launcher-release-")
        self.write("release.env", BASE)
        self.write("release-notes.md", "Launcher {TAG}\n- {RO3EMU}\nSHA-256: {SHA256}\n")

    def write(self, rel, text):
        path = os.path.join(self.root, rel)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w", encoding="utf-8") as f:
            f.write(text)

    def run_script(self, *args):
        return subprocess.run([sys.executable, "-I", SCRIPT, "--root", self.root, *args],
                              capture_output=True, text=True, check=False)

    def settings(self, *args):
        result = self.run_script("settings", *args)
        self.assertEqual(0, result.returncode, result.stderr)
        return dict(line.split("=", 1) for line in result.stdout.splitlines())

    def test_main_settings_are_read(self):
        settings = self.settings("--tag", "v1.2.0")
        self.assertEqual("ro3-launcher-patch", settings["PACKAGE_NAME"])
        self.assertEqual("", settings["CLOUDFLARED_TAG"])

    def test_brand_overrides_only_its_keys(self):
        self.write("brand/release.env", f"TAG_PREFIX=konsor-v\nPACKAGE_NAME=konsor-ro3-patch\n"
                                        f"CLOUDFLARED_TAG=2026.10.5\nCLOUDFLARED_SHA256={SHA}\n")
        settings = self.settings("--tag", "konsor-v1.0.0")
        self.assertEqual("konsor-ro3-patch", settings["PACKAGE_NAME"])
        self.assertEqual("2026.10.5", settings["CLOUDFLARED_TAG"])
        self.assertEqual("aethai/ro3emu-server", settings["RO3EMU_REPO"])

    def test_a_tag_of_the_other_branch_is_refused(self):
        self.assertNotEqual(0, self.run_script("settings", "--tag", "konsor-v1.0.0").returncode)
        self.write("brand/release.env", "TAG_PREFIX=konsor-v\n")
        refused = self.run_script("settings", "--tag", "v1.0.0")
        self.assertNotEqual(0, refused.returncode)
        self.assertIn("konsor-v", refused.stderr)

    def test_bad_settings_are_refused(self):
        for bad in ["RO3EMU_ZIP_SHA256=1234\n", "PACKAGE_NAME=My Patch\n", "TYPO_KEY=1\n",
                    "CLOUDFLARED_TAG=2026.10.5\n", "not a setting\n"]:
            with self.subTest(bad=bad):
                self.write("brand/release.env", bad)
                self.assertNotEqual(0, self.run_script("settings").returncode)

    def test_a_missing_required_key_is_refused(self):
        self.write("release.env", BASE.replace("RO3EMU_TAG=v1.0.19\n", ""))
        result = self.run_script("settings")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("RO3EMU_TAG", result.stderr)

    def test_notes_fill_the_placeholders_and_the_brand_template_wins(self):
        result = self.run_script("notes", "TAG=v1.2.0", "RO3EMU=aethai/ro3emu-server v1.0.19", f"SHA256={SHA}")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(f"Launcher v1.2.0\n- aethai/ro3emu-server v1.0.19\nSHA-256: {SHA}\n", result.stdout)

        self.write("brand/release-notes.md", "Parche {TAG}\n")
        self.assertEqual("Parche konsor-v1.0.0\n", self.run_script("notes", "TAG=konsor-v1.0.0").stdout)

    def test_a_placeholder_without_a_value_is_refused(self):
        result = self.run_script("notes", "TAG=v1.2.0")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("RO3EMU", result.stderr)


if __name__ == "__main__":
    unittest.main()
