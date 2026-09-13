#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR

for format in sha1 sha256; do
  git init -q --object-format="$format" --initial-branch=main "$scratch/$format"
  git init -q --bare --object-format="$format" --initial-branch=main "$scratch/$format.git"
  cd "$scratch/$format"
  git config user.name 'Hook test'
  git config user.email 'hook@example.invalid'
  git config commit.gpgsign false
  git config core.hooksPath /dev/null
  git commit -qm 'Base' --allow-empty
  git remote add origin "$scratch/$format.git"
  git push -q origin HEAD:main HEAD:one HEAD:two HEAD:three
  base=$(git rev-parse HEAD)
  sibling="$scratch/$format-sibling"
  git worktree add -q --detach "$sibling" HEAD
  ln -s "$repo_root/.githooks" .githooks
  make -s -f "$repo_root/Makefile" hooks
  test "$(git config --worktree --get core.hooksPath)" = .githooks
  test "$(git config --local --get core.hooksPath)" = /dev/null
  echo "PASS: $format registration uses worktree config"

  test "$(git -C "$sibling" config --get core.hooksPath)" = /dev/null
  make -s -C "$sibling" -f "$repo_root/Makefile" hooks
  test "$(git -C "$sibling" config --worktree --get core.hooksPath)" = .githooks
  git -C "$sibling" config --worktree core.hooksPath .sibling-hooks
  make -s -f "$repo_root/Makefile" hooks
  test "$(git config --worktree --get-all core.hooksPath)" = .githooks
  test "$(git -C "$sibling" config --get core.hooksPath)" = .sibling-hooks
  echo "PASS: $format repeated registration preserves sibling worktree settings"
  # A rejecting build gate makes accidental skips observable without running .NET.
  printf 'hook-pre-push:\n\t@touch gate-ran\n\t@exit 1\n' > Makefile

  git push -q origin :one
  test ! -e gate-ran
  if git --git-dir="$scratch/$format.git" show-ref --verify --quiet refs/heads/one; then
    echo 'FAIL: single deletion did not reach the remote' >&2
    exit 1
  fi
  echo "PASS: $format single deletion skips build"

  git push -q --atomic origin :two :three
  test ! -e gate-ran
  test "$(git --git-dir="$scratch/$format.git" for-each-ref --format='%(refname)' refs/heads)" = refs/heads/main
  echo "PASS: $format batch deletion skips build"

  git push -q origin main
  test ! -e gate-ran
  echo "PASS: $format up-to-date push skips build"

  git -c core.hooksPath=/dev/null commit -qm 'Update' --allow-empty
  for operation in create update mixed tag; do
    case "$operation" in
      create) refs=(HEAD:new) ;;
      update) refs=(HEAD:main) ;;
      mixed) refs=(:main HEAD:new) ;;
      tag) git tag v1; refs=(refs/tags/v1) ;;
    esac
    if git push -q --atomic origin "${refs[@]}" > push.log 2>&1; then
      echo "FAIL: $operation bypassed the rejecting build gate" >&2
      exit 1
    fi
    test -e gate-ran
    rm gate-ran
    test "$(git --git-dir="$scratch/$format.git" rev-parse refs/heads/main)" = "$base"
    test "$(git --git-dir="$scratch/$format.git" for-each-ref --format='%(refname)')" = refs/heads/main
    echo "PASS: $format $operation retains build gate and preserves remote on failure"
  done
done
