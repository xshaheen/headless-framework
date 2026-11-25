#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises proof reuse: `scripts/project-graph.py input-key` stays equal across a message-only amend and a rebase onto
# a main that touched nothing the selection builds, changes when the build closure or the worktree changes, and
# `scripts/proof.py reuse` reuses only a passing bundle with the same key.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# Both scripts resolve the repository from their own path, so the scratch repository carries copies of them.
mkdir -p "$scratch/scripts"
cp "$repo_root/scripts/project-graph.py" "$repo_root/scripts/proof.py" "$scratch/scripts/"
cd "$scratch"
git init -q -b main
git config user.name 'CI test'
git config user.email 'ci@example.invalid'
git config commit.gpgsign false
git config core.hooksPath /dev/null

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

# add_project <path.csproj> [referenced csproj]
add_project() {
  mkdir -p "$(dirname -- "$1")"
  if [ -n "${2:-}" ]; then
    printf '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="%s" /></ItemGroup></Project>\n' "$2" > "$1"
  else
    printf '<Project Sdk="Microsoft.NET.Sdk" />\n' > "$1"
  fi
}

add_project src/Headless.Base/Headless.Base.csproj
add_project src/Headless.Feature/Headless.Feature.csproj ../Headless.Base/Headless.Base.csproj
add_project src/Headless.Other/Headless.Other.csproj
for project in Base Feature Other; do echo "// v1" > "src/Headless.$project/Code.cs"; done
printf '<Project />\n' > Directory.Build.props
git add -A && git commit -q -m 'initial'

git switch -q -c feature
echo "// change" >> src/Headless.Feature/Code.cs
git commit -q -am 'change the feature'

key() {
  rm -rf "$scratch/run"
  python3 scripts/project-graph.py affected --base main --out-dir "$scratch/run" 2>/dev/null
  python3 scripts/project-graph.py input-key --dir "$scratch/run" --salt verify
}

original=$(key)

git commit -q --amend -m 'reword only'
[ "$(key)" = "$original" ] || fail "a message-only amend changed the key"

git switch -q main
echo "// unrelated" >> src/Headless.Other/Code.cs
git commit -q -am 'main touches an unrelated project'
git switch -q feature
git rebase -q main
[ "$(key)" = "$original" ] || fail "a rebase onto an unrelated main change changed the key"
proved_commit=$(git rev-parse HEAD)

git switch -q main
echo "// dependency" >> src/Headless.Base/Code.cs
git commit -q -am 'main touches a referenced project'
git switch -q feature
git rebase -q main
rebased_key=$(key)
[ "$rebased_key" != "$original" ] || fail "a change to a referenced project kept the key"

echo "// uncommitted" >> src/Headless.Feature/Code.cs
[ "$(key)" != "$rebased_key" ] || fail "an uncommitted edit in the selection kept the key"
git checkout -q -- src/Headless.Feature/Code.cs

echo "<Project><!-- changed --></Project>" > Directory.Build.props
[ "$(key)" != "$rebased_key" ] || fail "a scoped build file above the selection kept the key"
git checkout -q -- Directory.Build.props

[ "$(python3 scripts/project-graph.py input-key --dir "$scratch/run" --salt integration)" != "$rebased_key" ] \
  || fail "a different salt kept the key"

# reuse: a passing bundle with the same key is reused and names the commit that ran it; others are not.
root="$scratch/proof"
write_bundle() { # name verdict key commit
  mkdir -p "$root/$1"
  python3 - "$root/$1/summary.json" "$2" "$3" "$4" <<'PY'
import json, sys
path, verdict, key, commit = sys.argv[1:]
summary = {
    "generated_at": "2026-01-01T00:00:00+00:00", "commit": commit, "dirty": False, "base": "main", "verdict": verdict,
    "stages": [{"name": "build", "exit_code": 0, "seconds": 9.0}],
    "affected": {"changed_projects": [], "unit_tests": [], "integration_tests": [], "global_triggers": [], "unmapped_files": []},
    "tests": {"passed": 3, "failed": 0, "skipped": 0, "modules": []}, "compiler_diagnostics": [], "analyzer_findings": [],
    "coverage": [], "coverage_gate": None, "target": "make verify-affected", "input_key": key,
}
json.dump(summary, open(path, "w"))
PY
}
fresh() { rm -rf "$root/20260109T000000Z-verify"; cp -R "$scratch/run" "$root/20260109T000000Z-verify"; }

write_bundle 20260101T000000Z-verify pass "$rebased_key" "$proved_commit"
write_bundle 20260102T000000Z-verify fail "$rebased_key" "$proved_commit"
write_bundle 20260103T000000Z-integration pass other-key "$proved_commit"
fresh
python3 scripts/proof.py reuse --dir "$root/20260109T000000Z-verify" --root "$root" --key "$rebased_key" 2>/dev/null \
  || fail "a passing bundle with the same key was not reused"
python3 - "$root/20260109T000000Z-verify/summary.json" "$proved_commit" "$(git rev-parse HEAD)" <<'PY' || fail "the reused summary does not name both commits"
import json, sys
summary = json.load(open(sys.argv[1]))
assert summary["reused_from"]["commit"] == sys.argv[2], summary["reused_from"]
assert summary["reused_from"]["bundle"].endswith("20260101T000000Z-verify"), summary["reused_from"]
assert summary["commit"] == sys.argv[3], summary["commit"]
assert summary["verdict"] == "pass"
PY
grep -q "^Reused: commit \`${proved_commit:0:12}\`" "$root/20260109T000000Z-verify/summary.md" \
  || fail "summary.md does not say the proof was reused"

rm "$root/20260101T000000Z-verify/summary.json"
fresh
if python3 scripts/proof.py reuse --dir "$root/20260109T000000Z-verify" --root "$root" --key "$rebased_key" 2>/dev/null; then
  fail "a failing bundle was reused"
fi
if python3 scripts/proof.py reuse --dir "$root/20260109T000000Z-verify" --root "$root" --key other-key 2>/dev/null; then
  fail "an integration bundle was reused for a verify run"
fi
if python3 scripts/proof.py reuse --dir "$root/20260109T000000Z-verify" --root "$root" --key "" 2>/dev/null; then
  fail "an empty key was reused"
fi

# Repository-wide files count only for what reaches the selection. Each case lands on main and the branch rebases onto
# it, which is how the change reaches an open branch; an uncommitted edit would also change the branch's patch.
# The selection and proof directories live in the scratch repository, so keep them out of those commits.
printf 'run/\nproof/\n' >> .git/info/exclude
on_main() { # message, then the command that edits main
  local message=$1; shift
  git switch -q main
  "$@"
  git add -A && git commit -q -m "$message"
  git switch -q feature
  git rebase -q main
}
lock() { # project directory, then the package ids its lock file resolves
  local dir=$1; shift
  python3 - "$dir/packages.lock.json" "$@" <<'PY'
import json, sys
json.dump({"version": 1, "dependencies": {"net10.0": {name: {"type": "Direct"} for name in sys.argv[2:]}}}, open(sys.argv[1], "w"))
PY
}
setup_shared() {
  printf 'all:\n' > Makefile
  printf '<Solution>\n  <Project Path="src\\Headless.Base\\Headless.Base.csproj" />\n  <Project Path="src\\Headless.Feature\\Headless.Feature.csproj" />\n</Solution>\n' > headless-framework.slnx
  mkdir -p eng
  printf '# rule count path\nreal-wait 1 src/Headless.Feature/Code.cs\nreal-wait 1 src/Headless.Other/Code.cs\n' > eng/test-discipline-baseline.txt
  printf '<Project><ItemGroup>\n<PackageVersion Include="Pkg.Used" Version="1.0.0" />\n<PackageVersion Include="Pkg.Unused" Version="1.0.0" />\n</ItemGroup></Project>\n' > Directory.Packages.props
  lock src/Headless.Base Pkg.Used
  lock src/Headless.Feature Pkg.Used
}
on_main 'add the shared files' setup_shared
shared_key=$(key)

on_main 'gate tooling only' sh -c 'printf "all:\n\t@true\n" > Makefile; echo "# tooling" >> scripts/proof.py'
[ "$(key)" = "$shared_key" ] || fail "a Makefile or scripts/ change kept no reuse without a gate version bump"
[ "$(python3 scripts/project-graph.py input-key --dir "$scratch/run" --salt "verify gate-v2")" != "$shared_key" ] \
  || fail "a gate version bump kept the key"

on_main 'list an unrelated project' sed -i.bak 's#</Solution>#  <Project Path="src\\Headless.Other\\Headless.Other.csproj" /></Solution>#' headless-framework.slnx
rm -f headless-framework.slnx.bak
[ "$(key)" = "$shared_key" ] || fail "a solution entry outside the closure changed the key"

on_main 'baseline of an unrelated file' sed -i.bak 's#real-wait 1 src/Headless.Other/Code.cs#real-wait 2 src/Headless.Other/Code.cs#' eng/test-discipline-baseline.txt
rm -f eng/test-discipline-baseline.txt.bak
[ "$(key)" = "$shared_key" ] || fail "a baseline entry outside the closure changed the key"

on_main 'bump a package nothing here resolves' sed -i.bak 's#Pkg.Unused" Version="1.0.0"#Pkg.Unused" Version="2.0.0"#' Directory.Packages.props
rm -f Directory.Packages.props.bak
[ "$(key)" = "$shared_key" ] || fail "a version bump of an unresolved package changed the key"

on_main 'bump a package the closure resolves' sed -i.bak 's#Pkg.Used" Version="1.0.0"#Pkg.Used" Version="2.0.0"#' Directory.Packages.props
rm -f Directory.Packages.props.bak
bumped_key=$(key)
[ "$bumped_key" != "$shared_key" ] || fail "a version bump of a resolved package kept the key"

on_main 'baseline of a closure file' sed -i.bak 's#real-wait 1 src/Headless.Feature/Code.cs#real-wait 0 src/Headless.Feature/Code.cs#' eng/test-discipline-baseline.txt
rm -f eng/test-discipline-baseline.txt.bak
baseline_key=$(key)
[ "$baseline_key" != "$bumped_key" ] || fail "a baseline entry inside the closure kept the key"

on_main 'drop a closure project from the solution' sed -i.bak '/Headless.Base.csproj/d' headless-framework.slnx
rm -f headless-framework.slnx.bak
unlisted_key=$(key)
[ "$unlisted_key" != "$baseline_key" ] || fail "a solution entry inside the closure kept the key"

on_main 'a central property' sed -i.bak 's#<Project>#<Project><PropertyGroup><X>1</X></PropertyGroup>#' Directory.Packages.props
rm -f Directory.Packages.props.bak
[ "$(key)" != "$unlisted_key" ] || fail "a non-version change to the central package file kept the key"

rm src/Headless.Base/packages.lock.json
no_lock_key=$(key)
git checkout -q -- src/Headless.Base/packages.lock.json
on_main 'bump the unused package again' sed -i.bak 's#Pkg.Unused" Version="2.0.0"#Pkg.Unused" Version="3.0.0"#' Directory.Packages.props
rm -f Directory.Packages.props.bak
[ "$no_lock_key" != "$(key)" ] || fail "deleting a lock file kept the key"

# Unit-test modules get their own keys: a change one module does not compile leaves its key, so verify-affected
# reuses its earlier pass and reruns only the module the change reaches.
add_unit_tests() {
  add_project tests/Headless.Base.Tests.Unit/Headless.Base.Tests.Unit.csproj ../../src/Headless.Base/Headless.Base.csproj
  add_project tests/Headless.Feature.Tests.Unit/Headless.Feature.Tests.Unit.csproj ../../src/Headless.Feature/Headless.Feature.csproj
}
on_main 'add unit tests' add_unit_tests
echo "// branch touches base" >> src/Headless.Base/Code.cs
git commit -q -am 'branch touches base'
module_key() { # module key of a unit-test project in the current selection
  python3 scripts/project-graph.py module-keys --dir "$scratch/run" --salt verify
  python3 -c 'import json, sys; print(json.load(open(sys.argv[1]))[sys.argv[2]])' "$scratch/run/module-keys.json" "$1"
}
key > /dev/null
base_tests=tests/Headless.Base.Tests.Unit/Headless.Base.Tests.Unit.csproj
feature_tests=tests/Headless.Feature.Tests.Unit/Headless.Feature.Tests.Unit.csproj
base_module=$(module_key "$base_tests")
feature_module=$(module_key "$feature_tests")
on_main 'main touches the feature' sh -c 'echo "// main" > src/Headless.Feature/More.cs'
key > /dev/null
[ "$(module_key "$base_tests")" = "$base_module" ] || fail "a change outside a unit module's closure changed its key"
[ "$(module_key "$feature_tests")" != "$feature_module" ] || fail "a change inside a unit module's closure kept its key"

echo "test-proof-reuse: all cases passed"
