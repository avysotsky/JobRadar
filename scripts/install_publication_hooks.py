#!/usr/bin/env python3
"""Opt-in, worktree-scoped local Git privacy hooks; never modifies remote GitHub."""
import pathlib
import subprocess
import sys

repo = pathlib.Path(__file__).resolve().parent.parent


def git(*args):
    return subprocess.run(["git", "-C", str(repo), *args], capture_output=True, text=True)


for file in (".githooks/pre-commit", ".githooks/pre-push", "scripts/publication_guard.py"):
    if not (repo / file).is_file():
        sys.exit("Expected safety hook component is missing; refusing installation.")

worktrees = git("worktree", "list", "--porcelain")
if worktrees.returncode:
    sys.exit("Cannot inspect Git worktrees; refusing installation.")
multiple = sum(line.startswith("worktree ") for line in worktrees.stdout.splitlines()) > 1
if multiple:
    # Never set a shared core.hooksPath: it would also affect the pinned ops worktree.
    shared = git("config", "--local", "--get", "core.hooksPath")
    if shared.returncode not in (0, 1) or shared.stdout.strip():
        sys.exit("Shared hooksPath already exists; review configuration manually.")
    current = git("config", "--worktree", "--get", "core.hooksPath")
    if current.returncode not in (0, 1):
        # Older repos may need the Git worktree-config extension first.
        current = None
    if current and current.stdout.strip() not in ("", ".githooks"):
        sys.exit("Custom worktree-local hooksPath exists; refusing to overwrite it.")
    if git("config", "--local", "extensions.worktreeConfig", "true").returncode:
        sys.exit("Cannot enable Git worktree-scoped configuration.")
    if git("config", "--worktree", "core.hooksPath", ".githooks").returncode:
        sys.exit("Cannot set worktree-local hooksPath.")
    print("Hooks enabled for THIS worktree only; other worktrees unchanged.")
else:
    current = git("config", "--local", "--get", "core.hooksPath")
    if current.returncode not in (0, 1):
        sys.exit("Cannot inspect existing hooksPath.")
    if current.stdout.strip() not in ("", ".githooks"):
        sys.exit("Existing hooksPath detected; refusing to overwrite it.")
    if git("config", "--local", "core.hooksPath", ".githooks").returncode:
        sys.exit("Cannot set local hooksPath.")
    print("Hooks enabled for this standalone Git checkout.")

print("Local pre-commit and pre-push publication guard enabled (opt-in).")
print("Heuristic only; manually review commits, PR metadata and historical objects.")
