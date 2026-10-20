#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises the known-flake retry in scripts/proof.py against a scratch repository and a stand-in dotnet: a stage whose
# only failure is a listed flake retries that module once and passes, naming the flake and its issue; an unlisted
# failure is not retried and fails; a listed flake that fails again on its retry still fails.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# proof.py resolves the repository, and with it tests/known-flakes.txt, from its own path.
mkdir -p "$scratch/scripts" "$scratch/tests/Flaky.Tests.Unit"
cp "$repo_root/scripts/proof.py" "$scratch/scripts/proof.py"
cd "$scratch"
git init -q
git config user.name 'CI test'
git config user.email 'ci@example.invalid'
git config commit.gpgsign false
git config core.hooksPath /dev/null
touch tests/Flaky.Tests.Unit/Flaky.Tests.Unit.csproj
printf '# module test issue\nFlaky.Tests.Unit Tests.ClockTests.should_tick_* #42\n' > tests/known-flakes.txt
git add -A
git commit -qm 'Base'

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }
pass() { printf 'PASS: %s\n' "$1"; }

# trx <path> <outcome> <test name>
trx() {
  mkdir -p "$(dirname "$1")"
  cat > "$1" <<XML
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <ResultSummary outcome="Failed"><Counters total="2" /></ResultSummary>
  <Results>
    <UnitTestResult testName="Tests.ClockTests.should_pass" outcome="Passed" />
    <UnitTestResult testName="$3" outcome="$2"><Output><ErrorInfo><Message>timed out</Message></ErrorInfo></Output></UnitTestResult>
  </Results>
</TestRun>
XML
}

# The stand-in dotnet writes the retry's TRX with the outcome RETRY_OUTCOME names, and records that it ran.
cat > fake-dotnet <<'SH'
#!/usr/bin/env bash
results=""
while [ $# -gt 0 ]; do
  if [ "$1" = "--results-directory" ]; then results="$2"; shift; fi
  shift
done
mkdir -p "$results"
cat > "$results/Flaky.Tests.Unit_net10.0_arm64.trx" <<XML
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results><UnitTestResult testName="Tests.ClockTests.should_tick_on_time" outcome="$RETRY_OUTCOME" /></Results>
</TestRun>
XML
touch "$results/../retry-ran"
[ "$RETRY_OUTCOME" = "Passed" ]
SH
chmod +x fake-dotnet

# stage_failed <bundle> <failed test name>: a unit-tests stage that failed on one test.
stage_failed() {
  rm -rf "$1"
  mkdir -p "$1"
  printf 'tests/Flaky.Tests.Unit/Flaky.Tests.Unit.csproj\n' > "$1/unit.txt"
  printf '{"base": "HEAD", "changed_projects": []}\n' > "$1/affected.json"
  printf '{"name": "unit-tests", "command": [], "exit_code": 1, "seconds": 1.0, "log": "", "note": ""}\n' > "$1/stages.jsonl"
  trx "$1/unit-tests/Flaky.Tests.Unit_net10.0_arm64.trx" Failed "$2"
}

retry() { python3 scripts/proof.py retry-flakes --dir "$1" --stage unit-tests --projects "$1/unit.txt" --dotnet ./fake-dotnet -- --no-build 2> /dev/null; }

# A listed flake retries once, passes, and the bundle passes with the flake named beside its issue.
stage_failed known Tests.ClockTests.should_tick_on_time
RETRY_OUTCOME=Passed retry known || fail "a listed flake that passed on retry still failed the retry"
[ -e known/retry-ran ] || fail "the listed flake was not retried"
python3 scripts/proof.py summarize --dir known > /dev/null || fail "the bundle failed although its only failure passed on retry"
grep -q 'Tests.ClockTests.should_tick_on_time` (#42)' known/summary.md || fail "summary.md does not name the flake and its issue"
grep -q 'Tests: 2 passed, 0 failed' known/summary.md || fail "the retried flake does not count as one pass: $(grep -m1 'Tests:' known/summary.md)"
pass "a listed flake retries once and the bundle passes, naming the flake and its issue"

# A listed flake does not cover for a module that never finished: a timed-out module leaves no TRX at all.
stage_failed hung Tests.ClockTests.should_tick_on_time
printf 'tests/Hung.Tests.Unit/Hung.Tests.Unit.csproj\n' >> hung/unit.txt
if RETRY_OUTCOME=Passed retry hung; then fail "a flake retry covered for a module with no result"; fi
[ ! -e hung/retry-ran ] || fail "a stage with an unfinished module started a retry"
if python3 scripts/proof.py summarize --dir hung > /dev/null; then fail "a bundle with an unfinished module passed"; fi
pass "a listed flake does not hide a module that timed out or crashed"

# A malformed line or a messaging integration module is reported and ignored, never silently dropped or honored.
cp tests/known-flakes.txt known-flakes.bak
printf 'Flaky.Tests.Unit Tests.ClockTests.only_two_fields\nHeadless.Messaging.Nats.Tests.Integration Tests.* #9\n' >> tests/known-flakes.txt
python3 scripts/proof.py retry-flakes --dir unknown --stage unit-tests --projects unknown/unit.txt --dotnet ./fake-dotnet -- --no-build 2> lint.err || true
grep -q 'known-flakes.txt:3 ignored: expected' lint.err || fail "a malformed line was not reported: $(cat lint.err)"
grep -q 'known-flakes.txt:4 ignored: messaging integration' lint.err || fail "a messaging integration entry was not refused: $(cat lint.err)"
mv known-flakes.bak tests/known-flakes.txt
pass "a malformed or messaging integration entry is reported and ignored"

# An unlisted failure is not retried and fails the bundle.
stage_failed unknown Tests.ClockTests.should_add
if RETRY_OUTCOME=Passed retry unknown; then fail "an unlisted failure was retried as a flake"; fi
[ ! -e unknown/retry-ran ] || fail "an unlisted failure started a retry"
if python3 scripts/proof.py summarize --dir unknown > /dev/null; then fail "a bundle with an unlisted failure passed"; fi
pass "an unlisted failure is not retried and fails the bundle"

# A listed flake that fails again on its retry still fails the bundle.
stage_failed again Tests.ClockTests.should_tick_on_time
if RETRY_OUTCOME=Failed retry again; then fail "a flake that failed its retry passed"; fi
if python3 scripts/proof.py summarize --dir again > /dev/null; then fail "a bundle whose flake failed its retry passed"; fi
pass "a listed flake that fails again on retry still fails the bundle"
