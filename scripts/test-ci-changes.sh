#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
git init -q "$scratch"
cd "$scratch"
git config user.name 'CI test'
git config user.email 'ci@example.invalid'
git config commit.gpgsign false
git config core.hooksPath /dev/null
git commit -qm 'Empty base' --allow-empty
base=$(git rev-parse HEAD)

assert_checks() {
  local label="$1" event="$2" from="$3" to="$4" expected actual
  expected=$(printf 'dotnet=%s\ndashboards=%s\ntus=%s\n' "$5" "$6" "$7")
  actual=$(GITHUB_EVENT_NAME="$event" BASE_SHA="$from" HEAD_SHA="$to" bash "$script_dir/ci-changes.sh")
  if [[ "$actual" != "$expected" ]]; then
    printf 'FAIL: %s\nExpected:\n%s\nActual:\n%s\n' "$label" "$expected" "$actual" >&2
    exit 1
  fi
  printf 'PASS: %s\n' "$label"
}

add_path() {
  mkdir -p "$(dirname -- "$1")"
  touch "$1"
  git add -- "$1"
  git commit -qm 'Add fixture'
}

assert_checks 'empty diff' pull_request "$base" HEAD false false false
add_path README.md
add_path 'docs/guide with spaces.md'
assert_checks 'docs only' pull_request "$base" HEAD false false false
before=$(git rev-parse HEAD)
add_path $'src/Core/File with\nnewline.cs'
assert_checks 'backend with unusual filename' push "$before" HEAD true false false
before=$(git rev-parse HEAD)
git mv src/Core/* docs/moved.md
git commit -qam 'Move code into docs'
assert_checks 'rename out of source' pull_request "$before" HEAD true false false
before=$(git rev-parse HEAD)
add_path src/Headless.Jobs.Dashboard/wwwroot/src/App.vue
assert_checks 'dashboard' pull_request "$before" HEAD true true false
before=$(git rev-parse HEAD)
add_path demo/Headless.Tus.Demo/frontend/src/App.tsx
assert_checks 'Tus frontend' pull_request "$before" HEAD false false true
before=$(git rev-parse HEAD)
git rm -q demo/Headless.Tus.Demo/frontend/src/App.tsx
git commit -qm 'Delete frontend file'
assert_checks 'deletion' push "$before" HEAD false false true
before=$(git rev-parse HEAD)
add_path .editorconfig
assert_checks 'shared configuration' pull_request "$before" HEAD true true true
assert_checks 'missing history' push 0000000000000000000000000000000000000000 HEAD true true true
assert_checks 'missing base' push '' HEAD true true true
assert_checks 'release' release "$base" HEAD true true true
assert_checks 'manual dispatch' workflow_dispatch "$base" HEAD true true true
before=$(git rev-parse HEAD)
for ((i = 0; i < 350; i++)); do touch "docs/page-$i.md"; done
git add docs
git commit -qm 'Large documentation update'
add_path tests/Core/Regression.cs
assert_checks 'source after 350 docs paths' pull_request "$before" HEAD true false false
