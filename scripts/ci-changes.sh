#!/usr/bin/env bash
set -euo pipefail

# Read the complete Git diff locally, avoiding the Actions/API changed-file limits.
dotnet=false
dashboards=false
tus=false
all_checks() {
  dotnet=true
  dashboards=true
  tus=true
}

case "${GITHUB_EVENT_NAME:-}" in
  pull_request | push)
    changed=$(mktemp)
    trap 'rm -f "$changed"' EXIT
    # A missing base (for example a force push) must run checks, never skip them.
    if [[ -z "${BASE_SHA:-}" ]] || ! git diff --no-renames --name-only -z "$BASE_SHA" "${HEAD_SHA:-HEAD}" -- > "$changed"; then
      all_checks
    else
      while IFS= read -r -d '' path; do
        case "$path" in
          docs/* | .github/ISSUE_TEMPLATE/* | .github/*.md) ;;
          src/Headless.Jobs.Dashboard/* | src/Headless.Messaging.Dashboard/*)
            dotnet=true
            dashboards=true
            ;;
          demo/Headless.Tus.Demo/frontend/*) tus=true ;;
          src/* | tests/* | test-assets/* | demo/*) dotnet=true ;;
          *)
            # Only root Markdown is documentation; unknown build inputs run everything.
            if [[ "$path" == */* || "$path" != *.md ]]; then
              all_checks
            fi
            ;;
        esac
      done < "$changed"
    fi
    ;;
  *) all_checks ;;
esac

printf 'dotnet=%s\ndashboards=%s\ntus=%s\n' "$dotnet" "$dashboards" "$tus"
