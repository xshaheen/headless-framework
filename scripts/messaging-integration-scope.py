#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Choose what the messaging integration workflow runs for one event.

The messaging integration suites need Docker, brokers, and minutes per suite, so the required pull-request checks never
run them. messaging-integration.yml runs them beside those checks, and this script decides each run:

- pull_request and push to main: every leg when the change reaches any leg's integration project through the
  transitive project graph, else none. A change to a shared package messaging builds on reaches the legs as surely as
  a change to messaging itself, which a path filter would miss. A run takes every leg rather than the affected ones
  because the drift report can only judge a simulator cell once every real broker behind it has run. No chaos and no
  quarantine check.
- schedule (weekly): every leg, chaos at full sizes, and the quarantine check.
- workflow_dispatch: everything, at the requested chaos sizes.

The leg list lives here, once: the Makefile reads it through `projects`, and the workflow takes its matrix from
`select`. A base that is empty or does not resolve to a commit (a new branch, history rewritten) selects every leg.

Exit codes: 0 selection printed; 1 the affected-set computation failed; 2 usage error.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from dataclasses import asdict, dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class Leg:
    name: str
    project: str
    tz: str = "UTC"


def _integration(project: str) -> str:
    return f"tests/{project}.Tests.Integration/{project}.Tests.Integration.csproj"


LEGS: tuple[Leg, ...] = (
    # Runs for its emulator profile; the real-namespace tests need a credential and have their own dispatch workflow.
    Leg("azure-service-bus-emulator", _integration("Headless.Messaging.AzureServiceBus")),
    Leg("aws", _integration("Headless.Messaging.Aws")),
    Leg("aws-postgresql", _integration("Headless.Messaging.AwsPostgreSql")),
    Leg("kafka", _integration("Headless.Messaging.Kafka")),
    Leg("kafka-postgresql", _integration("Headless.Messaging.KafkaPostgreSql")),
    Leg("nats", _integration("Headless.Messaging.Nats")),
    Leg("nats-postgresql", _integration("Headless.Messaging.NatsPostgreSql")),
    Leg("pulsar", _integration("Headless.Messaging.Pulsar")),
    Leg("pulsar-postgresql", _integration("Headless.Messaging.PulsarPostgreSql")),
    Leg("rabbitmq", _integration("Headless.Messaging.RabbitMq")),
    Leg("rabbitmq-postgresql", _integration("Headless.Messaging.RabbitMqPostgreSql")),
    Leg("rabbitmq-sqlserver", _integration("Headless.Messaging.RabbitMqSqlServer")),
    Leg("redis", _integration("Headless.Messaging.Redis")),
    Leg("redis-postgresql", _integration("Headless.Messaging.RedisPostgreSql")),
    Leg("storage-postgresql", _integration("Headless.Messaging.Storage.PostgreSql")),
    # Runners are UTC, which hides code that trusts DateTime.Kind or a local offset. Schedules, leases and retry times
    # are stored here, so this suite runs a second time in a DST-observing zone east of UTC.
    Leg("storage-postgresql-cairo", _integration("Headless.Messaging.Storage.PostgreSql"), "Africa/Cairo"),
    Leg("storage-sqlserver", _integration("Headless.Messaging.Storage.SqlServer")),
    Leg("entityframework", _integration("Headless.EntityFramework.Messaging")),
)


@dataclass(frozen=True)
class Selection:
    mode: str
    reason: str
    legs: tuple[Leg, ...]
    run_chaos: bool
    chaos_profile: str
    run_quarantine: bool

    def outputs(self) -> dict[str, str]:
        return {
            "mode": self.mode,
            "reason": self.reason,
            "legs": json.dumps([asdict(leg) for leg in self.legs], separators=(",", ":")),
            "run_legs": _flag(bool(self.legs)),
            "run_chaos": _flag(self.run_chaos),
            "chaos_profile": self.chaos_profile,
            "run_quarantine": _flag(self.run_quarantine),
        }


def _flag(value: bool) -> str:
    return "true" if value else "false"


def _resolves(revision: str) -> str | None:
    if not revision:
        return None
    result = subprocess.run(
        ["git", "rev-parse", "--verify", "--quiet", f"{revision}^{{commit}}"],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    return result.stdout.strip() if result.returncode == 0 else None


def _affected_report(base: str) -> dict:
    result = subprocess.run(
        [sys.executable, "-I", str(REPO_ROOT / "scripts" / "project-graph.py"), "affected", "--base", base, "--transitive"],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(f"project-graph.py affected failed ({result.returncode}): {result.stderr.strip()}")
    return json.loads(result.stdout)


def select(event: str, base: str, chaos_profile: str, affected: dict | None) -> Selection:
    """Decides the run. `affected` is the transitive affected report against `base`; a change event computes it when absent."""
    if event == "workflow_dispatch":
        return Selection("full", "dispatched", LEGS, True, chaos_profile or "full", True)

    if event == "schedule":
        return Selection("full", "weekly full run", LEGS, True, "full", True)

    # pull_request or push
    base_commit = _resolves(base)
    if base_commit is None:
        return Selection("legs", "no base commit to diff against", LEGS, False, "smoke", False)
    if affected is None:
        affected = _affected_report(base_commit)
    tests = set(affected.get("integration_tests", []))
    reached = [leg.name for leg in LEGS if leg.project in tests]
    if not reached:
        return Selection("none", f"no messaging suite affected since {base_commit[:12]}", (), False, "smoke", False)
    return Selection("legs", f"affected since {base_commit[:12]}: {', '.join(reached)}", LEGS, False, "smoke", False)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("projects", help="print each distinct leg project, one per line")
    chooser = commands.add_parser("select", help="print the run's selection as key=value lines for $GITHUB_OUTPUT")
    chooser.add_argument("--event", required=True, choices=("pull_request", "push", "schedule", "workflow_dispatch"))
    chooser.add_argument(
        "--base", default="", help="commit a change event diffs against: the pull request's base, or the push's previous head"
    )
    chooser.add_argument("--chaos-profile", default="", choices=("", "full", "smoke"))
    chooser.add_argument(
        "--affected-report",
        type=Path,
        help="use this transitive affected JSON instead of computing it; for testing the selection",
    )
    args = parser.parse_args(argv)

    if args.command == "projects":
        for project in dict.fromkeys(leg.project for leg in LEGS):
            print(project)
        return 0

    affected = json.loads(args.affected_report.read_text(encoding="utf-8")) if args.affected_report else None
    try:
        selection = select(args.event, args.base, args.chaos_profile, affected)
    except RuntimeError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1

    for key, value in selection.outputs().items():
        print(f"{key}={value}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
