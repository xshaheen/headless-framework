#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises the coverage gate in scripts/proof.py against a scratch repository: changed-line floors, the unit floor,
# packages deferred to the integration gate, and a gate skipped because a test stage did not run.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# proof.py resolves the repository from its own path, so the scratch repository carries a copy of it.
mkdir -p "$scratch/scripts" "$scratch/src/Headless.Core" "$scratch/src/Headless.Provider" "$scratch/tests/Headless.Provider.Tests.Integration"
cp "$repo_root/scripts/proof.py" "$scratch/scripts/proof.py"
cd "$scratch"
git init -q
git config user.name 'CI test'
git config user.email 'ci@example.invalid'
git config commit.gpgsign false
git config core.hooksPath /dev/null

printf 'line 1\nline 2\nline 3\nline 4\n' > src/Headless.Core/Core.cs
printf 'line 1\nline 2\n' > src/Headless.Provider/Provider.cs
touch tests/Headless.Provider.Tests.Integration/.keep
git add -A
git commit -qm 'Base'
base=$(git rev-parse HEAD)

# The branch rewrites lines 2-3 of Core.cs and line 2 of Provider.cs.
printf 'line 1\nchanged 2\nchanged 3\nline 4\n' > src/Headless.Core/Core.cs
printf 'line 1\nchanged 2\n' > src/Headless.Provider/Provider.cs

# write_bundle <dir> <core line-rate> <core line 2 hits> <core line 3 hits> <provider line 2 hits> <unit-tests exit code> [branches covered of 2 on core line 2]
write_bundle() {
  local dir="$1" rate="$2" core2="$3" core3="$4" provider2="$5" unit_exit="$6" branches="${7:-2}"
  rm -rf "$dir"
  mkdir -p "$dir/coverage"
  cat > "$dir/affected.json" <<JSON
{"base": "$base", "changed_projects": ["src/Headless.Core/Headless.Core.csproj", "src/Headless.Provider/Headless.Provider.csproj"]}
JSON
  printf '{"name": "unit-tests", "command": [], "exit_code": %s, "seconds": 1.0, "log": "", "note": ""}\n' "$unit_exit" > "$dir/stages.jsonl"
  cat > "$dir/coverage/merged.cobertura.xml" <<XML
<?xml version="1.0"?>
<coverage line-rate="0.5" branch-rate="0.5">
  <packages>
    <package name="Headless.Core" line-rate="$rate" branch-rate="1">
      <classes>
        <class name="Headless.Core.Core" filename="$scratch/src/Headless.Core/Core.cs">
          <lines>
            <line number="2" hits="$core2" branch="True" condition-coverage="$((branches * 50))% ($branches/2)"/>
            <line number="3" hits="$core3" branch="False"/>
          </lines>
        </class>
      </classes>
    </package>
    <package name="Headless.Provider" line-rate="0.1" branch-rate="0">
      <classes>
        <class name="Headless.Provider.Provider" filename="$scratch/src/Headless.Provider/Provider.cs">
          <lines>
            <line number="2" hits="$provider2" branch="False"/>
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
XML
}

# expect <label> <expected exit> <scope> [text summary.md must contain] [warn|fail]
expect() {
  local label="$1" expected="$2" scope="$3" needle="${4:-}" mode="${5:-warn}" actual=0
  python3 scripts/proof.py summarize --dir bundle --coverage-gate "$scope" --coverage-gate-mode "$mode" > /dev/null || actual=$?
  if [[ "$actual" != "$expected" ]]; then
    printf 'FAIL: %s: exit %s, expected %s\n' "$label" "$actual" "$expected" >&2
    cat bundle/summary.md >&2
    exit 1
  fi
  if [[ -n "$needle" ]] && ! grep -qF -- "$needle" bundle/summary.md; then
    printf 'FAIL: %s: summary.md lacks "%s"\n' "$label" "$needle" >&2
    cat bundle/summary.md >&2
    exit 1
  fi
  printf 'PASS: %s\n' "$label"
}

write_bundle bundle 0.9 1 1 0 0
# shellcheck disable=SC2016 # The backticks are literal Markdown in summary.md, not a command substitution.
expect "unit gate passes when unit tests cover the changed lines and the package floor" 0 unit 'Held to the changed-line floor by `make test-affected-integration`, not here: `Headless.Provider`'
if ! grep -qF -- 'Coverage: PASS (100.0% line, 100.0% branch)' bundle/summary.md; then
  printf 'FAIL: summary line does not expose the coverage result\n' >&2
  cat bundle/summary.md >&2
  exit 1
fi

write_bundle bundle 0.9 1 0 1 0
expect "unit gate warns, and keeps the proof passing, on an uncovered changed line" 0 unit "uncovered: src/Headless.Core/Core.cs: 3"

write_bundle bundle 0.9 1 0 1 0
expect "unit gate fails the proof on an uncovered changed line when strict" 1 unit "Coverage gate: FAIL" fail
if ! grep -qF -- '<details open><summary><strong>make verify-affected: FAIL</strong>' bundle/summary.md \
  || ! grep -qF -- 'Coverage: FAIL' bundle/summary.md; then
  printf 'FAIL: failed proof is not open with its coverage result visible\n' >&2
  cat bundle/summary.md >&2
  exit 1
fi

write_bundle bundle 0.5 1 1 1 0
expect "unit gate warns below the unit floor" 0 unit "Headless.Core: unit line coverage 50.0% is under the 60% floor"

write_bundle bundle 0.9 1 1 0 0 1
expect "unit gate warns below the changed-branch floor" 0 unit "changed-branch coverage 50.0% (1/2) is under the 70% floor"

write_bundle bundle 0.9 1 1 0 0
expect "integration gate holds a package with an integration project to its changed lines" 0 integration "uncovered: src/Headless.Provider/Provider.cs: 2"

write_bundle bundle 0.9 1 0 1 0
expect "integration gate passes on covered package lines and leaves the unit-gated package alone" 0 integration "Changed lines: 100.0% (1/1"
if ! grep -qF -- '| Headless.Provider | 1 | 100.0 | n/a |' bundle/summary.md; then
  printf 'FAIL: assembly without changed branches is reported as measured coverage\n' >&2
  cat bundle/summary.md >&2
  exit 1
fi

write_bundle bundle 0.9 0 0 0 -1
expect "gate is skipped, not failed, when a test stage did not run" 0 unit "Coverage gate: SKIPPED"

git add src/Headless.Core/Core.cs src/Headless.Provider/Provider.cs
git commit -qm 'Current source becomes the comparison base'
base=$(git rev-parse HEAD)
write_bundle bundle 0.9 1 1 1 0
expect "zero changed lines are not reported as 100 percent coverage" 0 unit "Changed lines: not applicable (0 executable changed lines)"
if ! grep -qF -- 'Coverage: PASS</summary>' bundle/summary.md; then
  printf 'FAIL: zero changed lines or branches are reported as measured coverage in the summary line\n' >&2
  cat bundle/summary.md >&2
  exit 1
fi

write_bundle bundle 0.9 1 1 0 0
expect "no gate without --coverage-gate" 0 none

expected_summary='<details><summary><strong>make verify-affected: PASS</strong> · Tests: 0 passed, 0 failed, 0 skipped · Compiler diagnostics: 0 · Analyzer findings: 0</summary>'
if ! grep -qF -- "$expected_summary" bundle/summary.md; then
  printf 'FAIL: summary line is not compact or scan-friendly\n' >&2
  cat bundle/summary.md >&2
  exit 1
fi
# shellcheck disable=SC2016 # The backticks are literal Markdown, not command substitution.
if ! grep -qF -- '| `unit-tests` | PASS | 1.0s |' bundle/summary.md; then
  printf 'FAIL: stage results are not formatted consistently\n' >&2
  cat bundle/summary.md >&2
  exit 1
fi
if grep -qE '^### (Tests:|Compiler diagnostics|Analyzer findings)' bundle/summary.md; then
  printf 'FAIL: summary repeats totals or renders empty sections\n' >&2
  cat bundle/summary.md >&2
  exit 1
fi
if ! grep -qE '^## Verification$' "$repo_root/.github/PULL_REQUEST_TEMPLATE.md"; then
  printf 'FAIL: the PR template does not expose Verification as a normal heading\n' >&2
  exit 1
fi
if grep -qF -- '<summary><h2>Verification</h2></summary>' "$repo_root/.github/PULL_REQUEST_TEMPLATE.md"; then
  printf 'FAIL: the PR template hides Verification behind an outer disclosure\n' >&2
  exit 1
fi
