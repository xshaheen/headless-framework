#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises how scripts/project-graph.py selects projects for a change to a build file that project files <Import>:
# it selects the importers and their dependents, not every project.
# The project files below hold MSBuild properties such as $(MSBuildThisFileDirectory), which the shell must not expand.
# shellcheck disable=SC2016
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

# write_project <path.csproj> <body>
write_project() {
  mkdir -p "$(dirname -- "$1")"
  printf '<Project Sdk="Microsoft.NET.Sdk">\n%s\n</Project>\n' "$2" > "$1"
}

mkdir -p eng
printf '<Project />\n' > eng/Spa.targets
printf '<Project />\n' > eng/Other.targets
write_project src/Headless.Spa/Headless.Spa.csproj '  <Import Project="$(MSBuildThisFileDirectory)..\..\eng\Spa.targets" />'
write_project src/Headless.Relative/Headless.Relative.csproj '  <Import Project="../../eng/Spa.targets" />'
write_project src/Headless.Spa.Consumer/Headless.Spa.Consumer.csproj \
  '  <ItemGroup><ProjectReference Include="..\Headless.Spa\Headless.Spa.csproj" /></ItemGroup>'
write_project src/Headless.Unrelated/Headless.Unrelated.csproj '  <Import Project="$(SomeRoot)/eng/Other.targets" />'
git add -A
git commit -qm 'Base'

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }
list() { printf 'eng/%s\n' "$1" | python3 scripts/project-graph.py affected --base HEAD --files-from - --list "$2" | tr '\n' ' '; }

[ "$(list Spa.targets changed)" = "src/Headless.Relative/Headless.Relative.csproj src/Headless.Spa/Headless.Spa.csproj " ] \
  || fail "an imported build file should select its importers only: $(list Spa.targets changed)"
echo 'PASS: imported build file selects its importers'

[ "$(list Spa.targets affected)" = "src/Headless.Relative/Headless.Relative.csproj src/Headless.Spa.Consumer/Headless.Spa.Consumer.csproj src/Headless.Spa/Headless.Spa.csproj " ] \
  || fail "an imported build file should add the importers' dependents: $(list Spa.targets affected)"
echo 'PASS: imported build file adds the dependents'

# An import through a property the script cannot resolve maps to no project, so the file stays unmapped
# (and CI's scope falls back to a full run) instead of being guessed.
[ -z "$(list Other.targets changed)" ] || fail "an unresolvable import should select nothing: $(list Other.targets changed)"
printf 'eng/Other.targets\n' | python3 scripts/project-graph.py affected --base HEAD --files-from - --list json | grep -q '"eng/Other.targets"' \
  || fail "an unresolvable import should leave the file unmapped"
echo 'PASS: unresolvable import leaves the file unmapped'
