#!/usr/bin/env python3
"""Fail-closed local heuristic gate for public JobRadar Git commits/pushes.

Never logs filenames or matching contents: their very presence may be sensitive.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

PATH_BLOCK = re.compile(
    r"(?i)(?:^|/)(?:reports?|exports?|private|personal|credentials?|"
    r"secrets?|backups?|snapshots?)(?:/|$)|(?:^|/)[^/]*"
    r"(?:\.env(?:\.[^/]*)?|\.jsonl|\.csv|\.pdf|\.docx|\.xlsx|"
    r"\.pfx|\.p12|\.pem|\.key|\.sqlite|\.db|\.dump|\.bak|\.log|\.zip|\.7z)$"
)
SIGNATURES = {
    "PRIVATE_KEY": re.compile(rb"-----BEGIN (?:OPENSSH |RSA |EC |DSA )?PRIVATE KEY-----"),
    "SECRET_TOKEN": re.compile(
        rb"(?i)\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{15,}|"
        rb"sk-[A-Za-z0-9_-]{20,})\b"
    ),
    "CREDENTIAL_ASSIGNMENT": re.compile(
        rb"(?i)\b(?:password|pwd|clientsecret|apisecret)\s*[:=]\s*"
        rb"(?!\$|\{|<|null\b|change_me\b|example\b|placeholder\b)"
        rb"[^\s;'\\"]{8,}"
    ),
    "CONNECTION_CREDENTIAL": re.compile(
        rb"(?i)\b(?:postgres(?:ql)?|mongodb(?:\+srv)?)://[^\s/@:]+:[^\s/@]+@"
    ),
    "LITERAL_APPLICATION_URL": re.compile(rb"(?i)--employment-url\s*=\s*https?://\S+"),
    "PERSONAL_PHONE": re.compile(rb"(?<![0-9])\+?380(?:[\s().-]*[0-9]){9}(?![0-9])"),
}
EMAIL = re.compile(rb"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")
EXAMPLE_EMAIL = re.compile(rb"(?i)@(?:example\.(?:com|org|net)|test\.invalid)$")
MAX_FILE_BYTES = 1_000_000


class GuardError(Exception):
    """Errors must not echo private path contents or Git command arguments."""


def git(repo: Path, *args: str) -> bytes:
    p = subprocess.run(
        ["git", "-C", str(repo), *args],
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        check=False,
    )
    if p.returncode:
        raise GuardError("Git source inspection failed. Fail closed.")
    return p.stdout


def paths_from_nul(data: bytes) -> list[str]:
    return [s.decode("utf-8", "surrogateescape") for s in data.split(b"\0") if s]


def scan_blob(path: str, content: bytes) -> set[str]:
    failures: set[str] = set()
    if (
        not path
        or any(ord(ch) < 32 for ch in path)
        or PATH_BLOCK.search(path.replace("\\", "/"))
    ):
        return {"FILE_PATH"}
    if len(content) > MAX_FILE_BYTES or b"\0" in content:
        return {"BINARY_OR_OVERSIZE"}
    for name, pattern in SIGNATURES.items():
        if pattern.search(content):
            failures.add(name)
    if any(not EXAMPLE_EMAIL.search(match.group()) for match in EMAIL.finditer(content)):
        failures.add("EMAIL_ADDRESS")
    return failures


def staged(repo: Path) -> tuple[int, set[str]]:
    files = paths_from_nul(git(repo, "diff", "--cached", "--name-only", "-z", "--diff-filter=ACMR", "--"))
    errors: set[str] = set()
    for path in files:
        # If a staged file is deleted it does not introduce sensitive content.
        if PATH_BLOCK.search(path.replace("\\", "/")):
            errors.add("FILE_PATH")
            continue
        errors.update(scan_blob(path, git(repo, "show", ":" + path)))
    return len(files), errors


def branch(repo: Path, base_ref: str) -> tuple[int, set[str]]:
    # Scan EVERY introduced commit, not only HEAD: otherwise a subsequent
    # deletion could conceal a published historical blob.
    base = git(repo, "merge-base", base_ref, "HEAD").decode("ascii").strip()
    commits = git(repo, "rev-list", "--reverse", base + "..HEAD").decode("ascii").splitlines()
    seen = 0
    errors: set[str] = set()
    for commit in commits:
        files = paths_from_nul(
            git(repo, "diff-tree", "--no-commit-id", "--name-only", "-r", "-z", "--diff-filter=ACMR", commit)
        )
        for path in files:
            seen += 1
            if PATH_BLOCK.search(path.replace("\\", "/")):
                errors.add("FILE_PATH")
                continue
            errors.update(scan_blob(path, git(repo, "show", commit + ":" + path)))
    return seen, errors


def main() -> int:
    parser = argparse.ArgumentParser(description="Local public-repository privacy heuristic.")
    parser.add_argument("--mode", choices=("staged", "branch"), default="staged")
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    try:
        repo = args.repo.resolve()
        git(repo, "rev-parse", "--show-toplevel")
        count, errors = staged(repo) if args.mode == "staged" else branch(repo, args.base)
    except (GuardError, OSError, UnicodeError):
        print("JobRadar publication safety: BLOCKED (inspection unavailable).", file=sys.stderr)
        return 4
    if errors:
        # Rule names only. Never echo matched strings or even file paths.
        print("JobRadar publication safety: BLOCKED (" + ", ".join(sorted(errors)) + ").", file=sys.stderr)
        print("Review locally; never paste sensitive material into a public PR.", file=sys.stderr)
        return 4
    print(f"JobRadar publication safety: PASS ({count} scanned files; heuristic only).")
    print("Manual diff, PR description and historical commit review remain mandatory.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
