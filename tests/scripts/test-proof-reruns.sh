#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises the proof tooling that decides how much a gate re-runs, against a scratch repository: a bundle that
# selected nothing while HEAD changes src/ fails, `proof.py failed` re-runs a failed integration module, and
# `proof.py analyzers-clean` reports analyzer findings.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# Both tools resolve the repository from their own path, so the scratch repository carries copies of them.
mkdir -p "$scratch/scripts"
cp "$repo_root/scripts/proof.py" "$repo_root/scripts/project-graph.py" "$scratch/scripts/"
cd "$scratch"
git init -q
git config user.name 'CI test'
git config user.email 'ci@example.invalid'
git config commit.gpgsign false
git config core.hooksPath /dev/null

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }
pass() { printf 'PASS: %s\n' "$1"; }

# project <path> <package resolved by its lock file> [project reference]
project() {
  local path="$1" package="$2" reference="${3:-}" dir
  dir=$(dirname "$path")
  mkdir -p "$dir"
  {
    printf '<Project Sdk="Microsoft.NET.Sdk">\n  <ItemGroup>\n'
    if [[ -n "$reference" ]]; then printf '    <ProjectReference Include="%s" />\n' "$reference"; fi
    printf '  </ItemGroup>\n</Project>\n'
  } > "$path"
  printf '{"version": 2, "dependencies": {"net10.0": {"%s": {"type": "Direct"}, "Shared.Analyzer": {"type": "Direct"}}}}\n' "$package" > "$dir/packages.lock.json"
}

project src/Kafka/Kafka.csproj Confluent.Kafka
project src/Redis/Redis.csproj StackExchange.Redis
project tests/Kafka.Tests.Unit/Kafka.Tests.Unit.csproj xunit.v3 ../../src/Kafka/Kafka.csproj
project tests/Kafka.Tests.Integration/Kafka.Tests.Integration.csproj xunit.v3 ../../src/Kafka/Kafka.csproj
cat > Directory.Packages.props <<'XML'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Confluent.Kafka" Version="2.16.0" />
    <PackageVersion Include="StackExchange.Redis" Version="2.9.0" />
  </ItemGroup>
  <ItemGroup>
    <GlobalPackageReference Include="Shared.Analyzer" Version="1.0.0" />
  </ItemGroup>
</Project>
XML
git add -A
git commit -qm 'Base'
base=$(git rev-parse HEAD)
git update-ref refs/remotes/origin/main "$base"

# A bundle that selected nothing against the branch's own pushed copy fails while HEAD changes src/ against
# origin/main; against origin/main itself an empty selection is real (a src/ file no project owns) and passes.
printf '// changed\n' >> src/Kafka/Kafka.csproj
git commit -qam 'Change Kafka'
git update-ref refs/remotes/origin/feature HEAD
mkdir -p bundle
printf '{"base": "origin/feature", "changed_projects": []}\n' > bundle/affected.json
: > bundle/stages.jsonl
status=0
python3 scripts/proof.py summarize --dir bundle > /dev/null || status=$?
[[ "$status" -eq 1 ]] || fail "an empty selection against the branch's own copy passed (exit $status)"
grep -q 'affected-selection' bundle/summary.md || fail "summary.md does not name the affected-selection stage"
printf '{"base": "origin/main", "changed_projects": []}\n' > bundle/affected.json
python3 scripts/proof.py summarize --dir bundle > /dev/null || fail "an empty selection against origin/main failed"
printf '{"base": "origin/main", "changed_projects": ["src/Kafka/Kafka.csproj"]}\n' > bundle/affected.json
python3 scripts/proof.py summarize --dir bundle > /dev/null || fail "a bundle that selected the changed project failed"
pass "an empty selection fails only against a base other than origin/main"

# `failed` reads an integration bundle and maps its failed module to the integration project.
proof=artifacts/proof/20990101T000000Z-integration
mkdir -p "$proof/integration-tests"
printf 'tests/Kafka.Tests.Integration/Kafka.Tests.Integration.csproj\n' > "$proof/integration.txt"
printf '{"base": "origin/main", "changed_projects": ["src/Kafka/Kafka.csproj"]}\n' > "$proof/affected.json"
printf '{"name": "integration-tests", "command": [], "exit_code": 1, "seconds": 1.0, "log": "", "note": ""}\n' > "$proof/stages.jsonl"
cat > "$proof/integration-tests/Kafka.Tests.Integration_net10.0_arm64.trx" <<'XML'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    <UnitTestResult testName="should_consume" outcome="Failed"><Output><ErrorInfo><Message>broker timed out</Message></ErrorInfo></Output></UnitTestResult>
  </Results>
</TestRun>
XML
python3 scripts/proof.py summarize --dir "$proof" > /dev/null || true
rerun=$(python3 scripts/proof.py failed --root artifacts/proof 2>/dev/null) || fail "failed rejected the integration bundle"
[[ "$rerun" == "tests/Kafka.Tests.Integration/Kafka.Tests.Integration.csproj" ]] || fail "failed selected: ${rerun:-nothing}"
pass "failed re-runs the module that failed in an integration bundle"

# analyzers-clean fails on a visible finding in a changed project and passes without one.
mkdir -p "$proof/analyzers"
printf '[{"FilePath": "%s/src/Kafka/Consumer.cs", "FileChanges": [{"LineNumber": 3, "DiagnosticId": "CA2000", "FormatDescription": "warning CA2000: Dispose objects"}]}]\n' "$(pwd -P)" > "$proof/analyzers/format-report.json"
if python3 scripts/proof.py analyzers-clean --dir "$proof" 2> /dev/null; then fail "analyzers-clean passed with a finding"; fi
printf '[]\n' > "$proof/analyzers/format-report.json"
python3 scripts/proof.py analyzers-clean --dir "$proof" || fail "analyzers-clean failed without a finding"
pass "analyzers-clean reports a finding in a changed project"
