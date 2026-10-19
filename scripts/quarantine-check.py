#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Fail when a quarantined messaging cell or guarantee links an issue that is already closed.

A test that exposes a product defect is quarantined: its transport manifest cell or catalogued guarantee is marked
Unsupported with an issue URL, and the test skips. Fixing the defect closes the issue, but nothing un-skips the test,
so the fix goes unproven and a later regression goes unseen. The scheduled run calls this with the snapshot the
`ConformanceEvidenceExportTests` write (`make messaging-quarantine-check`), asks GitHub for the state of every linked
issue through `gh api`, read-only, and fails naming each cell that still points at a closed issue. A guarantee with
no test yet links its tracking issue the same way, so a closed tracking issue fails the check too.

Exit codes: 0 every linked issue is open; 3 an issue is closed, missing, or not a GitHub issue URL; 1 the snapshot
is unreadable or `gh` failed; 2 usage error.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path

_ISSUE_URL = re.compile(r"^https://github\.com/(?P<owner>[^/\s]+)/(?P<repo>[^/\s]+)/(?:issues|pull)/(?P<number>\d+)/?$")


@dataclass(frozen=True)
class QuarantinedEntry:
    """One cell or guarantee that a linked issue holds out of the evidence."""

    label: str
    status: str


@dataclass
class LinkedIssue:
    url: str
    entries: list[QuarantinedEntry] = field(default_factory=list)


@dataclass(frozen=True)
class IssueState:
    state: str
    reason: str
    title: str


class GhError(RuntimeError):
    pass


def collect(snapshot: dict) -> dict[str, LinkedIssue]:
    """Groups every quarantined manifest cell and guarantee by the issue it links."""
    issues: dict[str, LinkedIssue] = {}

    def link(url: str | None, entry: QuarantinedEntry) -> None:
        key = (url or "").strip()
        issues.setdefault(key, LinkedIssue(key)).entries.append(entry)

    for provider in snapshot.get("providers", []):
        for scenario, cell in sorted(provider.get("cells", {}).items()):
            if cell.get("status") == "Unsupported":
                link(cell.get("issueUrl"), QuarantinedEntry(f"transport manifest: {provider['name']} / {scenario}", "Unsupported"))

    for guarantee in snapshot.get("guarantees", []):
        status = guarantee.get("status")
        if status in ("Unsupported", "NoTestYet"):
            link(guarantee.get("issueUrl"), QuarantinedEntry(f"guarantee {guarantee['id']}", status))

    return issues


def fetch_state(gh: str, owner: str, repo: str, number: str) -> IssueState | None:
    """Returns the issue's state, or None when GitHub reports it missing."""
    try:
        completed = subprocess.run(
            [gh, "api", f"repos/{owner}/{repo}/issues/{number}"],
            capture_output=True,
            text=True,
            timeout=60,
            check=False,
        )
    except (OSError, subprocess.TimeoutExpired) as error:
        raise GhError(f"gh api repos/{owner}/{repo}/issues/{number}: {error}") from error

    if completed.returncode != 0:
        if "HTTP 404" in completed.stderr or "HTTP 410" in completed.stderr:
            return None
        raise GhError(f"gh api repos/{owner}/{repo}/issues/{number} exited {completed.returncode}: {completed.stderr.strip()}")

    try:
        issue = json.loads(completed.stdout)
    except json.JSONDecodeError as error:
        raise GhError(f"gh api repos/{owner}/{repo}/issues/{number} returned invalid JSON: {error}") from error

    return IssueState(str(issue.get("state", "")), str(issue.get("state_reason") or ""), str(issue.get("title", "")))


def render_entries(issue: LinkedIssue) -> list[str]:
    return [f"  - {entry.label} ({entry.status})" for entry in issue.entries]


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--snapshot", required=True, type=Path, help="snapshot.json written by ConformanceEvidenceExport")
    parser.add_argument("--gh", default="gh", help="the GitHub CLI to query issue state with (default: gh)")
    args = parser.parse_args(argv)

    try:
        snapshot = json.loads(args.snapshot.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"ERROR: cannot read the conformance snapshot {args.snapshot}: {error}", file=sys.stderr)
        return 1

    issues = collect(snapshot)
    entry_count = sum(len(issue.entries) for issue in issues.values())
    closed: list[tuple[LinkedIssue, IssueState]] = []
    missing: list[LinkedIssue] = []
    unverifiable: list[LinkedIssue] = []
    open_count = 0

    try:
        for url in sorted(issues):
            issue = issues[url]
            match = _ISSUE_URL.match(url)
            if match is None:
                unverifiable.append(issue)
                continue

            state = fetch_state(args.gh, match["owner"], match["repo"], match["number"])
            if state is None:
                missing.append(issue)
            elif state.state.lower() == "closed":
                closed.append((issue, state))
            else:
                open_count += 1
    except GhError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1

    lines = [
        "## Messaging quarantine check",
        "",
        f"Checked {len(issues)} linked issue(s) for {entry_count} quarantined cell(s) and guarantee(s); {open_count} open.",
    ]

    if closed:
        lines += ["", "### Closed issues that still quarantine a cell", ""]
        lines += ["Each cell below still skips its test. Prove the fix by marking it Supported (or Bound), or reopen the issue.", ""]
        for issue, state in closed:
            reason = f", {state.reason}" if state.reason else ""
            lines.append(f"- {issue.url} (closed{reason}): {state.title}")
            lines += render_entries(issue)

    if missing:
        lines += ["", "### Linked issues GitHub cannot find", ""]
        for issue in missing:
            lines.append(f"- {issue.url}")
            lines += render_entries(issue)

    if unverifiable:
        lines += ["", "### Links that are not a GitHub issue URL", ""]
        for issue in unverifiable:
            lines.append(f"- {issue.url or '(no issue URL)'}")
            lines += render_entries(issue)

    failed = bool(closed or missing or unverifiable)
    lines += ["", "Result: **fail**" if failed else "Result: **pass**, every quarantine links an open issue."]
    print("\n".join(lines))
    return 3 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
