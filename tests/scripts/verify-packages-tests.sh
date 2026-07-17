#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/../.." && pwd -P)"
VERIFIER="$REPO_ROOT/scripts/verify-packages.sh"
EXPECTED_VERSION="1.2.3"
EXPECTED_COMMIT="0123456789abcdef0123456789abcdef01234567"
EXPECTED_URL="https://github.com/xshaheen/headless-framework.git"
TEMP_DIR=""
TEMP_BASE=""

cleanup() {
    if [[ -n "$TEMP_DIR" && -d "$TEMP_DIR" && "$TEMP_DIR" == "$TEMP_BASE/"* ]]; then
        rm -rf -- "$TEMP_DIR"
    fi
}

trap cleanup EXIT
trap 'printf "ERROR: Package verifier tests failed at line %s.\n" "$LINENO" >&2' ERR

TEMP_BASE="${TMPDIR:-/tmp}"
TEMP_BASE="${TEMP_BASE%/}"
TEMP_DIR="$(mktemp -d "$TEMP_BASE/headless-package-tests.XXXXXX")"
MANIFEST="$TEMP_DIR/expected-packages.txt"
printf '%s\n' 'Headless.One' 'Headless.Two' > "$MANIFEST"

create_package() {
    local -r destination="$1"
    local -r package_id="$2"
    local -r package_version="$3"
    local -r repository_commit="$4"
    local -r sbom_package_id="$5"
    local -r sbom_package_version="$6"
    local -r include_sbom="${7:-true}"

    python3 - \
        "$destination" \
        "$package_id" \
        "$package_version" \
        "$repository_commit" \
        "$sbom_package_id" \
        "$sbom_package_version" \
        "$EXPECTED_URL" \
        "$include_sbom" <<'PY'
import json
import sys
import zipfile

destination, package_id, version, commit, sbom_id, sbom_version, repository_url, include_sbom = sys.argv[1:]
nuspec = f'''<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>{package_id}</id>
    <version>{version}</version>
    <repository type="git" url="{repository_url}" commit="{commit}" />
  </metadata>
</package>
'''
sbom = {
    "spdxVersion": "SPDX-2.2",
    "dataLicense": "CC0-1.0",
    "SPDXID": "SPDXRef-DOCUMENT",
    "name": f"{sbom_id} {sbom_version}",
    "documentNamespace": f"https://spdx.example/{sbom_id}/{sbom_version}",
    "creationInfo": {"creators": ["Tool: verifier-test"]},
    "packages": [
        {
            "SPDXID": "SPDXRef-RootPackage",
            "name": sbom_id,
            "versionInfo": sbom_version,
        }
    ],
    "relationships": [],
}

with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    archive.writestr(f"{package_id}.nuspec", nuspec)
    archive.writestr("lib/net10.0/placeholder.txt", "test")
    if include_sbom == "true":
        archive.writestr("_manifest/spdx_2.2/manifest.spdx.json", json.dumps(sbom))
    elif include_sbom == "invalid":
        archive.writestr("_manifest/spdx_2.2/manifest.spdx.json", "{")
PY
}

run_verifier() {
    local -r packages_dir="$1"
    shift

    "$VERIFIER" \
        --packages-only \
        --manifest "$MANIFEST" \
        --packages-dir "$packages_dir" \
        --expected-version "$EXPECTED_VERSION" \
        --repository-url "$EXPECTED_URL" \
        --repository-commit "$EXPECTED_COMMIT" \
        "$@"
}

expect_failure() {
    local -r name="$1"
    shift

    if "$@" >"$TEMP_DIR/$name.stdout" 2>"$TEMP_DIR/$name.stderr"; then
        printf 'ERROR: Expected failure: %s\n' "$name" >&2
        exit 1
    fi
}

VALID_DIR="$TEMP_DIR/valid"
mkdir -p "$VALID_DIR"
create_package "$VALID_DIR/Headless.One.$EXPECTED_VERSION.nupkg" \
    'Headless.One' "$EXPECTED_VERSION" "$EXPECTED_COMMIT" 'Headless.One' "$EXPECTED_VERSION"
create_package "$VALID_DIR/Headless.Two.$EXPECTED_VERSION.nupkg" \
    'Headless.Two' "$EXPECTED_VERSION" "$EXPECTED_COMMIT" 'Headless.Two' "$EXPECTED_VERSION"
run_verifier "$VALID_DIR"

MISSING_DIR="$TEMP_DIR/missing"
mkdir -p "$MISSING_DIR"
cp "$VALID_DIR/Headless.One.$EXPECTED_VERSION.nupkg" "$MISSING_DIR/"
expect_failure missing run_verifier "$MISSING_DIR"

EXTRA_MANIFEST="$TEMP_DIR/one-package-manifest.txt"
printf '%s\n' 'Headless.One' > "$EXTRA_MANIFEST"
expect_failure extra "$VERIFIER" \
    --packages-only \
    --manifest "$EXTRA_MANIFEST" \
    --packages-dir "$VALID_DIR" \
    --expected-version "$EXPECTED_VERSION" \
    --repository-url "$EXPECTED_URL" \
    --repository-commit "$EXPECTED_COMMIT"

CORRUPT_DIR="$TEMP_DIR/corrupt"
mkdir -p "$CORRUPT_DIR"
cp "$VALID_DIR/Headless.One.$EXPECTED_VERSION.nupkg" "$CORRUPT_DIR/"
printf 'not a zip archive\n' > "$CORRUPT_DIR/Headless.Two.$EXPECTED_VERSION.nupkg"
expect_failure corrupt run_verifier "$CORRUPT_DIR"

DUPLICATE_DIR="$TEMP_DIR/duplicate"
mkdir -p "$DUPLICATE_DIR"
cp "$VALID_DIR/Headless.One.$EXPECTED_VERSION.nupkg" "$DUPLICATE_DIR/first.nupkg"
cp "$VALID_DIR/Headless.One.$EXPECTED_VERSION.nupkg" "$DUPLICATE_DIR/second.nupkg"
cp "$VALID_DIR/Headless.Two.$EXPECTED_VERSION.nupkg" "$DUPLICATE_DIR/"
expect_failure duplicate run_verifier "$DUPLICATE_DIR"

WRONG_COMMIT_DIR="$TEMP_DIR/wrong-commit"
mkdir -p "$WRONG_COMMIT_DIR"
create_package "$WRONG_COMMIT_DIR/Headless.One.$EXPECTED_VERSION.nupkg" \
    'Headless.One' "$EXPECTED_VERSION" 'abcdefabcdefabcdefabcdefabcdefabcdefabcd' 'Headless.One' "$EXPECTED_VERSION"
cp "$VALID_DIR/Headless.Two.$EXPECTED_VERSION.nupkg" "$WRONG_COMMIT_DIR/"
expect_failure wrong-commit run_verifier "$WRONG_COMMIT_DIR"

WRONG_VERSION_DIR="$TEMP_DIR/wrong-version"
mkdir -p "$WRONG_VERSION_DIR"
create_package "$WRONG_VERSION_DIR/Headless.One.9.9.9.nupkg" \
    'Headless.One' '9.9.9' "$EXPECTED_COMMIT" 'Headless.One' '9.9.9'
cp "$VALID_DIR/Headless.Two.$EXPECTED_VERSION.nupkg" "$WRONG_VERSION_DIR/"
expect_failure wrong-version run_verifier "$WRONG_VERSION_DIR"

MISSING_SBOM_DIR="$TEMP_DIR/missing-sbom"
mkdir -p "$MISSING_SBOM_DIR"
create_package "$MISSING_SBOM_DIR/Headless.One.$EXPECTED_VERSION.nupkg" \
    'Headless.One' "$EXPECTED_VERSION" "$EXPECTED_COMMIT" 'Headless.One' "$EXPECTED_VERSION" false
cp "$VALID_DIR/Headless.Two.$EXPECTED_VERSION.nupkg" "$MISSING_SBOM_DIR/"
expect_failure missing-sbom run_verifier "$MISSING_SBOM_DIR"

INVALID_SBOM_DIR="$TEMP_DIR/invalid-sbom"
mkdir -p "$INVALID_SBOM_DIR"
create_package "$INVALID_SBOM_DIR/Headless.One.$EXPECTED_VERSION.nupkg" \
    'Headless.One' "$EXPECTED_VERSION" "$EXPECTED_COMMIT" 'Headless.One' "$EXPECTED_VERSION" invalid
cp "$VALID_DIR/Headless.Two.$EXPECTED_VERSION.nupkg" "$INVALID_SBOM_DIR/"
expect_failure invalid-sbom run_verifier "$INVALID_SBOM_DIR"

WRONG_SBOM_DIR="$TEMP_DIR/wrong-sbom"
mkdir -p "$WRONG_SBOM_DIR"
create_package "$WRONG_SBOM_DIR/Headless.One.$EXPECTED_VERSION.nupkg" \
    'Headless.One' "$EXPECTED_VERSION" "$EXPECTED_COMMIT" 'Headless.One' '9.9.9'
cp "$VALID_DIR/Headless.Two.$EXPECTED_VERSION.nupkg" "$WRONG_SBOM_DIR/"
expect_failure wrong-sbom run_verifier "$WRONG_SBOM_DIR"

printf 'All package verifier fixtures passed.\n'
