from __future__ import annotations

import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

from eng.verify_messaging_conformance import verify_results


class VerifyMessagingConformanceTests(unittest.TestCase):
    def test_accepts_full_exact_pass_roster(self) -> None:
        errors = self._verify(
            expected_tests=["Tests.Provider.round_trip", "Tests.Provider.commit"],
            results=[
                ("Tests.Unrelated.marker", "Passed"),
                ("Tests.Provider.round_trip", "Passed"),
                ("Tests.Provider.commit", "Passed"),
            ],
        )

        self.assertEqual([], errors)

    def test_rejects_missing_expected_test(self) -> None:
        errors = self._verify(
            expected_tests=["Tests.Provider.round_trip", "Tests.Provider.commit"],
            results=[("Tests.Provider.round_trip", "Passed")],
        )

        self.assertTrue(any("missing expected conformance test: Tests.Provider.commit" in error for error in errors))

    def test_rejects_skipped_expected_test(self) -> None:
        errors = self._verify(
            expected_tests=["Tests.Provider.round_trip"],
            results=[("Tests.Provider.round_trip", "NotExecuted")],
        )

        self.assertTrue(any("outcome=NotExecuted" in error for error in errors))

    def test_rejects_failed_expected_test(self) -> None:
        errors = self._verify(
            expected_tests=["Tests.Provider.round_trip"],
            results=[("Tests.Provider.round_trip", "Failed")],
        )

        self.assertTrue(any("outcome=Failed" in error for error in errors))

    def test_rejects_duplicate_expected_test_results(self) -> None:
        errors = self._verify(
            expected_tests=["Tests.Provider.round_trip"],
            results=[
                ("Tests.Provider.round_trip", "Passed"),
                ("Tests.Provider.round_trip", "Passed"),
            ],
        )

        self.assertTrue(any("appeared 2 times" in error for error in errors))

    def test_rejects_empty_roster(self) -> None:
        errors = self._verify(
            expected_tests=[],
            results=[("Tests.Unrelated.marker", "Passed")],
        )

        self.assertTrue(any("expected conformance test roster is empty" in error for error in errors))

    def test_rejects_duplicate_roster_entries(self) -> None:
        errors = self._verify(
            expected_tests=["Tests.Provider.round_trip", "Tests.Provider.round_trip"],
            results=[("Tests.Provider.round_trip", "Passed")],
        )

        self.assertTrue(any("roster contains duplicates" in error for error in errors))

    def test_rejects_missing_trx_results(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            errors = verify_results("Provider", Path(directory), ["Tests.Provider.round_trip"])

        self.assertTrue(any("no TRX results found" in error for error in errors))

    def test_rejects_malformed_trx(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            results_directory = Path(directory)
            (results_directory / "broken.trx").write_bytes(b"<TestRun><Results>")
            errors = verify_results("Provider", results_directory, ["Tests.Provider.round_trip"])

        self.assertTrue(any("invalid TRX" in error for error in errors))

    def _verify(self, expected_tests: list[str], results: list[tuple[str, str]]) -> list[str]:
        with tempfile.TemporaryDirectory() as directory:
            results_directory = Path(directory)
            _write_trx(results_directory / "results.trx", results)
            return verify_results("Provider", results_directory, expected_tests)


def _write_trx(path: Path, results: list[tuple[str, str]]) -> None:
    namespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
    test_run = ET.Element(f"{{{namespace}}}TestRun")
    result_elements = ET.SubElement(test_run, f"{{{namespace}}}Results")
    for test_name, outcome in results:
        ET.SubElement(
            result_elements,
            f"{{{namespace}}}UnitTestResult",
            {"testName": test_name, "outcome": outcome},
        )

    ET.ElementTree(test_run).write(path, encoding="utf-8", xml_declaration=True)


if __name__ == "__main__":
    unittest.main()
