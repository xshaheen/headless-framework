#!/bin/bash
set -Eeuo pipefail

if [[ "$#" -ne 3 ]]; then
    printf 'Usage: %s <restore-log> <old-version> <new-version>\n' "$0" >&2
    exit 2
fi

readonly RESTORE_LOG="$1"
readonly OLD_VERSION="$2"
readonly NEW_VERSION="$3"

if [[ ! -f "$RESTORE_LOG" ]]; then
    printf 'ERROR: Restore log does not exist: %s\n' "$RESTORE_LOG" >&2
    exit 2
fi

if [[ -z "$OLD_VERSION" || -z "$NEW_VERSION" || "$OLD_VERSION" == "$NEW_VERSION" ]]; then
    printf 'ERROR: Expected distinct non-empty old and new Messaging package versions.\n' >&2
    exit 2
fi

require_nu1605_line() {
    local -r expected="$1"

    if ! awk -v expected="$expected" '
        index($0, "NU1605") && index($0, expected) { found = 1 }
        END { exit found ? 0 : 1 }
    ' "$RESTORE_LOG"; then
        printf 'ERROR: SelectedMixed restore did not report the expected NU1605 detail:\n  %s\n' "$expected" >&2
        exit 1
    fi
}

require_nu1605_line \
    "Detected package downgrade: Headless.Messaging.Core from $NEW_VERSION to $OLD_VERSION."
require_nu1605_line \
    "SelectedMixed -> Headless.Messaging.Redis $NEW_VERSION -> Headless.Messaging.Core (>= $NEW_VERSION)"
require_nu1605_line \
    "SelectedMixed -> Headless.Messaging.Core (>= $OLD_VERSION)"

printf 'SelectedMixed reported the expected Headless.Messaging.Core %s <- Headless.Messaging.Redis %s downgrade boundary.\n' \
    "$OLD_VERSION" \
    "$NEW_VERSION"
