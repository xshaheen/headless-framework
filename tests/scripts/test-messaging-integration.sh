#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises the messaging integration workflow's logic against scratch inputs: the quarantine check against a fake gh
# that serves issue states, the drift report against TRX files and conformance exports shaped like the ones the
# integration suites write, the scope selection that decides whether a run takes the legs, and the workflow's place
# outside the required CI checks.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

quarantine=(python3 -I "$repo_root/scripts/quarantine-check.py")
drift=(python3 -I "$repo_root/scripts/messaging-drift-report.py")
scope=(python3 -I "$repo_root/scripts/messaging-integration-scope.py")

fail() {
  printf 'FAIL: %s\n' "$1" >&2
  if [[ -f "$scratch/out.txt" ]]; then cat "$scratch/out.txt" >&2; fi
  exit 1
}

# expect <label> <expected exit> <command...>: runs the command into out.txt and checks its exit status.
expect() {
  local label="$1" expected="$2" actual=0
  shift 2
  "$@" > "$scratch/out.txt" 2>&1 || actual=$?
  [[ "$actual" == "$expected" ]] || fail "$label: exit $actual, expected $expected"
}

contains() {
  grep -qF -- "$2" "$scratch/out.txt" || fail "$1: output lacks \"$2\""
}

lacks() {
  if grep -qF -- "$2" "$scratch/out.txt"; then fail "$1: output should not contain \"$2\""; fi
}

pass() {
  printf 'PASS: %s\n' "$1"
}

# --- Quarantine check -------------------------------------------------------------------------------------------

issue_url() {
  printf 'https://github.com/xshaheen/headless-framework/issues/%s' "$1"
}

cat > "$scratch/snapshot.json" <<JSON
{
  "simulator": "Simulator",
  "providers": [
    {
      "name": "RabbitMQ",
      "realBrokerLeaf": true,
      "cells": {
        "PublishDuringOutage": {"status": "Unsupported", "rationale": "drops the consumer", "issueUrl": "$(issue_url 10)"},
        "StaleSettlement": {"status": "Unsupported", "rationale": "closes the channel", "issueUrl": "$(issue_url 11)"},
        "QueueRoundTrip": {"status": "Supported", "rationale": null, "issueUrl": null}
      }
    }
  ],
  "guarantees": [
    {"id": "MSG-CB-06", "statement": "s", "status": "Unsupported", "rationale": "r", "issueUrl": "$(issue_url 12)", "manifestScenarios": []},
    {"id": "MSG-PUB-08", "statement": "s", "status": "NoTestYet", "rationale": null, "issueUrl": "$(issue_url 11)", "manifestScenarios": []},
    {"id": "MSG-PUB-01", "statement": "s", "status": "Bound", "rationale": null, "issueUrl": null, "manifestScenarios": []}
  ]
}
JSON

# The fake gh answers `gh api repos/<owner>/<repo>/issues/<n>` from $GH_FIXTURES: <n>.json is the issue, <n>.404 means
# GitHub has no such issue, and <n>.500 a server error. Any other call fails, so the check cannot write anything.
mkdir -p "$scratch/bin" "$scratch/gh"
cat > "$scratch/bin/gh" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$#" -ne 2 || "$1" != "api" || "$2" != repos/*/issues/* ]]; then
  echo "unexpected gh call: $*" >&2
  exit 64
fi
number="${2##*/}"
if [[ -f "$GH_FIXTURES/$number.404" ]]; then echo "gh: Not Found (HTTP 404)" >&2; exit 1; fi
if [[ -f "$GH_FIXTURES/$number.500" ]]; then echo "gh: Server Error (HTTP 500)" >&2; exit 1; fi
cat "$GH_FIXTURES/$number.json"
SH
chmod +x "$scratch/bin/gh"
export GH_FIXTURES="$scratch/gh"

set_issue() {
  rm -f "$GH_FIXTURES/$1".*
  printf '{"number": %s, "state": "%s", "state_reason": "%s", "title": "issue %s"}\n' "$1" "$2" "${3:-}" "$1" > "$GH_FIXTURES/$1.json"
}

set_issue 10 closed completed
set_issue 11 open
set_issue 12 open
expect "quarantine fails on a closed issue" 3 "${quarantine[@]}" --snapshot "$scratch/snapshot.json" --gh "$scratch/bin/gh"
contains "quarantine names the closed issue" "$(issue_url 10) (closed, completed): issue 10"
contains "quarantine names the cell the closed issue still holds" "transport manifest: RabbitMQ / PublishDuringOutage (Unsupported)"
lacks "quarantine leaves cells with open issues out of the closed list" "RabbitMQ / StaleSettlement"
pass "a closed issue linked from an Unsupported cell fails the check, naming the cell"

set_issue 10 open
set_issue 11 closed not_planned
expect "quarantine fails on a closed tracking issue" 3 "${quarantine[@]}" --snapshot "$scratch/snapshot.json" --gh "$scratch/bin/gh"
contains "quarantine names the guarantee with no test yet" "guarantee MSG-PUB-08 (NoTestYet)"
contains "quarantine names the manifest cell on the same issue" "transport manifest: RabbitMQ / StaleSettlement (Unsupported)"
pass "a closed issue fails every cell and guarantee that links it"

set_issue 11 open
expect "quarantine passes when every issue is open" 0 "${quarantine[@]}" --snapshot "$scratch/snapshot.json" --gh "$scratch/bin/gh"
contains "quarantine counts the checked issues" "Checked 3 linked issue(s) for 4 quarantined cell(s) and guarantee(s); 3 open."
pass "every quarantine linking an open issue passes"

rm -f "$GH_FIXTURES/12.json"
touch "$GH_FIXTURES/12.404"
expect "quarantine fails on a missing issue" 3 "${quarantine[@]}" --snapshot "$scratch/snapshot.json" --gh "$scratch/bin/gh"
contains "quarantine names the missing issue's guarantee" "guarantee MSG-CB-06 (Unsupported)"
pass "an issue GitHub cannot find fails the check"

rm -f "$GH_FIXTURES/12.404"
touch "$GH_FIXTURES/12.500"
expect "quarantine errors when gh fails" 1 "${quarantine[@]}" --snapshot "$scratch/snapshot.json" --gh "$scratch/bin/gh"
contains "quarantine reports the gh failure" "HTTP 500"
pass "a gh failure is an error, not a pass"

# --- Drift report -----------------------------------------------------------------------------------------------

# Three real brokers back the simulator's QueueRoundTrip and EmptyBodyDispatch cells. Alpha and Beta run; Gamma is
# outside the run when --exclude-providers names it, as Azure Service Bus is in the integration workflow.
results="$scratch/results"
mkdir -p "$results/alpha/conformance/bindings" "$results/beta/conformance/bindings"
cat > "$results/alpha/conformance/snapshot.json" <<'JSON'
{
  "simulator": "Simulator",
  "providers": [
    {"name": "Simulator", "realBrokerLeaf": false, "cells": {
      "QueueRoundTrip": {"status": "Supported"}, "EmptyBodyDispatch": {"status": "Supported"}}},
    {"name": "Alpha", "realBrokerLeaf": true, "cells": {
      "QueueRoundTrip": {"status": "Supported"}, "EmptyBodyDispatch": {"status": "Supported"}}},
    {"name": "Beta", "realBrokerLeaf": true, "cells": {
      "QueueRoundTrip": {"status": "Supported"}, "EmptyBodyDispatch": {"status": "Supported"}}},
    {"name": "Gamma", "realBrokerLeaf": true, "cells": {
      "QueueRoundTrip": {"status": "Supported"}, "EmptyBodyDispatch": {"status": "Supported"}}}
  ],
  "guarantees": [
    {"id": "MSG-LANE-09", "statement": "An empty body is delivered as empty.", "status": "Bound", "manifestScenarios": ["EmptyBodyDispatch"]}
  ]
}
JSON

# write_bindings <leg> <provider>: binds each scenario to a test of the leg's assembly.
write_bindings() {
  cat > "$results/$1/conformance/bindings/Tests.$2--$2.json" <<JSON
{"provider": "$2", "bindings": [
  {"scenario": "QueueRoundTrip", "assembly": "Tests.$2", "testClass": "Tests.$2ConformanceTests", "testMethod": "should_round_trip"},
  {"scenario": "EmptyBodyDispatch", "assembly": "Tests.$2", "testClass": "Tests.$2ConformanceTests", "testMethod": "should_dispatch_empty_body"}
]}
JSON
}

# write_trx <leg> <provider> <round-trip outcome> <empty-body outcome>: the TestDefinitions/Results shape MTP writes.
write_trx() {
  local leg="$1" provider="$2" assembly="Tests.$2"
  cat > "$results/$leg/${assembly}_net10.0_x64.trx" <<XML
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="00000000-0000-0000-0000-000000000000" name="integration" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult executionId="e1" testId="t1" testName="Tests.${provider}ConformanceTests.should_round_trip" outcome="$3">
      <Output><ErrorInfo><Message>round trip $3 on $provider</Message></ErrorInfo></Output>
    </UnitTestResult>
    <UnitTestResult executionId="e2" testId="t2" testName="Tests.${provider}ConformanceTests.should_dispatch_empty_body" outcome="$4">
      <Output><ErrorInfo><Message>empty body $4 on $provider</Message></ErrorInfo></Output>
    </UnitTestResult>
  </Results>
  <TestDefinitions>
    <UnitTest name="Tests.${provider}ConformanceTests.should_round_trip" id="t1">
      <TestMethod codeBase="/runner/tests/$assembly/bin/Release/net10.0/$assembly.dll" className="Tests.${provider}ConformanceTests" name="should_round_trip" />
    </UnitTest>
    <UnitTest name="Tests.${provider}ConformanceTests.should_dispatch_empty_body" id="t2">
      <TestMethod codeBase="/runner/tests/$assembly/bin/Release/net10.0/$assembly.dll" className="Tests.${provider}ConformanceTests" name="should_dispatch_empty_body" />
    </UnitTest>
  </TestDefinitions>
</TestRun>
XML
}

write_bindings alpha Alpha
write_bindings beta Beta

write_trx alpha Alpha Failed Failed
write_trx beta Beta Passed Failed
expect "drift report labels drift" 3 "${drift[@]}" --results "$results" --exclude-providers Gamma --out "$scratch/summary.md"
contains "drift report names the drifted cell and its guarantee" "- **EmptyBodyDispatch**: guarantee MSG-LANE-09: An empty body is delivered as empty."
contains "drift report lists each backing failure under drift" "  - Beta: \`Tests.BetaConformanceTests.should_dispatch_empty_body\`: empty body Failed on Beta"
contains "drift report marks the cell in the table" "| EmptyBodyDispatch | **FAIL** | **FAIL** | **simulator drift** |"
cmp -s "$scratch/out.txt" "$scratch/summary.md" || fail "drift report: --out differs from stdout"
pass "every real leaf behind one simulator cell failing is labelled simulator drift"

contains "drift report keeps a single provider's failure apart" "  - \`Tests.AlphaConformanceTests.should_round_trip\` (Alpha/QueueRoundTrip): round trip Failed on Alpha"
contains "drift report calls it a provider failure in the table" "| QueueRoundTrip | **FAIL** | pass | provider failure |"
lacks "drift report does not list a drift test as a provider failure" "(Alpha/EmptyBodyDispatch)"
pass "a provider failing a cell another real provider passes is a provider failure, not drift"

expect "drift report waits for every backing broker" 0 "${drift[@]}" --results "$results"
contains "drift report does not judge a cell a backing broker did not run" "| EmptyBodyDispatch | **FAIL** | **FAIL** | not run | not judged: failed where it ran, a backing broker did not run it |"
contains "drift report does not call drift while Gamma has not run" "None: every simulator cell"
pass "a cell is not judged as drift while a backing broker in the run did not run it"

write_trx alpha Alpha Passed Passed
write_trx beta Beta Passed NotExecuted
expect "drift report passes a clean run" 0 "${drift[@]}" --results "$results" --exclude-providers Gamma
contains "drift report reports no provider failures" "### Provider failures

None."
contains "drift report treats a skip as not run" "| EmptyBodyDispatch | pass | skip | not judged: a backing broker did not run it |"
pass "a run with no failures reports no drift and no provider failure"

expect "drift report rejects an unknown excluded provider" 2 "${drift[@]}" --results "$results" --exclude-providers Delta
expect "drift report needs TRX files" 1 "${drift[@]}" --results "$results/alpha/conformance"
pass "the drift report rejects bad inputs"

# --- Scope selection --------------------------------------------------------------------------------------------

# value <key>: reads one key=value line the scope selection printed.
value() {
  sed -n "s/^$1=//p" "$scratch/out.txt"
}

head_sha=$(git -C "$repo_root" rev-parse HEAD)
leg_count=$("${scope[@]}" select --event workflow_dispatch | sed -n 's/^legs=//p' | python3 -I -c 'import json, sys; print(len(json.load(sys.stdin)))')
[[ "$leg_count" -gt 0 ]] || fail "scope: a dispatched run selects no legs"

# legs_selected: how many legs the last selection chose.
legs_selected() {
  value legs | python3 -I -c 'import json, sys; print(len(json.load(sys.stdin)))'
}

expect "scope runs everything on dispatch" 0 "${scope[@]}" select --event workflow_dispatch --chaos-profile smoke
[[ "$(value mode)" == "full" && "$(value run_legs)" == "true" && "$(value run_quarantine)" == "true" ]] || fail "scope: dispatch is not a full run"
[[ "$(value chaos_profile)" == "smoke" ]] || fail "scope: dispatch ignores the requested chaos sizes"
pass "a dispatched run runs every leg, chaos at the requested sizes, and the quarantine check"

expect "scope runs the full week" 0 "${scope[@]}" select --event schedule --base "$head_sha"
[[ "$(value mode)" == "full" && "$(value run_chaos)" == "true" && "$(value chaos_profile)" == "full" && "$(value run_quarantine)" == "true" ]] || fail "scope: a scheduled run is not a full run"
[[ "$(legs_selected)" == "$leg_count" ]] || fail "scope: a scheduled run skips legs"
pass "a scheduled run runs every leg, chaos at full sizes, and the quarantine check"

expect "scope runs every leg without a base" 0 "${scope[@]}" select --event push --base ""
[[ "$(legs_selected)" == "$leg_count" && "$(value run_chaos)" == "false" && "$(value run_quarantine)" == "false" ]] || fail "scope: a push with no base is not every leg alone"
pass "a push with no base to diff against runs every leg, without chaos or the quarantine check"

kafka=tests/Headless.Messaging.Kafka.Tests.Integration/Headless.Messaging.Kafka.Tests.Integration.csproj
printf '{"integration_tests": ["%s", "tests/Headless.Features.Tests.Integration/Headless.Features.Tests.Integration.csproj"]}\n' "$kafka" > "$scratch/affected.json"
for event in pull_request push; do
  expect "scope runs every leg when $event reaches one" 0 "${scope[@]}" select --event "$event" --base "$head_sha" --affected-report "$scratch/affected.json"
  [[ "$(value mode)" == "legs" && "$(legs_selected)" == "$leg_count" ]] || fail "scope: a $event reaching one suite does not run every leg: $(value legs)"
  [[ "$(value run_chaos)" == "false" && "$(value run_quarantine)" == "false" ]] || fail "scope: a $event runs chaos or the quarantine check"
  contains "scope names the affected leg" "reason=affected since ${head_sha:0:12}: kafka"
done
pass "a pull request or push that reaches one messaging suite runs every leg, so the drift report can judge each cell"

printf '{"integration_tests": ["tests/Headless.Features.Tests.Integration/Headless.Features.Tests.Integration.csproj"]}\n' > "$scratch/affected.json"
expect "scope skips an unrelated pull request" 0 "${scope[@]}" select --event pull_request --base "$head_sha" --affected-report "$scratch/affected.json"
[[ "$(value run_legs)" == "false" && "$(value run_chaos)" == "false" && "$(value legs)" == "[]" ]] || fail "scope: a pull request affecting no messaging suite still runs legs"
pass "a pull request that affects no messaging suite runs nothing"

expect "scope rejects an unknown event" 2 "${scope[@]}" select --event merge_group
pass "the scope selection rejects an event the workflow does not run on"

# The Makefile reads the leg projects from the scope script, so a local run and the workflow run the same suites.
# GNU Make 3.81 on macOS has no --eval, so the printing rule arrives on stdin beside an include of the Makefile.
# shellcheck disable=SC2016 # $(MESSAGING_INTEGRATION_PROJECTS) is a make expansion in the rule, not the shell's.
make_projects=$(printf 'include Makefile\nprint-integration-projects:\n\t@printf "%%s\\n" $(MESSAGING_INTEGRATION_PROJECTS)\n' | make -s --no-print-directory -C "$repo_root" -f - print-integration-projects)
[[ -n "$make_projects" ]] || fail "MESSAGING_INTEGRATION_PROJECTS is empty"
[[ "$make_projects" == "$("${scope[@]}" projects)" ]] || fail "MESSAGING_INTEGRATION_PROJECTS differs from the scope script's legs"
pass "the Makefile runs the same integration projects the workflow's legs do"

# --- Workflow placement -----------------------------------------------------------------------------------------

workflow="$repo_root/.github/workflows/messaging-integration.yml"
# A pull request waits only on checks that ci.yml's "CI status" needs; this workflow must never become one of them.
if grep -qF 'messaging-integration.yml' "$repo_root/.github/workflows/ci.yml"; then
  fail "ci.yml must not call the messaging integration workflow"
fi
# pull_request_target would run a fork's code with this repository's token and secrets.
if grep -qE '^\s+(pull_request_target|workflow_call|merge_group):' "$workflow"; then
  fail "the messaging integration workflow must not run on pull_request_target, workflow_call, or merge_group"
fi
grep -A1 -E '^  pull_request:' "$workflow" | grep -qE '^\s+branches: \[main\]$' || fail "the messaging integration workflow must run on pull requests to main"
# The only push it takes is a push to main; a branch push would run real-broker suites for unmerged work twice.
grep -A1 -E '^  push:' "$workflow" | grep -qE '^\s+branches: \[main\]$' || fail "the messaging integration workflow must run on push to main only"
grep -qE '^\s+schedule:' "$workflow" || fail "the messaging integration workflow must keep its weekly schedule"
# A new commit supersedes a pull request's run; a run on main is the only real-broker result for its commit.
grep -qF "cancel-in-progress: \${{ github.event_name == 'pull_request' }}" "$workflow" || fail "the messaging integration workflow must cancel only stale pull-request runs"
if grep -qE '^\s+[a-z-]+:\s*write\s*$' "$workflow"; then
  fail "the messaging integration workflow must not request a write permission"
fi
# Comments may name the flag to say it is left out; only a command passing it turns retries on.
if grep -hvE '^\s*#' "$workflow" "$repo_root/Makefile" | grep -qF -- '--retry-failed-tests'; then
  fail "the messaging integration workflow must run without retries"
fi
pass "the workflow runs on pull requests and pushes to main, weekly, or on dispatch, outside CI status, read-only, without retries"
