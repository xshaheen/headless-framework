#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Compare two BenchmarkDotNet JSON exports and print a before/after table.

`make bench-compare` runs the same benchmark filter at a base ref and at the working tree, then
calls this. It reports, it never gates: BenchmarkDotNet results on a shared laptop are noisy, so a
reviewer reads the ratio next to the error column instead of a pass/fail threshold.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def load(directory: Path) -> dict[str, dict[str, float]]:
    results: dict[str, dict[str, float]] = {}
    for report in sorted(directory.rglob("*-report*.json")):
        for benchmark in json.loads(report.read_text(encoding="utf-8")).get("Benchmarks", []):
            statistics = benchmark.get("Statistics") or {}
            memory = benchmark.get("Memory") or {}
            # A dry or failed run leaves statistics and memory fields null rather than absent.
            results[benchmark["FullName"]] = {
                "mean": float(statistics.get("Mean") or 0.0),
                "error": float(statistics.get("StandardError") or 0.0),
                "allocated": float(memory.get("BytesAllocatedPerOperation") or 0.0),
            }
    return results


def format_time(nanoseconds: float) -> str:
    for unit, scale in (("s", 1e9), ("ms", 1e6), ("us", 1e3)):
        if nanoseconds >= scale:
            return f"{nanoseconds / scale:.2f} {unit}"
    return f"{nanoseconds:.1f} ns"


def ratio(after: float, before: float) -> str:
    return f"{after / before:.2f}x" if before else "n/a"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base", required=True, type=Path, help="BenchmarkDotNet artifacts directory for the base ref")
    parser.add_argument("--head", required=True, type=Path, help="BenchmarkDotNet artifacts directory for the working tree")
    parser.add_argument("--base-label", default="base")
    args = parser.parse_args()

    base, head = load(args.base), load(args.head)
    if not base or not head:
        print(f"[bench-compare] no JSON export found (base: {len(base)}, head: {len(head)} benchmarks)", file=sys.stderr)
        return 1

    lines = [
        f"## Benchmarks: working tree vs `{args.base_label}`",
        "",
        "| Benchmark | Mean before | Mean after | Time ratio | Alloc before | Alloc after | Alloc ratio |",
        "| --- | --- | --- | --- | --- | --- | --- |",
    ]
    for name in sorted(set(base) | set(head)):
        before, after = base.get(name), head.get(name)
        if not before or not after:
            lines.append(f"| {name} | {'missing' if not before else format_time(before['mean'])} | {'missing' if not after else format_time(after['mean'])} | | | | |")
            continue
        lines.append(
            f"| {name} | {format_time(before['mean'])} ±{format_time(before['error'])} | {format_time(after['mean'])} ±{format_time(after['error'])} "
            f"| {ratio(after['mean'], before['mean'])} | {before['allocated']:.0f} B | {after['allocated']:.0f} B | {ratio(after['allocated'], before['allocated'])} |"
        )
    print("\n".join(lines))
    return 0


if __name__ == "__main__":
    sys.exit(main())
