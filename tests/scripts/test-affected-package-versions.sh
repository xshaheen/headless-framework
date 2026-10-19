#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises how scripts/project-graph.py selects projects for a Directory.Packages.props change: a version-only edit
# selects the projects whose lock files resolve the changed packages, and any other edit still selects everything.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# project-graph.py resolves the repository from its own path, so the scratch repository carries a copy of it.
mkdir -p "$scratch/scripts"
cp "$repo_root/scripts/project-graph.py" "$scratch/scripts/project-graph.py"
cd "$scratch"
git init -q
git config user.name 'CI test'
git config user.email 'ci@example.invalid'
git config commit.gpgsign false
git config core.hooksPath /dev/null

# add_project <path.csproj> <lock-file packages...>; no packages means no lock file.
add_project() {
  local project="$1" dir
  shift
  dir=$(dirname -- "$project")
  mkdir -p "$dir"
  printf '<Project Sdk="Microsoft.NET.Sdk" />\n' > "$project"
  if [ "$#" -gt 0 ]; then
    python3 - "$dir/packages.lock.json" "$@" <<'PY'
import json, sys
print(json.dumps({"version": 2, "dependencies": {"net10.0": {name: {"type": "Transitive"} for name in sys.argv[2:]}}}), file=open(sys.argv[1], "w"))
PY
  fi
}

write_props() {
  cat > Directory.Packages.props <<XML
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>$1
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Redis.Client" Version="$2" />
    <PackageVersion Include="Bogus" Version="1.0.0" />
  </ItemGroup>
  <ItemGroup>
    <GlobalPackageReference Include="Some.Analyzer" Version="$3" />
  </ItemGroup>
  <ItemGroup Condition="'\$(IsPackable)' == 'false'">
    <PackageVersion Include="Some.Tool" Version="1.0.0" />
  </ItemGroup>
</Project>
XML
}

add_project src/Headless.Caching.Redis/Headless.Caching.Redis.csproj Redis.Client Some.Analyzer
add_project src/Headless.Checks/Headless.Checks.csproj Some.Analyzer
add_project tests/Headless.Checks.Tests.Unit/Headless.Checks.Tests.Unit.csproj Bogus Some.Analyzer
add_project src/Headless.NoLock/Headless.NoLock.csproj
write_props "" 1.0.0 1.0.0
git add -A
git commit -qm 'Base'

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }
changed() { python3 scripts/project-graph.py affected --base HEAD --list changed | tr '\n' ' '; }
everything="src/Headless.Caching.Redis/Headless.Caching.Redis.csproj src/Headless.Checks/Headless.Checks.csproj src/Headless.NoLock/Headless.NoLock.csproj tests/Headless.Checks.Tests.Unit/Headless.Checks.Tests.Unit.csproj "

write_props "" 2.0.0 1.0.0
[ "$(changed)" = "src/Headless.Caching.Redis/Headless.Caching.Redis.csproj src/Headless.NoLock/Headless.NoLock.csproj " ] \
  || fail "a version bump should select its lock-file users and the project without a lock file: $(changed)"
python3 scripts/project-graph.py affected --base HEAD --list json | grep -q '"redis.client"' || fail "the report should name the changed package"
echo 'PASS: version bump selects lock-file users'

write_props "" 1.0.0 2.0.0
[ "$(changed)" = "$everything" ] || fail "a GlobalPackageReference bump should select everything: $(changed)"
echo 'PASS: GlobalPackageReference bump selects everything'

write_props $'\n    <SomeProperty>true</SomeProperty>' 1.0.0 1.0.0
[ "$(changed)" = "$everything" ] || fail "a property edit should select everything: $(changed)"
echo 'PASS: property edit selects everything'

# Moving an unchanged version into the conditioned ItemGroup changes which projects can resolve it, so it selects
# the package's lock-file users like a bump does.
cat > Directory.Packages.props <<'XML'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Redis.Client" Version="1.0.0" />
  </ItemGroup>
  <ItemGroup>
    <GlobalPackageReference Include="Some.Analyzer" Version="1.0.0" />
  </ItemGroup>
  <ItemGroup Condition="'$(IsPackable)' == 'false'">
    <PackageVersion Include="Some.Tool" Version="1.0.0" />
    <PackageVersion Include="Bogus" Version="1.0.0" />
  </ItemGroup>
</Project>
XML
[ "$(changed)" = "src/Headless.NoLock/Headless.NoLock.csproj tests/Headless.Checks.Tests.Unit/Headless.Checks.Tests.Unit.csproj " ] \
  || fail "moving a version into a conditioned ItemGroup should select its lock-file users: $(changed)"
echo 'PASS: version moved between conditioned ItemGroups selects lock-file users'

# CI's scope never narrows: a central package file change is still a build-wide trigger there.
write_props "" 2.0.0 1.0.0
git commit -qam 'Bump'
python3 scripts/project-graph.py ci-scope --event pull_request --base HEAD~1 --head HEAD --out-dir "$scratch/ci" 2>/dev/null | grep -qx 'scope=full' \
  || fail "ci-scope should keep the full run for Directory.Packages.props"
echo 'PASS: ci-scope keeps the full run'
