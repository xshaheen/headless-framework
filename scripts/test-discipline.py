#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Ratchet on test code that waits real time, reads the wall clock, sets its own timeout or retry, or skips silently.

A real wait costs wall time on every run and turns into a flake once a loaded runner misses the margin; a wall-clock
read makes the result depend on when the test ran; a per-test timeout or retry hides a race the code under test may
really have; and a skip with no issue or capability behind it never comes back. The repository had hundreds of these
before the rule existed, so they are frozen rather than fixed: eng/test-discipline-baseline.txt holds the count per
file and rule, a file may not rise above its line, a file without a line may have none, and a count that falls must
be written back (`make test-discipline-baseline`) so the gain is kept.

Rules, matched in test code (C# under tests/, and the dashboard SPAs' test files) outside line comments:
  real-wait     Task.Delay (except Timeout.Infinite waits, which end only on cancellation), Thread.Sleep,
                SpinWait.SpinUntil, TimeProvider.System.Delay; setTimeout(fn, ms) used as a sleep in TypeScript.
  wall-clock    DateTime/DateTimeOffset.Now/UtcNow, Stopwatch.StartNew/GetTimestamp, Environment.TickCount,
                TimeProvider.System.GetUtcNow/GetTimestamp; Date.now(), performance.now(), new Date() in TypeScript.
  test-timeout  A Timeout on [Fact]/[Theory], [RetryFact]/[RetryTheory]; a timeout or retry option on it()/test().
  skip          Skip = on a test attribute, Assert.Skip(...), it.skip/test.skip/describe.skip/.todo, xit/xdescribe,
                unless the skip names an issue (#123 or an issues/pull URL) or is gated on a capability:
                Assert.SkipWhen/SkipUnless, an attribute's SkipUnless/SkipWhen, it.skipIf/runIf, or an
                Assert.Skip under an if, else, or case.

A line opts out with a reason on it or on the line above: `// real-wait: <why>`, `// wall-clock: <why>`,
`// test-timeout: <why>`, `// skip: <capability>`, or `// quarantine: #123 <why>` (the issue is required). Use it for a test of real timeout
behavior or a subprocess or network fixture, never to pass the check. The messaging fast tier (its core and in-memory
unit projects and its harness) runs in-process on simulated time, so a real-wait opt-out does not count there.

The rules are a script and not an analyzer because a banned-symbol analyzer (RS0030) has no per-file baseline: adopting
it would mean a suppression at each existing site, which freezes nothing and teaches the wrong habit.

Commands:
  check     Compare the counts with the baseline; exit 3 naming each rise, new file, or fall. --base REF limits it to
            the test files changed against the merge base with REF, uncommitted and untracked work included, or to
            every file when the baseline itself changed. Either way a baseline line above the merge base's fails,
            and a renamed file is held to its old line until the rewrite moves it.
  baseline  Rewrite the baseline: each line falls to the current count and never rises; a renamed file keeps its
            line under the new name. With no baseline file it records the current counts.

Exit codes: 0 holds; 3 a count rose, a new file has one, or a count fell; 1 git failed; 2 usage error.
Standard library only.
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from collections.abc import Iterable
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
BASELINE = REPO_ROOT / "eng" / "test-discipline-baseline.txt"
BASELINE_COMMAND = "make test-discipline-baseline"

# Simulated-time projects where no real wait may hide behind an opt-out.
STRICT_REAL_WAIT_PREFIXES = (
    "tests/Headless.Messaging.Tests.Unit/",
    "tests/Headless.Messaging.InMemory.Tests.Unit/",
    "tests/Headless.Messaging.Tests.Harness/",
)

RULES = ("real-wait", "wall-clock", "test-timeout", "skip")

FIXES = {
    "real-wait": "await the signal the test is waiting for (a TaskCompletionSource, a channel read, a persisted row) "
    "or advance a FakeTimeProvider; in TypeScript use vi.useFakeTimers and vi.advanceTimersByTime or await the element",
    "wall-clock": "inject TimeProvider and use FakeTimeProvider, assert on events and counts rather than elapsed time; "
    "in TypeScript use vi.setSystemTime",
    "test-timeout": "remove the per-test timeout or retry and fix the race that needed it; the module timeout is the "
    "ceiling",
    "skip": "name the tracking issue in the skip reason (#123 or its URL), gate it on a capability "
    "(Assert.SkipWhen/SkipUnless, it.skipIf), or mark it `// quarantine: #123 <why>`",
}

# A quarantine is a skip held against a defect, so its marker must name the issue that tracks the defect.
OPT_OUT = re.compile(r"//\s*(?:(real-wait|wall-clock|test-timeout|skip)\s*:\s*\S|(quarantine)\s*:\s*(?:#\d|\S*/(?:issues|pull)/\d))")

CS_PATTERNS = {
    "real-wait": re.compile(
        r"\bTask\.Delay\s*\((?!\s*Timeout\.Infinite)|\bThread\.Sleep\s*\(|\bSpinWait\.SpinUntil\s*\(|"
        r"\bTimeProvider\.System\.Delay\s*\("
    ),
    "wall-clock": re.compile(
        r"\bDateTime(?:Offset)?\.(?:Utc)?Now\b|\bStopwatch\.(?:StartNew|GetTimestamp)\s*\(|\bEnvironment\.TickCount|"
        r"\bTimeProvider\.System\.(?:GetUtcNow|GetTimestamp)\s*\("
    ),
    "test-timeout": re.compile(r"\[\s*(?:Fact|Theory)\s*\([^\]]*\bTimeout\s*=|\[\s*Retry(?:Fact|Theory)\b"),
    "skip": re.compile(
        r"\[\s*(?:Fact|Theory|InlineData|MemberData|ClassData|Retry(?:Fact|Theory))\b[^\]]*\bSkip\s*=|"
        r"\bAssert\.Skip\s*\("
    ),
}

TS_PATTERNS = {
    "real-wait": re.compile(r"\bsetTimeout\s*\(\s*\w+\s*,\s*[\d_]+\s*\)"),
    "wall-clock": re.compile(r"\bDate\.now\s*\(|\bperformance\.now\s*\(|\bnew\s+Date\s*\(\s*\)"),
    "test-timeout": re.compile(r"\b(?:it|test)(?:\.\w+)?\([^\n]*,\s*\{[^}\n]*\b(?:timeout|retry)\s*:"),
    "skip": re.compile(r"\b(?:it|test|describe)\.(?:skip|todo)\s*\(|\bx(?:it|describe)\s*\("),
}

# The look-behind keeps a URL scheme such as http:// from reading as the start of a comment.
LINE_COMMENT = re.compile(r"(?<!:)//[^\r\n]*")
ISSUE = re.compile(r"#\d+\b|/(?:issues|pull)/\d+")
# xUnit v3 shows an attribute's Skip message only while its SkipUnless/SkipWhen member says so: a capability gate.
CONDITIONAL_ATTRIBUTE = re.compile(r"\bSkip(?:Unless|When)\s*=")
CONDITION = re.compile(r"^\s*(?:\}\s*)?(?:if\b|else\b|case\b|default\s*:)|\?\s*$")


@dataclass(frozen=True)
class Hit:
    rule: str
    line: int


def is_test_file(path: str) -> bool:
    if path.startswith("tests/") and path.endswith(".cs"):
        return "/bin/" not in path and "/obj/" not in path
    if path.startswith("src/") and "/wwwroot/" in path and "/node_modules/" not in path:
        if re.search(r"\.(?:test|spec)\.tsx?$", path):
            return True
        return "/test/" in path and path.endswith((".ts", ".tsx"))
    return False


def _skip_is_reasoned(code_lines: list[str], index: int, start: int) -> bool:
    """A skip names an issue in its own statement, or sits under a condition that states the capability it needs."""
    statement = "\n".join(code_lines[index : index + 4])
    end = statement.find(";", start)
    if ISSUE.search(statement[: end if end >= 0 else len(statement)]):
        return True
    # A braceless `if (...)` puts the skip on the next statement line.
    previous = next((line for line in reversed(code_lines[:index]) if line.strip()), "")
    if CONDITION.search(previous):
        return True
    # Otherwise read the header of the block that encloses the skip: the lines above its opening brace back to the
    # end of the previous statement.
    depth = 0
    for row in range(index - 1, -1, -1):
        line = code_lines[row]
        depth += line.count("}") - line.count("{")
        if depth < 0:
            header: list[str] = [line.split("{", 1)[0]]
            for above in range(row - 1, max(-1, row - 8), -1):
                text = code_lines[above].rstrip()
                if text.endswith((";", "{", "}")):
                    break
                header.append(text)
            return bool(re.search(r"\b(?:if|else|case|when|catch)\b", " ".join(header)))
    return False


def scan_text(path: str, text: str) -> list[Hit]:
    patterns = CS_PATTERNS if path.endswith(".cs") else TS_PATTERNS
    raw_lines = text.splitlines()
    code_lines = LINE_COMMENT.sub("", text).splitlines()
    strict = path.startswith(STRICT_REAL_WAIT_PREFIXES)
    hits: list[Hit] = []
    for index, code in enumerate(code_lines):
        for rule, pattern in patterns.items():
            for match in pattern.finditer(code):
                if rule == "skip" and (
                    _skip_is_reasoned(code_lines, index, match.start()) or CONDITIONAL_ATTRIBUTE.search(code)
                ):
                    continue
                if not (rule == "real-wait" and strict) and _opted_out(raw_lines, index, rule):
                    continue
                hits.append(Hit(rule, index + 1))
    return hits


def _opted_out(raw_lines: list[str], index: int, rule: str) -> bool:
    for candidate in raw_lines[max(0, index - 1) : index + 1]:
        marker = OPT_OUT.search(candidate)
        if marker and (marker.group(1) == rule or (rule == "skip" and marker.group(2))):
            return True
    return False


def git_lines(*args: str) -> list[str]:
    completed = subprocess.run(["git", *args], cwd=REPO_ROOT, capture_output=True, text=True, check=False)
    if completed.returncode != 0:
        raise GitError(f"git {' '.join(args)}: {completed.stderr.strip()}")
    return [line for line in completed.stdout.splitlines() if line]


class GitError(RuntimeError):
    pass


def all_test_files() -> list[str]:
    files = git_lines("ls-files", "--cached", "--others", "--exclude-standard", "--", "tests", "src")
    return sorted({f for f in files if is_test_file(f) and (REPO_ROOT / f).is_file()})


def changed_test_files(base: str) -> tuple[list[str], str]:
    merge_base = git_lines("merge-base", base, "HEAD")[0]
    # --no-renames lists a moved file under both paths, so the old path's line is checked too.
    changed = git_lines("diff", "--no-renames", "--name-only", merge_base, "--", "tests", "src")
    changed += git_lines("ls-files", "--others", "--exclude-standard", "--", "tests", "src")
    return sorted({f for f in changed if is_test_file(f)}), merge_base


def count(files: Iterable[str]) -> dict[tuple[str, str], list[int]]:
    counts: dict[tuple[str, str], list[int]] = {}
    for file in files:
        path = REPO_ROOT / file
        if not path.is_file():
            continue
        for hit in scan_text(file, path.read_text(encoding="utf-8", errors="replace")):
            counts.setdefault((file, hit.rule), []).append(hit.line)
    return counts


def read_baseline() -> dict[tuple[str, str], int] | None:
    if not BASELINE.exists():
        return None
    return parse_baseline(BASELINE.read_text(encoding="utf-8"))


def base_baseline(merge_base: str | None) -> dict[tuple[str, str], int] | None:
    """The baseline as the merge base has it, or None when there is no base or it had no baseline yet."""
    if merge_base is None:
        return None
    relative = BASELINE.relative_to(REPO_ROOT).as_posix()
    shown = subprocess.run(
        ["git", "show", f"{merge_base}:{relative}"], cwd=REPO_ROOT, capture_output=True, text=True, check=False
    )
    return parse_baseline(shown.stdout) if shown.returncode == 0 else None


def parse_baseline(text: str) -> dict[tuple[str, str], int]:
    baseline: dict[tuple[str, str], int] = {}
    for number, raw in enumerate(text.splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split(" ", 2)
        if len(parts) != 3 or parts[0] not in RULES or not parts[1].isdigit():
            raise ValueError(f"{BASELINE.name}:{number}: '{line}' is not '<rule> <count> <path>'")
        baseline[(parts[2], parts[0])] = int(parts[1])
    return baseline


def write_baseline(entries: dict[tuple[str, str], int]) -> None:
    header = BASELINE.read_text(encoding="utf-8").splitlines() if BASELINE.exists() else []
    comments = [line for line in header if line.startswith("#")] or [
        "# Frozen test-discipline findings per file: <rule> <count> <path>. scripts/test-discipline.py states the rules.",
        f"# A count may only fall. When it does, `{BASELINE_COMMAND}` writes the lower count back.",
    ]
    rows = sorted(entries.items(), key=lambda item: (item[0][0], RULES.index(item[0][1])))
    body = [f"{rule} {value} {file}" for (file, rule), value in rows if value > 0]
    BASELINE.write_text("\n".join([*comments, *body]) + "\n", encoding="utf-8")


def check(base: str | None) -> int:
    baseline = read_baseline()
    if baseline is None:
        print(f"[test-discipline] {BASELINE.relative_to(REPO_ROOT)} is missing; run `{BASELINE_COMMAND}`.", file=sys.stderr)
        return 3
    baseline_path = BASELINE.relative_to(REPO_ROOT).as_posix()
    if base and baseline_path not in git_lines("diff", "--name-only", git_lines("merge-base", base, "HEAD")[0]):
        files, merge_base = changed_test_files(base)
        scope = set(files)
    else:
        # A changed baseline can hide a rise in a file this branch did not touch, so it is checked whole.
        base = None
        files = all_test_files()
        scope = set(files) | {f for f, _ in baseline}
        merge_base = _merge_base("origin/main")
    counts = count(files)
    renames = renamed_paths(merge_base)

    # The baseline only falls: a line above the merge base's, other than one moved with its file, raised it by hand.
    raised: list[str] = []
    previous = base_baseline(merge_base)
    if previous is not None:
        for (file, rule), value in sorted(baseline.items()):
            before = previous.get((file, rule), previous.get((renames.get(file, ""), rule), 0))
            if value > before:
                raised.append(f"  {file}: {rule} {value}, was {before} on the merge base")

    rises: list[str] = []
    falls: list[str] = []
    broken: set[str] = set()
    moved_away = set(renames.values())
    for key in sorted(set(counts) | {k for k in baseline if k[0] in scope and k[0] not in moved_away}):
        file, rule = key
        lines = counts.get(key, [])
        # A moved file is held to its old line, but the line must move with it: on main, after the merge, no rename
        # is left to detect, so a line still under the old path would fail every later run.
        old = renames.get(file, "")
        moved = key not in baseline and (old, rule) in baseline
        allowed = baseline.get(key, baseline.get((old, rule), 0))
        if len(lines) > allowed:
            where = ", ".join(str(n) for n in lines)
            limit = f"above its baseline of {allowed}" if allowed else "and the file has no baseline line"
            rises.append(f"  {file}: {len(lines)} {rule} (lines {where}), {limit}")
            broken.add(rule)
        elif moved:
            falls.append(f"  {file}: {rule} {len(lines)}, moved from {old} (baseline {allowed})")
        elif len(lines) < allowed:
            falls.append(f"  {file}: {rule} {len(lines)}, baseline {allowed}")

    if rises:
        print("[test-discipline] new findings in test code:", file=sys.stderr)
        print("\n".join(rises), file=sys.stderr)
        for rule in RULES:
            if rule in broken:
                print(f"  {rule}: {FIXES[rule]}.", file=sys.stderr)
        print(
            "  A legitimate case (a test of real timeout behavior, a subprocess or network fixture) takes a reason "
            "comment on the line or the line above, such as `// real-wait: <why>`.",
            file=sys.stderr,
        )
        if any(line.lstrip().startswith(STRICT_REAL_WAIT_PREFIXES) and " real-wait " in line for line in rises):
            print(
                "  The messaging fast tier runs on simulated time, so a `// real-wait:` comment does not apply in its "
                "core and in-memory unit projects or its harness: drive the wait from the simulated clock.",
                file=sys.stderr,
            )
    if falls:
        print(
            f"[test-discipline] counts fell below the baseline or moved with a file; run `{BASELINE_COMMAND}` "
            "to write them back:",
            file=sys.stderr,
        )
        print("\n".join(falls), file=sys.stderr)
    if raised:
        print(
            f"[test-discipline] {baseline_path} rose above the merge base's; the baseline only falls. Restore those "
            "lines and fix the findings, or give each a reason comment:",
            file=sys.stderr,
        )
        print("\n".join(raised), file=sys.stderr)
    if rises or falls or raised:
        return 3
    print(f"[test-discipline] {len(files)} test files hold the baseline{' (changed files only)' if base else ''}.")
    return 0


def _merge_base(ref: str) -> str | None:
    try:
        return git_lines("merge-base", ref, "HEAD")[0]
    except GitError:
        return None


def renamed_paths(merge_base: str | None) -> dict[str, str]:
    """New path to old path for files renamed since the merge base, so a moved file keeps its line."""
    if merge_base is None:
        return {}
    rows = git_lines("diff", "-M", "--name-status", "--diff-filter=R", merge_base)
    return {parts[2]: parts[1] for parts in (row.split("\t") for row in rows) if len(parts) == 3}


def rewrite() -> int:
    baseline = read_baseline()
    counts = {key: len(lines) for key, lines in count(all_test_files()).items()}
    if baseline is None:
        write_baseline(counts)
        print(f"[test-discipline] recorded {sum(counts.values())} findings in {BASELINE.relative_to(REPO_ROOT)}.")
        return 0
    renames = renamed_paths(_merge_base("origin/main"))
    entries: dict[tuple[str, str], int] = {}
    for (file, rule), current in counts.items():
        allowed = baseline.get((file, rule), baseline.get((renames.get(file, ""), rule), 0))
        entries[(file, rule)] = min(current, allowed)
    write_baseline(entries)
    held = sum(entries.values())
    over = sum(counts.values()) - held
    print(f"[test-discipline] baseline holds {held} findings (was {sum(baseline.values())}).")
    if over:
        print(f"[test-discipline] {over} findings above the baseline stay failing; fix them or give each a reason.")
    return 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    check_parser = commands.add_parser("check", help="compare the counts with the baseline")
    check_parser.add_argument("--base", help="check only test files changed against the merge base with this ref")
    commands.add_parser("baseline", help="rewrite the baseline; counts only fall")
    args = parser.parse_args(argv)
    try:
        return check(args.base) if args.command == "check" else rewrite()
    except GitError as error:
        print(f"[test-discipline] {error}", file=sys.stderr)
        return 1
    except ValueError as error:
        print(f"[test-discipline] {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
