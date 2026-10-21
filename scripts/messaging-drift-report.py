#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Label simulator drift in a scheduled messaging run, apart from ordinary provider failures.

The fast tier proves transport guarantees against an in-process simulator, and the manifest lets the simulator claim
only cells some real broker also supports. That claim holds only while the real brokers keep passing. When every real
broker that supports a cell fails it in one run, while the simulator still passes it, the simulator no longer behaves
like the brokers it stands in for: that is drift, and the fast tier is now green on a guarantee nothing real keeps.
When only some of them fail, the simulator is still backed and the failure belongs to those providers.

Inputs, all found under --results: the conformance `snapshot.json` and `bindings/*.json` the evidence tests export
when HEADLESS_CONFORMANCE_EXPORT_DIR is set, and the run's `*.trx` files. A cell's result is the worst result of the
tests its provider binds to it. A cell is judged drift only when every real broker that supports it, apart from the
providers --exclude-providers names as outside the run, ran it and failed it.

Exit codes: 0 no drift; 3 drift found; 1 inputs missing or unreadable; 2 usage error.
"""

from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ElementTree
from collections import defaultdict
from dataclasses import dataclass
from pathlib import Path

_TRX_NAMESPACE = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
_FAILED_OUTCOMES = {"Failed", "Error", "Timeout", "Aborted"}
_PASSED_OUTCOMES = {"Passed"}

PASSED = "passed"
FAILED = "failed"
SKIPPED = "skipped"
NOT_RUN = "not run"

_CELL_MARK = {PASSED: "pass", FAILED: "**FAIL**", SKIPPED: "skip", NOT_RUN: "not run"}
# Worst outcome ranks highest: a test or cell with several results reports the worst of them.
_OUTCOME_RANK = {NOT_RUN: 0, SKIPPED: 1, PASSED: 2, FAILED: 3}


@dataclass(frozen=True)
class TestKey:
    assembly: str
    test_class: str
    method: str

    def display(self) -> str:
        return f"{self.test_class}.{self.method}"


@dataclass
class TestResult:
    outcome: str
    message: str


class InputError(RuntimeError):
    pass


def load_snapshot(results: Path) -> dict:
    for path in sorted(results.rglob("snapshot.json")):
        try:
            snapshot = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise InputError(f"cannot read {path}: {error}") from error
        if isinstance(snapshot, dict) and "providers" in snapshot:
            return snapshot
    raise InputError(
        f"no conformance snapshot.json under {results}; run the integration suites with HEADLESS_CONFORMANCE_EXPORT_DIR set"
    )


def load_bindings(results: Path) -> dict[tuple[str, str], set[TestKey]]:
    """Maps (provider, scenario) to the tests that provider binds to the cell."""
    cells: dict[tuple[str, str], set[TestKey]] = defaultdict(set)
    for path in sorted(results.rglob("*.json")):
        if path.parent.name != "bindings":
            continue
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise InputError(f"cannot read {path}: {error}") from error
        provider = document["provider"]
        for binding in document.get("bindings", []):
            key = TestKey(binding["assembly"], binding["testClass"], binding["testMethod"])
            cells[(provider, binding["scenario"])].add(key)
    return cells


def load_results(results: Path) -> dict[TestKey, TestResult]:
    """Reads every TRX under the results root; a test with several result rows keeps its worst outcome."""
    tests: dict[TestKey, TestResult] = {}
    trx_files = sorted(results.rglob("*.trx"))
    if not trx_files:
        raise InputError(f"no *.trx files under {results}")

    for path in trx_files:
        try:
            root = ElementTree.parse(path).getroot()
        except (OSError, ElementTree.ParseError) as error:
            raise InputError(f"cannot parse {path}: {error}") from error

        definitions: dict[str, TestKey] = {}
        for unit_test in root.iterfind("t:TestDefinitions/t:UnitTest", _TRX_NAMESPACE):
            method = unit_test.find("t:TestMethod", _TRX_NAMESPACE)
            if method is None:
                continue
            assembly = Path(method.get("codeBase", "").replace("\\", "/")).stem
            definitions[unit_test.get("id", "")] = TestKey(assembly, method.get("className", ""), method.get("name", ""))

        for result in root.iterfind("t:Results/t:UnitTestResult", _TRX_NAMESPACE):
            key = definitions.get(result.get("testId", ""))
            if key is None:
                continue
            raw = result.get("outcome", "")
            outcome = FAILED if raw in _FAILED_OUTCOMES else PASSED if raw in _PASSED_OUTCOMES else SKIPPED
            message_node = result.find("t:Output/t:ErrorInfo/t:Message", _TRX_NAMESPACE)
            message = (message_node.text or "").strip().splitlines()[0] if message_node is not None and message_node.text else ""
            previous = tests.get(key)
            if previous is None or _rank(outcome) > _rank(previous.outcome):
                tests[key] = TestResult(outcome, message)
    return tests


def _rank(outcome: str) -> int:
    return _OUTCOME_RANK[outcome]


def cell_outcome(keys: set[TestKey], tests: dict[TestKey, TestResult]) -> str:
    outcomes = [tests[key].outcome for key in keys if key in tests]
    if not outcomes:
        return NOT_RUN
    return max(outcomes, key=_rank)


def build_report(snapshot: dict, bindings: dict[tuple[str, str], set[TestKey]], tests: dict[TestKey, TestResult], excluded: set[str]) -> tuple[str, bool]:
    simulator_name = snapshot["simulator"]
    providers = {provider["name"]: provider for provider in snapshot["providers"]}
    simulator = providers[simulator_name]
    real = sorted(name for name, provider in providers.items() if provider.get("realBrokerLeaf") and name != simulator_name)
    in_run = [name for name in real if name not in excluded]

    guarantees_by_scenario: dict[str, list[dict]] = defaultdict(list)
    for guarantee in snapshot.get("guarantees", []):
        for scenario in guarantee.get("manifestScenarios", []):
            guarantees_by_scenario[scenario].append(guarantee)

    def supports(provider: str, scenario: str) -> bool:
        return providers[provider]["cells"].get(scenario, {}).get("status") == "Supported"

    scenarios = sorted({scenario for name in real for scenario in providers[name]["cells"] if supports(name, scenario)})
    outcomes = {(name, scenario): cell_outcome(bindings.get((name, scenario), set()), tests) for name in in_run for scenario in scenarios if supports(name, scenario)}

    drift: list[str] = []
    verdicts: dict[str, str] = {}
    for scenario in scenarios:
        if simulator["cells"].get(scenario, {}).get("status") != "Supported":
            verdicts[scenario] = "not on the simulator"
            continue
        backing = [name for name in in_run if supports(name, scenario)]
        results = [outcomes[(name, scenario)] for name in backing]
        if backing and all(result == FAILED for result in results):
            verdicts[scenario] = "**simulator drift**"
            drift.append(scenario)
        elif FAILED in results and PASSED in results:
            # Another real broker still keeps the guarantee, so the simulator's claim is backed.
            verdicts[scenario] = "provider failure"
        elif FAILED in results:
            # Whether the brokers that did not run it would also fail decides drift, so it cannot be called either way.
            verdicts[scenario] = "not judged: failed where it ran, a backing broker did not run it"
        elif backing and all(result == PASSED for result in results):
            verdicts[scenario] = "backed"
        else:
            verdicts[scenario] = "not judged: a backing broker did not run it"

    drift_tests = {key for scenario in drift for name in in_run for key in bindings.get((name, scenario), set())}
    cells_by_test: dict[TestKey, list[str]] = defaultdict(list)
    for (name, scenario), keys in bindings.items():
        for key in keys:
            cells_by_test[key].append(f"{name}/{scenario}")

    failed_tests = sorted((key for key, result in tests.items() if result.outcome == FAILED), key=lambda key: (key.assembly, key.display()))
    counts = defaultdict(int)
    for result in tests.values():
        counts[result.outcome] += 1

    lines = [
        "## Messaging integration: simulator drift and provider failures",
        "",
        f"Tests: {counts[PASSED]} passed, {counts[FAILED]} failed, {counts[SKIPPED]} skipped, across "
        f"{len({key.assembly for key in tests})} test module(s).",
        f"Real brokers in this run: {', '.join(in_run) or 'none'}."
        + (f" Outside this run: {', '.join(sorted(excluded & set(real)))}." if excluded & set(real) else ""),
        "",
        "### Simulator drift",
        "",
    ]

    if drift:
        lines.append(
            "Every real broker that supports each cell below failed it, so the simulator's pass on it stands alone. "
            "Fix the simulator, or the brokers, before trusting the fast tier on these guarantees."
        )
        lines.append("")
        for scenario in drift:
            catalogued = guarantees_by_scenario.get(scenario, [])
            named = "; ".join(f"{guarantee['id']}: {guarantee['statement']}" for guarantee in catalogued)
            lines.append(f"- **{scenario}**: " + (f"guarantee {named}" if named else "no catalogued guarantee names this cell"))
            for name in in_run:
                if supports(name, scenario):
                    for key in sorted(bindings.get((name, scenario), set()), key=TestKey.display):
                        result = tests.get(key)
                        if result is not None and result.outcome == FAILED:
                            lines.append(f"  - {name}: `{key.display()}`: {result.message}")
    else:
        lines.append("None: every simulator cell some real broker supports passed on at least one of them, or was not judged.")

    lines += ["", "### Provider failures", ""]
    provider_failures = [key for key in failed_tests if key not in drift_tests]
    if provider_failures:
        current = None
        for key in provider_failures:
            if key.assembly != current:
                current = key.assembly
                lines.append(f"- {current}")
            cells = ", ".join(sorted(cells_by_test.get(key, []))) or "no manifest cell"
            message = tests[key].message
            lines.append(f"  - `{key.display()}` ({cells})" + (f": {message}" if message else ""))
    else:
        lines.append("None.")

    lines += ["", "### Per-cell results", ""]
    header = ["Scenario", *in_run, "Verdict"]
    lines.append("| " + " | ".join(header) + " |")
    lines.append("|" + "---|" * len(header))
    for scenario in scenarios:
        row = [scenario]
        for name in in_run:
            row.append(_CELL_MARK[outcomes[(name, scenario)]] if supports(name, scenario) else "-")
        row.append(verdicts[scenario])
        lines.append("| " + " | ".join(row) + " |")

    return "\n".join(lines) + "\n", bool(drift)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--results", required=True, type=Path, help="root holding the run's TRX files and conformance export")
    parser.add_argument("--exclude-providers", default="", help="comma-separated manifest providers the run does not cover")
    parser.add_argument("--out", type=Path, help="also write the Markdown report here")
    args = parser.parse_args(argv)

    if not args.results.is_dir():
        print(f"ERROR: {args.results} is not a directory", file=sys.stderr)
        return 1

    excluded = {name.strip() for name in args.exclude_providers.split(",") if name.strip()}
    try:
        snapshot = load_snapshot(args.results)
        bindings = load_bindings(args.results)
        tests = load_results(args.results)
    except (InputError, KeyError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1

    unknown = excluded - {provider["name"] for provider in snapshot["providers"]}
    if unknown:
        print(f"ERROR: --exclude-providers names providers the manifest does not know: {', '.join(sorted(unknown))}", file=sys.stderr)
        return 2

    report, drifted = build_report(snapshot, bindings, tests, excluded)
    if args.out is not None:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(report, encoding="utf-8")
    sys.stdout.write(report)
    return 3 if drifted else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
