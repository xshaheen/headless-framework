#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Run verification stages and collect their results into one proof bundle.

An agent's claim that a change works is only as good as the evidence attached to it. This tool
records every stage it runs (command, exit code, duration, full log) and then reduces the raw
outputs (compiler log, TRX files, analyzer report, Cobertura coverage) into summary.json for
machines and summary.md for a pull request body. Standard library only.

Commands:
  run        Run one stage command, tee its output to <dir>/<name>.log, record its result, and
             exit with the command's status.
  summarize  Write <dir>/summary.json and <dir>/summary.md from what the stages left in <dir>. With
             --coverage-gate, also hold the change to the coverage floors and fail the bundle below them.
  failed     Print the test projects whose modules failed in a bundle (the latest verify or test
             bundle by default), one per line, and name the other failed stages on stderr.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]
TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
# MSBuild canonical diagnostic: path(line,col): warning CODE: message [project]
DIAGNOSTIC = re.compile(
    r"^(?P<file>[^(\s][^(]*)\((?P<line>\d+)(?:,\d+)?\): (?P<severity>warning|error) (?P<id>[A-Za-z]+\d+): (?P<message>.*?)(?: \[(?P<project>[^\]]+)\])?$"
)
MAX_LISTED = 50


@dataclass
class Stage:
    name: str
    command: list[str]
    exit_code: int
    seconds: float
    log: str
    # A skipped stage records exit_code -1 and why it did not run, e.g. tests after a failed build.
    note: str = ""


@dataclass
class TestModule:
    module: str
    passed: int = 0
    failed: int = 0
    skipped: int = 0
    failures: list[dict[str, str]] = field(default_factory=list)


_ROOT_PREFIX = REPO_ROOT.as_posix() + "/"


def relative(path: str) -> str:
    # Tools report absolute paths under the checkout, so a prefix strip is enough; resolving every
    # path costs a filesystem call each, and an analyzer report carries tens of thousands.
    return path.removeprefix(_ROOT_PREFIX)


def run_stage(directory: Path, name: str, command: list[str]) -> int:
    directory.mkdir(parents=True, exist_ok=True)
    log_path = directory / f"{name}.log"
    started = time.monotonic()
    with log_path.open("w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=REPO_ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        assert process.stdout is not None
        for line in process.stdout:
            sys.stdout.write(line)
            log.write(line)
        exit_code = process.wait()
    stage = Stage(name, command, exit_code, round(time.monotonic() - started, 1), log_path.name)
    with (directory / "stages.jsonl").open("a", encoding="utf-8") as stages:
        stages.write(json.dumps(asdict(stage)) + "\n")
    return exit_code


def skip_stage(directory: Path, name: str, reason: str) -> int:
    directory.mkdir(parents=True, exist_ok=True)
    print(f"[proof] {name} skipped: {reason}")
    stage = Stage(name, [], -1, 0.0, "", reason)
    with (directory / "stages.jsonl").open("a", encoding="utf-8") as stages:
        stages.write(json.dumps(asdict(stage)) + "\n")
    return 0


def read_stages(directory: Path) -> list[Stage]:
    path = directory / "stages.jsonl"
    if not path.exists():
        return []
    return [Stage(**json.loads(line)) for line in path.read_text(encoding="utf-8").splitlines() if line]


def compiler_diagnostics(directory: Path) -> list[dict[str, str]]:
    seen: set[tuple[str, ...]] = set()
    diagnostics: list[dict[str, str]] = []
    for log in sorted(directory.glob("*.log")):
        for line in log.read_text(encoding="utf-8", errors="replace").splitlines():
            match = DIAGNOSTIC.match(line.strip())
            if not match:
                continue
            key = (match["file"], match["line"], match["id"])
            if key in seen:
                continue
            seen.add(key)
            diagnostics.append(
                {
                    "severity": match["severity"],
                    "id": match["id"],
                    "file": relative(match["file"]),
                    "line": match["line"],
                    "message": match["message"],
                }
            )
    return diagnostics


def analyzer_findings(directory: Path, project_dirs: list[str]) -> list[dict[str, str]]:
    """Findings in the analyzed projects only: `dotnet format` also reports on referenced projects."""
    findings: list[dict[str, str]] = []
    for report in sorted(directory.rglob("format-report.json")):
        for document in json.loads(report.read_text(encoding="utf-8")):
            for change in document.get("FileChanges", []):
                description: str = change.get("FormatDescription", "")
                severity, _, message = description.partition(" ")
                if severity == "hidden":
                    continue
                file = relative(document.get("FilePath", ""))
                if project_dirs and not file.startswith(tuple(project_dirs)):
                    continue
                findings.append(
                    {
                        "severity": severity,
                        "id": change.get("DiagnosticId", ""),
                        "file": file,
                        "line": str(change.get("LineNumber", "")),
                        "message": message.split(": ", 1)[-1],
                    }
                )
    return findings


def test_modules(directory: Path) -> list[TestModule]:
    modules: list[TestModule] = []
    for trx in sorted(directory.rglob("*.trx")):
        root = ET.parse(trx).getroot()
        module = TestModule(module=re.sub(r"_net[\d.]+_\w+$", "", trx.stem))
        for result in root.iterfind(".//t:Results/t:UnitTestResult", TRX_NS):
            outcome = result.get("outcome", "")
            if outcome == "Passed":
                module.passed += 1
            elif outcome in ("Failed", "Error", "Timeout", "Aborted"):
                module.failed += 1
                message = result.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=TRX_NS).strip()
                module.failures.append({"test": result.get("testName", ""), "message": message.splitlines()[0] if message else outcome})
            else:
                module.skipped += 1
        modules.append(module)
    return modules


def coverage(directory: Path, assemblies: set[str]) -> list[dict[str, object]]:
    merged = directory / "coverage" / "merged.cobertura.xml"
    if not merged.exists():
        return []
    rows: list[dict[str, object]] = []
    for package in ET.parse(merged).getroot().iterfind(".//package"):
        name = package.get("name", "")
        if assemblies and name not in assemblies:
            continue
        rows.append(
            {
                "assembly": name,
                "line": round(float(package.get("line-rate", 0)) * 100, 1),
                "branch": round(float(package.get("branch-rate", 0)) * 100, 1),
            }
        )
    return sorted(rows, key=lambda row: str(row["assembly"]))


def git(*args: str) -> str:
    return subprocess.run(["git", *args], cwd=REPO_ROOT, capture_output=True, text=True, check=False).stdout.strip()


DEFAULT_FLOORS = {"unit": 60.0, "line": 80.0, "branch": 70.0}
TARGET_BY_SUFFIX = {"verify": "make verify-affected", "test": "make test-affected", "integration": "make test-affected-integration", "build": "make build-affected"}
HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(?P<start>\d+)(?:,(?P<count>\d+))? @@")
CONDITION = re.compile(r"\((?P<covered>\d+)/(?P<total>\d+)\)")
# The coverage settings exclude these assemblies (test helpers shipped as packages), so no run ever measures them.
UNMEASURED_ASSEMBLY = re.compile(r"\.Testing(\.[^.]+)?$")


@dataclass
class LineHit:
    covered: bool = False
    branches_covered: int = 0
    branches: int = 0


def changed_source_lines(base: str | None) -> dict[str, set[int]]:
    """Lines this side adds or changes in src/ C# files, including uncommitted and untracked work.

    Diffing the merge base rather than BASE itself keeps commits upstream has, and this branch does not, out of the set.
    """
    if not base:
        return {}
    merge_base = git("merge-base", "HEAD", base)
    if not merge_base:
        return {}
    changed: dict[str, set[int]] = {}
    current: str | None = None
    for line in git("diff", "--unified=0", "--no-color", "--no-ext-diff", "--diff-filter=AMR", merge_base, "--", "src/*.cs").splitlines():
        if line.startswith("+++ "):
            target = line[4:]
            current = target[2:] if target.startswith("b/") else None
            continue
        match = HUNK.match(line)
        if match and current:
            start, count = int(match["start"]), int(match["count"] or 1)
            changed.setdefault(current, set()).update(range(start, start + count))
    for path in git("ls-files", "--others", "--exclude-standard", "--", "src/*.cs").splitlines():
        line_count = len((REPO_ROOT / path).read_text(encoding="utf-8", errors="replace").splitlines())
        changed[path] = set(range(1, line_count + 1))
    return {path: lines for path, lines in changed.items() if lines}


def line_hits(cobertura: Path) -> tuple[dict[str, dict[int, LineHit]], dict[str, str]]:
    """Per-file line hits from a merged Cobertura report, plus each file's assembly.

    A file can appear in several classes (partial and nested types), so hits merge across them: a line is covered when
    any class covered it, and its branch count is the best any class reported.
    """
    hits: dict[str, dict[int, LineHit]] = {}
    assemblies: dict[str, str] = {}
    if not cobertura.exists():
        return hits, assemblies
    for package in ET.parse(cobertura).getroot().iterfind(".//package"):
        assembly = package.get("name", "")
        for cls in package.iterfind("classes/class"):
            file = relative(cls.get("filename", "").replace("\\", "/"))
            if file.startswith("/"):
                # A checkout reached through a symlink (macOS /var is /private/var) reports one form of the path and
                # resolves to the other.
                file = relative(Path(file).resolve().as_posix())
            assemblies.setdefault(file, assembly)
            by_line = hits.setdefault(file, {})
            for element in cls.iterfind("lines/line"):
                hit = by_line.setdefault(int(element.get("number", "0")), LineHit())
                hit.covered = hit.covered or int(element.get("hits", "0")) > 0
                condition = CONDITION.search(element.get("condition-coverage", ""))
                if element.get("branch", "").lower() == "true" and condition:
                    hit.branches_covered = max(hit.branches_covered, int(condition["covered"]))
                    hit.branches = max(hit.branches, int(condition["total"]))
    return hits, assemblies


def has_integration_project(assembly: str) -> bool:
    # A package that talks to a broker, a database, or an HTTP host owns a tests/<Package>.Tests.Integration project;
    # that is the repository's marker for "needs an external dependency", so its coverage counts integration runs.
    return (REPO_ROOT / "tests" / f"{assembly}.Tests.Integration").is_dir()


def percent(covered: int, total: int) -> float:
    return round(covered * 100 / total, 1) if total else 100.0


def compress(lines: list[int]) -> str:
    """Render sorted line numbers as ranges, e.g. 3-5, 9."""
    ranges: list[str] = []
    start = previous = lines[0]
    for number in lines[1:] + [-1]:
        if number == previous + 1:
            previous = number
            continue
        ranges.append(f"{start}-{previous}" if previous != start else str(start))
        start = previous = number
    return ", ".join(ranges)


def coverage_gate(
    directory: Path,
    scope: str,
    base: str | None,
    changed_assemblies: set[str],
    coverage_rows: list[dict[str, object]],
    floors: dict[str, float],
    tests_skipped: bool,
) -> tuple[Stage | None, dict[str, object] | None]:
    """Hold the change to the coverage floors.

    `unit` (verify-affected) checks what unit tests alone can reach: the unit floor of every changed package without an
    integration project, and the changed lines in those packages. `all` (test-affected-integration) checks every changed
    line against the merged unit and integration coverage. A package with an integration project is never held to a
    unit-only figure: its real behavior sits behind the broker or database its integration suite starts.
    """
    if scope == "none":
        return None, None
    if tests_skipped:
        stage = Stage("coverage-gate", [], -1, 0.0, "", "a test stage did not run, so coverage is incomplete")
        return stage, {"scope": scope, "result": "skipped", "reasons": [stage.note]}

    hits, file_assembly = line_hits(directory / "coverage" / "merged.cobertura.xml")
    measured = {str(row["assembly"]): float(row["line"]) for row in coverage_rows}
    reasons: list[str] = []
    deferred: list[str] = []
    changed_lines = changed_source_lines(base)

    def assembly_of(path: str) -> str:
        parts = Path(path).parts
        return file_assembly.get(path) or (parts[1] if len(parts) > 2 else "")

    # The unit floor binds the packages whose source this branch edits. A project selected only because a build-wide
    # file changed answers for nothing it did, so it is not held to a floor here.
    edited = {assembly_of(path) for path in changed_lines} & changed_assemblies

    unit_floor: list[dict[str, object]] = []
    if scope == "unit":
        for assembly in sorted(edited):
            if has_integration_project(assembly) or UNMEASURED_ASSEMBLY.search(assembly):
                continue
            line = measured.get(assembly)
            ok = line is not None and line >= floors["unit"]
            unit_floor.append({"assembly": assembly, "line": line, "floor": floors["unit"], "pass": ok})
            if not ok:
                reasons.append(
                    f"{assembly}: unit line coverage {line}% is under the {floors['unit']:g}% floor"
                    if line is not None
                    else f"{assembly}: no unit test loads it, so its unit coverage is 0%"
                )

    by_assembly: dict[str, dict[str, int]] = {}
    uncovered: list[str] = []
    unmeasured_files: list[str] = []
    for path, lines in sorted(changed_lines.items()):
        assembly = assembly_of(path)
        if UNMEASURED_ASSEMBLY.search(assembly):
            continue
        if scope == "unit" and has_integration_project(assembly):
            if assembly not in deferred:
                deferred.append(assembly)
            continue
        file_hits = hits.get(path)
        if file_hits is None:
            # Coverage lists only executable lines; a file it never saw has none measured, such as an interface or a
            # file in a package no test loads (the unit floor reports the latter).
            unmeasured_files.append(path)
            continue
        totals = by_assembly.setdefault(assembly, {"lines": 0, "covered": 0, "branches": 0, "branches_covered": 0})
        missed: list[int] = []
        for number in sorted(lines):
            hit = file_hits.get(number)
            if hit is None:
                continue
            totals["lines"] += 1
            totals["covered"] += int(hit.covered)
            totals["branches"] += hit.branches
            totals["branches_covered"] += hit.branches_covered
            if not hit.covered:
                missed.append(number)
        if missed:
            uncovered.append(f"{path}: {compress(missed)}")

    lines_total = sum(t["lines"] for t in by_assembly.values())
    lines_covered = sum(t["covered"] for t in by_assembly.values())
    branches_total = sum(t["branches"] for t in by_assembly.values())
    branches_covered = sum(t["branches_covered"] for t in by_assembly.values())
    line_rate = percent(lines_covered, lines_total)
    branch_rate = percent(branches_covered, branches_total)
    if line_rate < floors["line"]:
        reasons.append(f"changed-line coverage {line_rate}% ({lines_covered}/{lines_total}) is under the {floors['line']:g}% floor")
    if branch_rate < floors["branch"]:
        reasons.append(
            f"changed-branch coverage {branch_rate}% ({branches_covered}/{branches_total}) is under the {floors['branch']:g}% floor"
        )

    report: dict[str, object] = {
        "scope": scope,
        "result": "fail" if reasons else "pass",
        "reasons": reasons,
        "floors": floors,
        "changed_lines": {"line": line_rate, "covered": lines_covered, "total": lines_total},
        "changed_branches": {"branch": branch_rate, "covered": branches_covered, "total": branches_total},
        "by_assembly": [
            {
                "assembly": name,
                "line": percent(t["covered"], t["lines"]),
                "branch": percent(t["branches_covered"], t["branches"]),
                "lines": t["lines"],
            }
            for name, t in sorted(by_assembly.items())
            if t["lines"]
        ],
        "unit_floor": unit_floor,
        "uncovered": uncovered,
        "unmeasured_files": unmeasured_files,
        "deferred_to_integration": deferred,
    }
    stage = Stage("coverage-gate", [], 3 if reasons else 0, 0.0, "", "; ".join(reasons))
    return stage, report


def summarize(directory: Path, gate_scope: str = "none", floors: dict[str, float] | None = None) -> int:
    stages = read_stages(directory)
    affected_path = directory / "affected.json"
    affected = json.loads(affected_path.read_text(encoding="utf-8")) if affected_path.exists() else {}
    changed_assemblies = {Path(p).stem for p in affected.get("changed_projects", []) if p.startswith("src/")}

    diagnostics = compiler_diagnostics(directory)
    findings = analyzer_findings(directory, [str(Path(p).parent) + "/" for p in affected.get("changed_projects", [])])
    modules = test_modules(directory)
    coverage_rows = coverage(directory, changed_assemblies)
    # `dotnet format --verify-no-changes` exits 2 for hidden-severity findings too, which the gate
    # ignores, so the analyzers stage passes or fails on its visible findings instead.
    for stage in stages:
        if stage.name == "analyzers" and stage.exit_code in (0, 2):
            stage.exit_code = 2 if findings else 0
    tests_skipped = any(stage.name in ("unit-tests", "integration-tests") and stage.exit_code < 0 for stage in stages)
    gate_stage, gate = coverage_gate(
        directory,
        gate_scope,
        affected.get("base"),
        changed_assemblies,
        coverage_rows,
        floors or DEFAULT_FLOORS,
        tests_skipped,
    )
    if gate_stage is not None:
        stages.append(gate_stage)
    failed_stages = [stage.name for stage in stages if stage.exit_code > 0]

    summary = {
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "commit": git("rev-parse", "HEAD"),
        "dirty": bool(git("status", "--porcelain")),
        "base": affected.get("base"),
        "verdict": "fail" if failed_stages else "pass",
        "stages": [asdict(stage) for stage in stages],
        "affected": {key: affected.get(key, []) for key in ("changed_projects", "unit_tests", "integration_tests", "global_triggers", "unmapped_files")},
        "tests": {
            "passed": sum(m.passed for m in modules),
            "failed": sum(m.failed for m in modules),
            "skipped": sum(m.skipped for m in modules),
            "modules": [asdict(m) for m in modules],
        },
        "compiler_diagnostics": diagnostics,
        "analyzer_findings": findings,
        "coverage": coverage_rows,
        "coverage_gate": gate,
        "target": TARGET_BY_SUFFIX.get(directory.name.rsplit("-", 1)[-1], "make verify-affected"),
        "integration_ran": any(stage.name == "integration-tests" and stage.exit_code >= 0 for stage in stages),
    }
    (directory / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    (directory / "summary.md").write_text(render_markdown(summary), encoding="utf-8")
    print((directory / "summary.md").read_text(encoding="utf-8"))
    return 1 if failed_stages else 0


def render_markdown(summary: dict) -> str:
    tests = summary["tests"]
    # The PR template's Verification section holds this bundle. The summary line keeps the verdict and
    # totals visible while the tables fold away; a failing bundle stays open so the failure is seen.
    verdict = summary["verdict"].upper()
    lines = [
        f"<details{' open' if verdict == 'FAIL' else ''}><summary><strong>{summary.get('target', 'make verify-affected')}: {verdict}</strong>. "
        f"Tests: {tests['passed']} passed, {tests['failed']} failed, {tests['skipped']} skipped. "
        f"Compiler diagnostics: {len(summary['compiler_diagnostics'])}. "
        f"Analyzer findings: {len(summary['analyzer_findings'])}.</summary>",
        "",
        f"Commit `{summary['commit'][:12]}`{' with uncommitted changes' if summary['dirty'] else ''}, base `{summary['base']}`.",
        "",
        "| Stage | Result | Time |",
        "| --- | --- | --- |",
    ]
    for stage in summary["stages"]:
        exit_code = stage["exit_code"]
        result = "pass" if exit_code == 0 else f"skipped: {stage.get('note', '')}" if exit_code < 0 else f"fail ({exit_code})"
        lines.append(f"| `{stage['name']}` | {result} | {stage['seconds']}s |")

    affected = summary["affected"]
    lines += [
        "",
        f"Changed projects: {len(affected['changed_projects'])}. "
        f"Unit-test projects: {len(affected['unit_tests'])}. "
        f"Integration-test projects{'' if summary.get('integration_ran') else ' (not run here)'}: {len(affected['integration_tests'])}.",
    ]
    if affected["global_triggers"]:
        lines.append(f"Build-wide files changed: {', '.join(f'`{f}`' for f in affected['global_triggers'])}.")

    lines += ["", f"### Tests: {tests['passed']} passed, {tests['failed']} failed, {tests['skipped']} skipped", ""]
    if tests["modules"]:
        lines += ["| Module | Passed | Failed | Skipped |", "| --- | --- | --- | --- |"]
        lines += [f"| {m['module']} | {m['passed']} | {m['failed']} | {m['skipped']} |" for m in tests["modules"]]
    failures = [(m["module"], f) for m in tests["modules"] for f in m["failures"]]
    for module, failure in failures[:MAX_LISTED]:
        lines.append(f"- `{module}` `{failure['test']}`: {failure['message']}")

    for title, key in (("Compiler diagnostics", "compiler_diagnostics"), ("Analyzer findings", "analyzer_findings")):
        items = summary[key]
        lines += ["", f"### {title}: {len(items)}", ""]
        by_id: dict[str, int] = {}
        for item in items:
            by_id[item["id"]] = by_id.get(item["id"], 0) + 1
        if by_id:
            lines.append(", ".join(f"`{rule}` x{count}" for rule, count in sorted(by_id.items(), key=lambda kv: -kv[1])))
            lines.append("")
        for item in items[:MAX_LISTED]:
            lines.append(f"- {item['severity']} `{item['id']}` {item['file']}:{item['line']} {item['message']}")
        if len(items) > MAX_LISTED:
            lines.append(f"- ... {len(items) - MAX_LISTED} more in summary.json")

    if summary["coverage"]:
        source = "unit and integration tests" if summary.get("integration_ran") else "unit tests"
        lines += ["", f"### Coverage of changed assemblies ({source})", "", "| Assembly | Line % | Branch % |", "| --- | --- | --- |"]
        lines += [f"| {row['assembly']} | {row['line']} | {row['branch']} |" for row in summary["coverage"]]
    lines += render_gate(summary.get("coverage_gate"))
    lines += ["", "</details>"]
    return "\n".join(lines) + "\n"


def render_gate(gate: dict | None) -> list[str]:
    if not gate:
        return []
    scope = "changed lines in packages without an integration project, unit tests only" if gate["scope"] == "unit" else "every changed line, unit and integration tests merged"
    lines = ["", f"### Coverage gate: {gate['result']} ({scope})", ""]
    if gate["result"] == "skipped":
        return lines + [f"- {reason}" for reason in gate["reasons"]]
    floors = gate["floors"]
    changed, branches = gate["changed_lines"], gate["changed_branches"]
    lines.append(
        f"Changed lines: {changed['line']}% ({changed['covered']}/{changed['total']}, floor {floors['line']:g}%). "
        f"Changed branches: {branches['branch']}% ({branches['covered']}/{branches['total']}, floor {floors['branch']:g}%)."
    )
    if gate["by_assembly"]:
        lines += ["", "| Assembly | Changed lines | Line % | Branch % |", "| --- | --- | --- | --- |"]
        lines += [f"| {row['assembly']} | {row['lines']} | {row['line']} | {row['branch']} |" for row in gate["by_assembly"]]
    if gate["unit_floor"]:
        lines += ["", f"Unit floor ({floors['unit']:g}% line, packages without an integration project):"]
        lines += [f"- {row['assembly']}: {row['line'] if row['line'] is not None else 'not loaded'}% {'pass' if row['pass'] else 'FAIL'}" for row in gate["unit_floor"]]
    if gate["deferred_to_integration"]:
        lines += ["", "Held to the changed-line floor by `make test-affected-integration`, not here: " + ", ".join(f"`{name}`" for name in gate["deferred_to_integration"]) + "."]
    for reason in gate["reasons"]:
        lines.append(f"- **{reason}**")
    for entry in gate["uncovered"][:MAX_LISTED]:
        lines.append(f"- uncovered: {entry}")
    if len(gate["uncovered"]) > MAX_LISTED:
        lines.append(f"- ... {len(gate['uncovered']) - MAX_LISTED} more files in summary.json")
    return lines


# What to run to repeat a failed non-test stage on its own; the module rerun covers only unit-tests.
STAGE_RERUN = {
    "format": "make format-check-changed (make format writes the fix)",
    "restore": "make build-affected",
    "build": "make build-affected",
    "analyzers": "make quality-analyzers-affected",
    "coverage-merge": "make verify-affected",
    "coverage-gate": "add tests for the uncovered changed lines summary.md lists, then make verify-affected (or make test-affected-integration for a package with an integration project)",
}


def latest_bundle(root: Path) -> Path | None:
    # Bundle names start with a UTC timestamp, so name order is run order.
    bundles = sorted(
        path for path in root.glob("*") if path.name.endswith(("-verify", "-test")) and (path / "summary.json").is_file()
    )
    return bundles[-1] if bundles else None


def failed_projects(bundle: Path | None, root: Path) -> int:
    """A narrow fix proves itself on the modules that failed; the full gate runs once at the end."""
    if bundle is None:
        bundle = latest_bundle(root)
        if bundle is None:
            print(f"[proof] no verify-affected or test-affected bundle under {root}. Run: make verify-affected", file=sys.stderr)
            return 1
    summary = json.loads((bundle / "summary.json").read_text(encoding="utf-8"))
    unit_list = bundle / "unit.txt"
    by_module = {Path(line).stem: line for line in unit_list.read_text(encoding="utf-8").split()} if unit_list.exists() else {}
    projects: list[str] = []
    for module in summary["tests"]["modules"]:
        if not module["failed"]:
            continue
        name = module["module"]
        project = by_module.get(name, f"tests/{name}/{name}.csproj")
        if not (REPO_ROOT / project).is_file():
            print(f"[proof] {name} failed, but no test project was found for it at {project}", file=sys.stderr)
            return 1
        projects.append(project)

    stages = {stage["name"]: stage["exit_code"] for stage in summary["stages"]}
    others = [name for name, code in stages.items() if code > 0 and name != "unit-tests"]
    print(f"[proof] {bundle}: verdict {summary['verdict']}, {len(projects)} failed test module(s)", file=sys.stderr)
    for name in others:
        rerun = STAGE_RERUN.get(name, f"make {name}-test" if name.startswith("dashboard-") else "make verify-affected")
        print(f"[proof]   stage {name} failed; re-run it with: {rerun}", file=sys.stderr)
    if stages.get("unit-tests", 0) > 0 and not projects:
        # A module that crashed or timed out leaves no failed result to select.
        print("[proof]   unit-tests failed without a failed test result (crash or timeout); read unit-tests.log", file=sys.stderr)
        return 1
    if not projects:
        if not others:
            print("[proof]   nothing failed; nothing to re-run", file=sys.stderr)
        return 1 if others else 0
    print("\n".join(projects))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("run", help="run and record one stage")
    run.add_argument("--dir", required=True, type=Path)
    run.add_argument("--name", required=True)
    run.add_argument("stage_command", nargs=argparse.REMAINDER, help="command after --")
    skip = commands.add_parser("skip", help="record a stage that did not run, and why")
    skip.add_argument("--dir", required=True, type=Path)
    skip.add_argument("--name", required=True)
    skip.add_argument("--reason", required=True)
    summary = commands.add_parser("summarize", help="write summary.json and summary.md")
    summary.add_argument("--dir", required=True, type=Path)
    summary.add_argument(
        "--coverage-gate",
        choices=("none", "unit", "all"),
        default="none",
        help="hold the change to the coverage floors: unit tests only (unit) or unit and integration merged (all)",
    )
    summary.add_argument("--unit-floor", type=float, default=DEFAULT_FLOORS["unit"], help="assembly line %% from unit tests")
    summary.add_argument("--line-floor", type=float, default=DEFAULT_FLOORS["line"], help="changed-line coverage %%")
    summary.add_argument("--branch-floor", type=float, default=DEFAULT_FLOORS["branch"], help="changed-branch coverage %%")
    failed = commands.add_parser("failed", help="print the test projects that failed in a bundle")
    failed.add_argument("--dir", type=Path, help="bundle to read (default: the latest -verify or -test bundle under --root)")
    failed.add_argument("--root", type=Path, default=REPO_ROOT / "artifacts" / "proof", help="where the bundles live")
    args = parser.parse_args()

    if args.command == "run":
        command = args.stage_command[1:] if args.stage_command[:1] == ["--"] else args.stage_command
        if not command:
            parser.error("run needs a command after --")
        return run_stage(args.dir, args.name, command)
    if args.command == "skip":
        return skip_stage(args.dir, args.name, args.reason)
    if args.command == "failed":
        return failed_projects(args.dir, args.root)
    floors = {"unit": args.unit_floor, "line": args.line_floor, "branch": args.branch_floor}
    return summarize(args.dir, args.coverage_gate, floors)


if __name__ == "__main__":
    sys.exit(main())
