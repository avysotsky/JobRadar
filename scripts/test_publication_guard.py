#!/usr/bin/env python3
"""Local synthetic tests for the publication safety check; no real user data."""
import pathlib
import subprocess
import sys
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).with_name("publication_guard.py")


class PrivacyGateTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="jobradar-privacy-synthetic-")
        self.repo = pathlib.Path(self.temp.name)
        self.git("init", "-q")
        self.git("config", "user.name", "Synthetic Developer")
        self.git("config", "user.email", "synthetic@example.com")
        self.path = self.repo / "synthetic.cs"
        self.write("class Example {}")
        self.git("add", "--", "synthetic.cs")
        self.git("commit", "-qm", "Synthetic initial baseline")
        self.base = self.git("rev-parse", "HEAD").strip()

    def tearDown(self):
        self.temp.cleanup()

    def git(self, *args):
        return subprocess.check_output(["git", "-C", str(self.repo), *args], stderr=subprocess.DEVNULL).decode()

    def write(self, content):
        self.path.write_text(content, encoding="utf-8")

    def gate(self, mode="staged"):
        return subprocess.run(
            [sys.executable, str(SCRIPT), "--repo", str(self.repo), "--mode", mode, "--base", self.base],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        ).returncode

    def test_clean_stage(self):
        self.write('class Example { /* example@example.org */ }')
        self.git("add", "synthetic.cs")
        self.assertEqual(self.gate(), 0)

    def test_literal_employment_action_url_is_blocked(self):
        text = "--employment-url=" + "https://djinni.co/jobs/00000-synthetic/"
        self.write(text)
        self.git("add", "synthetic.cs")
        self.assertEqual(self.gate(), 4)

    def test_synthetic_private_email_is_blocked(self):
        text = "candidate@" + "synthetic.invalid"
        self.write(text)
        self.git("add", "synthetic.cs")
        self.assertEqual(self.gate(), 4)

    def test_synthetic_credential_shape_is_blocked(self):
        text = "ghp_" + "A" * 32
        self.write(text)
        self.git("add", "synthetic.cs")
        self.assertEqual(self.gate(), 4)

    def test_report_export_path_is_blocked(self):
        report = self.repo / "reports" / "vacancies.jsonl"
        report.parent.mkdir()
        report.write_text('{}\n')
        self.git("add", "-f", "--", "reports/vacancies.jsonl")
        self.assertEqual(self.gate(), 4)

    def test_branch_history_catches_introduced_then_removed(self):
        text = "--employment-url=" + "https://djinni.co/jobs/00000-synthetic/"
        self.write(text)
        self.git("add", "synthetic.cs")
        self.git("commit", "-qm", "Add synthetic blocked fixture")
        self.write("class Example {}")
        self.git("add", "synthetic.cs")
        self.git("commit", "-qm", "Remove synthetic blocked fixture")
        self.assertEqual(self.gate("branch"), 4)


    def test_installer_does_not_change_other_worktree(self):
        import shutil
        other = pathlib.Path(self.temp.name) / "ops"
        self.git("worktree", "add", "-q", "-b", "synthetic-ops", str(other))
        (self.repo / "scripts").mkdir(exist_ok=True)
        (self.repo / ".githooks").mkdir(exist_ok=True)
        shutil.copy(SCRIPT, self.repo / "scripts" / "publication_guard.py")
        installer = pathlib.Path(__file__).with_name("install_publication_hooks.py")
        shutil.copy(installer, self.repo / "scripts" / "install_publication_hooks.py")
        for file in ("pre-commit", "pre-push"):
            (self.repo / ".githooks" / file).write_text("#!/bin/sh\nexit 0\n")
        p = subprocess.run([sys.executable, str(self.repo / "scripts" / "install_publication_hooks.py")],
                           capture_output=True, text=True)
        self.assertEqual(p.returncode, 0)
        self.assertEqual(self.git("config", "--get", "core.hooksPath").strip(), ".githooks")
        other_value = subprocess.run(["git", "-C", str(other), "config", "--get", "core.hooksPath"],
                                     capture_output=True, text=True)
        self.assertEqual(other_value.returncode, 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)
