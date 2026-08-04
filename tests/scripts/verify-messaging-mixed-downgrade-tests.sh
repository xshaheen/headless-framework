#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly SCRIPT_DIR
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
readonly REPO_ROOT
readonly VERIFIER="$REPO_ROOT/scripts/verify-messaging-mixed-downgrade.sh"
readonly OLD_VERSION="0.11.0"
readonly NEW_VERSION="0.11.1-preview.0.135"
TEMP_DIR=""

cleanup() {
    if [[ -n "$TEMP_DIR" && -d "$TEMP_DIR" && "$TEMP_DIR" == "${TMPDIR:-/tmp}/"* ]]; then
        rm -rf -- "$TEMP_DIR"
    fi
}

trap cleanup EXIT
trap 'printf "ERROR: Mixed-downgrade verifier tests failed at line %s.\n" "$LINENO" >&2' ERR

TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/headless-mixed-downgrade-tests.XXXXXX")"

write_expected_diagnostic() {
    local -r destination="$1"

    cat > "$destination" <<EOF
/tmp/SelectedMixed.csproj : error NU1605: Warning As Error: Detected package downgrade: Headless.Messaging.Core from $NEW_VERSION to $OLD_VERSION. Reference the package directly from the project to select a different version.
/tmp/SelectedMixed.csproj : error NU1605:  SelectedMixed -> Headless.Messaging.Redis $NEW_VERSION -> Headless.Messaging.Core (>= $NEW_VERSION)
/tmp/SelectedMixed.csproj : error NU1605:  SelectedMixed -> Headless.Messaging.Core (>= $OLD_VERSION)
EOF
}

expect_failure() {
    local -r name="$1"
    shift

    if "$@" >"$TEMP_DIR/$name.stdout" 2>"$TEMP_DIR/$name.stderr"; then
        printf 'ERROR: Expected failure: %s\n' "$name" >&2
        exit 1
    fi
}

VALID_LOG="$TEMP_DIR/valid.log"
write_expected_diagnostic "$VALID_LOG"
"$VERIFIER" "$VALID_LOG" "$OLD_VERSION" "$NEW_VERSION"

UNRELATED_LOG="$TEMP_DIR/unrelated.log"
cat > "$UNRELATED_LOG" <<'EOF'
/tmp/SelectedMixed.csproj : error NU1605: Warning As Error: Detected package downgrade: Unrelated.Package from 2.0.0 to 1.0.0.
/tmp/SelectedMixed.csproj : error NU1605:  SelectedMixed -> Unrelated.Dependency 2.0.0 -> Unrelated.Package (>= 2.0.0)
/tmp/SelectedMixed.csproj : error NU1605:  SelectedMixed -> Unrelated.Package (>= 1.0.0)
EOF
expect_failure unrelated "$VERIFIER" "$UNRELATED_LOG" "$OLD_VERSION" "$NEW_VERSION"

WRONG_NEW_VERSION_LOG="$TEMP_DIR/wrong-new-version.log"
write_expected_diagnostic "$WRONG_NEW_VERSION_LOG"
expect_failure wrong-new-version "$VERIFIER" "$WRONG_NEW_VERSION_LOG" "$OLD_VERSION" "0.11.1-preview.0.999"

WRONG_DIRECT_VERSION_LOG="$TEMP_DIR/wrong-direct-version.log"
sed "s/Headless.Messaging.Core (>= $OLD_VERSION)/Headless.Messaging.Core (>= 0.10.0)/" \
    "$VALID_LOG" > "$WRONG_DIRECT_VERSION_LOG"
expect_failure wrong-direct-version "$VERIFIER" "$WRONG_DIRECT_VERSION_LOG" "$OLD_VERSION" "$NEW_VERSION"

printf 'All mixed-downgrade verifier fixtures passed.\n'
