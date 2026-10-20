#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises `scripts/proof.py prune`: the newest bundles of each kind survive, counting the run about to start, other
# directories are never touched, and --keep 0 keeps everything.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
root="$scratch/proof"

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

reset() {
  rm -rf "$root"
  mkdir -p "$root"
  local name
  for name in 20260101T000000Z-verify 20260102T000000Z-verify 20260103T000000Z-verify 20260104T000000Z-verify \
    20260101T000000Z-integration 20260102T000000Z-integration 20260101T000000Z-quality notes; do
    mkdir -p "$root/$name"
  done
  touch "$root/20260105T000000Z-verify.txt"
}

remaining() { (cd "$root" && find . -mindepth 1 -maxdepth 1 | sed 's|^\./||' | sort | tr '\n' ' '); }

# A new verify run with --keep 3 leaves two older verify bundles beside it; other kinds keep their newest three.
reset
python3 "$repo_root/scripts/proof.py" prune --root "$root" --keep 3 --starting "artifacts/proof/20260105T000000Z-verify" 2>/dev/null
expected="20260101T000000Z-integration 20260101T000000Z-quality 20260102T000000Z-integration 20260103T000000Z-verify 20260104T000000Z-verify 20260105T000000Z-verify.txt notes "
[ "$(remaining)" = "$expected" ] || fail "keep 3 for a starting verify run left: $(remaining)"

# --keep 1 for an integration run removes every older integration bundle and trims the other kinds to one.
reset
python3 "$repo_root/scripts/proof.py" prune --root "$root" --keep 1 --starting "$root/20260109T000000Z-integration" 2>/dev/null
expected="20260101T000000Z-quality 20260104T000000Z-verify 20260105T000000Z-verify.txt notes "
[ "$(remaining)" = "$expected" ] || fail "keep 1 for a starting integration run left: $(remaining)"

# --keep 0 keeps everything.
reset
before=$(remaining)
python3 "$repo_root/scripts/proof.py" prune --root "$root" --keep 0 --starting "$root/20260109T000000Z-verify"
[ "$(remaining)" = "$before" ] || fail "keep 0 removed bundles: $(remaining)"

# A missing root is not an error.
python3 "$repo_root/scripts/proof.py" prune --root "$scratch/missing" --keep 3 || fail "missing root failed"

echo "test-proof-prune: all cases passed"
