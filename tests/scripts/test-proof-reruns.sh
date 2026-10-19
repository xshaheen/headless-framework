#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises the proof tooling that decides how much a gate re-runs, against a scratch repository: a version-only edit
# to Directory.Packages.props selects only the projects that resolve a moved package (while CI still runs everything),
# any other edit to it selects every project, a bundle that selected nothing while HEAD changes src/ fails,
# `proof.py failed` re-runs a failed integration module, and `proof.py analyzers-clean` reports analyzer findings.
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

affected() { python3 scripts/project-graph.py affected --base origin/main --list "$1"; }

# A version-only edit selects the projects whose lock file resolves the moved package, and their tests.
sed -i.bak 's|"Confluent.Kafka" Version="2.16.0"|"Confluent.Kafka" Version="2.16.1"|' Directory.Packages.props && rm Directory.Packages.props.bak
changed=$(affected changed)
[[ "$changed" == "src/Kafka/Kafka.csproj" ]] || fail "a Kafka version bump selected: ${changed:-nothing}"
affected unit | grep -qx 'tests/Kafka.Tests.Unit/Kafka.Tests.Unit.csproj' || fail "a Kafka version bump did not select the Kafka unit tests"
affected json | python3 -c 'import json, sys; r = json.load(sys.stdin); sys.exit(0 if r["package_triggers"] == {"Directory.Packages.props": ["confluent.kafka"]} and not r["global_triggers"] else 1)' \
  || fail "a version-only edit was not reported as a package trigger"
pass "a version-only edit selects only the projects that resolve the moved package"

# A pull request's CI run still builds everything for a package bump.
git commit -qam 'Bump Kafka'
scope=$(python3 scripts/project-graph.py ci-scope --event pull_request --base "$base" --head HEAD --out-dir ci 2>/dev/null)
grep -qx 'scope=full' <<< "$scope" || fail "CI narrowed a package bump: $scope"
pass "CI still runs the whole solution for a package bump"
git reset -q --hard "$base"

# A global reference's version resolves in every project.
sed -i.bak 's|"Shared.Analyzer" Version="1.0.0"|"Shared.Analyzer" Version="1.0.1"|' Directory.Packages.props && rm Directory.Packages.props.bak
[[ $(affected changed | wc -l) -eq 4 ]] || fail "a global reference bump did not select every project"
pass "a global reference's version bump selects every project that resolves it"
git checkout -q -- Directory.Packages.props

# Any edit besides versions keeps the file's whole scope.
sed -i.bak 's|<ManagePackageVersionsCentrally>true|<ManagePackageVersionsCentrally>false|' Directory.Packages.props && rm Directory.Packages.props.bak
[[ $(affected changed | wc -l) -eq 4 ]] || fail "a property edit did not select every project"
affected json | python3 -c 'import json, sys; sys.exit(0 if json.load(sys.stdin)["global_triggers"] == ["Directory.Packages.props"] else 1)' \
  || fail "a property edit was not reported as a build-wide trigger"
git checkout -q -- Directory.Packages.props
sed -i.bak 's|<GlobalPackageReference Include="Shared.Analyzer" Version="1.0.0" />|&<GlobalPackageReference Include="New.Analyzer" Version="1.0.0" />|' Directory.Packages.props && rm Directory.Packages.props.bak
[[ $(affected changed | wc -l) -eq 4 ]] || fail "an added global reference did not select every project"
git checkout -q -- Directory.Packages.props
pass "any edit besides versions, an added global reference included, selects every project"

# A bundle that selected nothing while HEAD changes src/ against origin/main fails; one that selected projects does not.
printf '// changed\n' >> src/Kafka/Kafka.csproj
git commit -qam 'Change Kafka'
mkdir -p bundle
printf '{"base": "origin/feature", "changed_projects": []}\n' > bundle/affected.json
: > bundle/stages.jsonl
status=0
python3 scripts/proof.py summarize --dir bundle > /dev/null || status=$?
[[ "$status" -eq 1 ]] || fail "an empty selection on a src change passed (exit $status)"
grep -q 'affected-selection' bundle/summary.md || fail "summary.md does not name the affected-selection stage"
printf '{"base": "origin/main", "changed_projects": ["src/Kafka/Kafka.csproj"]}\n' > bundle/affected.json
python3 scripts/proof.py summarize --dir bundle > /dev/null || fail "a bundle that selected the changed project failed"
pass "a bundle that selected nothing while HEAD changes src/ fails"

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
