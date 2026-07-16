#!/usr/bin/env python3
"""Verify that an exact provider conformance roster passed in TRX results."""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path
from typing import Sequence


def verify_results(
    provider: str,
    results_directory: Path,
    expected_tests: Sequence[str],
) -> list[str]:
    """Return conformance evidence errors for one provider's TRX results."""
    errors: list[str] = []
    normalized_expected = [test_name.strip() for test_name in expected_tests if test_name.strip()]

    if not normalized_expected:
        errors.append(f"{provider}: expected conformance test roster is empty.")
        return errors

    duplicate_expectations = _duplicates(normalized_expected)
    if duplicate_expectations:
        errors.append(
            f"{provider}: expected conformance roster contains duplicates: "
            f"{', '.join(duplicate_expectations)}."
        )

    result_paths = sorted(results_directory.rglob("*.trx")) if results_directory.is_dir() else []
    if not result_paths:
        errors.append(f"{provider}: no TRX results found under {results_directory}.")
        return errors

    outcomes_by_test: dict[str, list[str]] = defaultdict(list)
    for result_path in result_paths:
        try:
            root = ET.parse(result_path).getroot()
        except ET.ParseError as exception:
            errors.append(f"{provider}: invalid TRX {result_path}: {exception}.")
            continue

        for element in root.iter():
            if element.tag.rsplit("}", maxsplit=1)[-1] != "UnitTestResult":
                continue

            test_name = element.attrib.get("testName")
            if test_name:
                outcomes_by_test[test_name].append(element.attrib.get("outcome", "<missing>"))

    for test_name in dict.fromkeys(normalized_expected):
        outcomes = outcomes_by_test.get(test_name, [])
        if not outcomes:
            errors.append(f"{provider}: missing expected conformance test: {test_name}.")
            continue

        if len(outcomes) != 1:
            errors.append(
                f"{provider}: expected conformance test appeared {len(outcomes)} times: "
                f"{test_name} ({', '.join(outcomes)})."
            )
            continue

        if outcomes[0] != "Passed":
            errors.append(
                f"{provider}: expected conformance test did not pass: "
                f"{test_name} (outcome={outcomes[0]})."
            )

    return errors


def _duplicates(values: Sequence[str]) -> list[str]:
    seen: set[str] = set()
    duplicates: set[str] = set()
    for value in values:
        if value in seen:
            duplicates.add(value)
        seen.add(value)

    return sorted(duplicates)


def _parse_args(argv: Sequence[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Require every expected messaging conformance test exactly once with a Passed TRX outcome."
    )
    parser.add_argument("--provider", required=True, help="Provider label used in diagnostics.")
    parser.add_argument(
        "--results-directory",
        required=True,
        type=Path,
        help="Directory searched recursively for TRX result files.",
    )
    parser.add_argument(
        "--expected-tests",
        required=True,
        help="Newline-separated fully qualified test names that must pass exactly once.",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run the conformance evidence verifier CLI."""
    args = _parse_args(argv)
    expected_tests = args.expected_tests.splitlines()
    errors = verify_results(args.provider, args.results_directory, expected_tests)

    if errors:
        for error in errors:
            print(error, file=sys.stderr)
        return 1

    executed_count = len([test_name for test_name in expected_tests if test_name.strip()])
    print(f"Validated {executed_count} exact {args.provider} conformance tests.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
