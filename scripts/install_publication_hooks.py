#!/usr/bin/env python3
"""Explicit opt-in, local-only Git hooks installation; no remote changes."""
import pathlib
import subprocess
import sys

repo = pathlib.Path(__file__).resolve().parent.parent

def git(*args):
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True)

existing = git("config", "--local", "--get", "core.hooksPath")
if existing.returncode not in (0, 1):
    sys.exit("Cannot inspect current Git hooks; refusing installation.")
if existing.stdout.strip() not in ("", ".githooks"):
    sys.exit("Existing hooksPath detected: refusing to overwrite local hooks.")
for file in (".githooks/pre-commit", ".githooks/pre-push", "scripts/publication_guard.py"):
    if not (repo / file).is_file():
        sys.exit("Expected safety hook component is missing; refusing installation.")
if git("config", "--local", "core.hooksPath", ".githooks").returncode:
    sys.exit("Failed to set local hooksPath.")
print("Local pre-commit and pre-push safety hooks enabled (opt-in).")
print("Heuristic only; manually review commits, PR metadata and historical objects.")
