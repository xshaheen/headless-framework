#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
TMPDIR="$(mktemp -d)"
TMPDIR="$(cd -- "$TMPDIR" && pwd -P)"

cleanup() {
    if [ -n "${TMPDIR:-}" ] && [ "$TMPDIR" != "/" ]; then
        rm -rf -- "$TMPDIR"
    fi
}

trap cleanup EXIT
trap 'echo "ERROR: validate-headless-shop-template.sh failed on line $LINENO" >&2' ERR

step() {
    printf '\n==> %s\n' "$1"
}

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "ERROR: Missing required command: $1" >&2
        exit 4
    fi
}

require_command dotnet
require_command docker
require_command perl

PACKAGES_DIR="$TMPDIR/packages"
DOTNET_HOME="$TMPDIR/dotnet-home"
OUTPUT_DIR="$TMPDIR/generated"
GLOBAL_PACKAGES_SOURCE="${NUGET_PACKAGES:-${HOME}/.nuget/packages}"
NUGET_PACKAGES="$TMPDIR/nuget-packages"
LOCAL_PACKAGE_SOURCE="${HEADLESS_SHOP_LOCAL_PACKAGE_SOURCE:-}"
HEADLESS_PACKAGE_VERSION=""

mkdir -p "$PACKAGES_DIR" "$DOTNET_HOME" "$OUTPUT_DIR" "$NUGET_PACKAGES"
export NUGET_PACKAGES

PACKAGE_PATH="${HEADLESS_SHOP_TEMPLATE_PACKAGE_PATH:-}"

if [ -z "$PACKAGE_PATH" ]; then
    step "Pack headless-shop template"
    dotnet pack "$REPO_ROOT/templates/HeadlessShop/HeadlessShop.csproj" \
        --configuration Release \
        --output "$PACKAGES_DIR" \
        -v:q \
        -nologo

    PACKAGE_PATH="$(find "$PACKAGES_DIR" -name 'Headless.Templates.HeadlessShop.*.nupkg' -type f | head -n 1)"
else
    step "Use prebuilt headless-shop template package"
fi

if [ -z "$PACKAGE_PATH" ] || [ ! -f "$PACKAGE_PATH" ]; then
    echo "ERROR: Template package was not found: ${PACKAGE_PATH:-$PACKAGES_DIR}" >&2
    exit 3
fi

step "Install template into isolated CLI home"
DOTNET_CLI_HOME="$DOTNET_HOME" dotnet new install "$PACKAGE_PATH" --force

if [ -n "$LOCAL_PACKAGE_SOURCE" ]; then
    if [ ! -d "$LOCAL_PACKAGE_SOURCE" ]; then
        echo "ERROR: Local Headless package source was not found: $LOCAL_PACKAGE_SOURCE" >&2
        exit 3
    fi

    LOCAL_VALIDATION_SOURCE="$TMPDIR/local-headless-source"
    mkdir -p "$LOCAL_VALIDATION_SOURCE"
    find "$LOCAL_PACKAGE_SOURCE" -maxdepth 1 -name '*.nupkg' ! -name '*.snupkg' -type f \
        -exec cp {} "$LOCAL_VALIDATION_SOURCE" \;

    for sdk_package in headless.net.sdk headless.net.sdk.web headless.net.sdk.test; do
        sdk_path="$GLOBAL_PACKAGES_SOURCE/$sdk_package/0.0.129/$sdk_package.0.0.129.nupkg"
        if [ ! -f "$sdk_path" ]; then
            echo "ERROR: Required SDK package was not found in the global package cache: $sdk_path" >&2
            exit 3
        fi
        cp "$sdk_path" "$LOCAL_VALIDATION_SOURCE"
    done

    LOCAL_PACKAGE_SOURCE="$LOCAL_VALIDATION_SOURCE"
    HEADLESS_CORE_PACKAGE="$(find "$LOCAL_PACKAGE_SOURCE" -name 'Headless.Core.*.nupkg' ! -name '*.snupkg' -type f | head -n 1)"

    if [ -z "$HEADLESS_CORE_PACKAGE" ]; then
        echo "ERROR: Headless.Core package was not found in local source: $LOCAL_PACKAGE_SOURCE" >&2
        exit 3
    fi

    HEADLESS_PACKAGE_VERSION="${HEADLESS_CORE_PACKAGE##*/Headless.Core.}"
    HEADLESS_PACKAGE_VERSION="${HEADLESS_PACKAGE_VERSION%.nupkg}"
fi

step "Generate TrailStore from packed template"
if [ -n "$HEADLESS_PACKAGE_VERSION" ]; then
    DOTNET_CLI_HOME="$DOTNET_HOME" dotnet new headless-shop -n TrailStore -o "$OUTPUT_DIR" \
        --HeadlessPackageVersion "$HEADLESS_PACKAGE_VERSION"
else
    DOTNET_CLI_HOME="$DOTNET_HOME" dotnet new headless-shop -n TrailStore -o "$OUTPUT_DIR"
fi

if [ -n "$LOCAL_PACKAGE_SOURCE" ]; then
    step "Use only locally-built Headless packages"
    dotnet nuget add source "$LOCAL_PACKAGE_SOURCE" --name local-headless --configfile "$OUTPUT_DIR/nuget.config"
    dotnet nuget disable source github.com --configfile "$OUTPUT_DIR/nuget.config"
    perl -0pi -e 's#(<packageSourceMapping>\n)#$1    <packageSource key="local-headless">\n      <package pattern="Headless.*" />\n    </packageSource>\n#' "$OUTPUT_DIR/nuget.config"
fi

step "Validate generated placeholders and docs"
PLACEHOLDER_REPORT="$TMPDIR/placeholder-check.txt"
if find "$OUTPUT_DIR" -type f \( -name '*.cs' -o -name '*.csproj' -o -name '*.md' -o -name '*.json' \) -print0 |
    xargs -0 grep -n 'HeadlessShop' >"$PLACEHOLDER_REPORT" 2>/dev/null; then
    cat "$PLACEHOLDER_REPORT" >&2
    echo "ERROR: Generated output still contains HeadlessShop placeholders." >&2
    exit 3
fi

for required_path in \
    "$OUTPUT_DIR/AGENTS.md" \
    "$OUTPUT_DIR/.config/dotnet-tools.json" \
    "$OUTPUT_DIR/README.md" \
    "$OUTPUT_DIR/compose.yaml" \
    "$OUTPUT_DIR/docs/architecture.md" \
    "$OUTPUT_DIR/docs/validation.md" \
    "$OUTPUT_DIR/docs/recipes/add-command.md" \
    "$OUTPUT_DIR/TrailStore.Tests.Architecture/TrailStore.Tests.Architecture.csproj" \
    "$OUTPUT_DIR/TrailStore.Tests.Integration/TrailStore.Tests.Integration.csproj"; do
    if [ ! -e "$required_path" ]; then
        echo "ERROR: Generated required path missing: $required_path" >&2
        exit 3
    fi
done

step "Validate generated docs and recipe commands"
for expected_text in \
    "dotnet restore TrailStore.slnx" \
    "dotnet test TrailStore.Tests.Architecture/TrailStore.Tests.Architecture.csproj" \
    "dotnet test TrailStore.Tests.Integration/TrailStore.Tests.Integration.csproj"; do
    if ! grep -F "$expected_text" "$OUTPUT_DIR/docs/validation.md" "$OUTPUT_DIR/docs/recipes/add-command.md" >/dev/null; then
        echo "ERROR: Generated docs are missing expected command: $expected_text" >&2
        exit 3
    fi
done

for documented_path in \
    "$OUTPUT_DIR/TrailStore.Catalog.Application" \
    "$OUTPUT_DIR/TrailStore.Catalog.Module/SetupCatalogModule.cs" \
    "$OUTPUT_DIR/TrailStore.Contracts" \
    "$OUTPUT_DIR/TrailStore.Tests.Architecture/TrailStore.Tests.Architecture.csproj" \
    "$OUTPUT_DIR/TrailStore.Tests.Integration/TrailStore.Tests.Integration.csproj"; do
    if [ ! -e "$documented_path" ]; then
        echo "ERROR: Generated recipe references missing path: $documented_path" >&2
        exit 3
    fi
done

step "Restore generated solution"
dotnet tool restore --tool-manifest "$OUTPUT_DIR/.config/dotnet-tools.json"
dotnet restore "$OUTPUT_DIR/TrailStore.slnx"

step "Build generated solution"
dotnet build "$OUTPUT_DIR/TrailStore.slnx" --configuration Release --no-restore --no-incremental -v:q -nologo /clp:ErrorsOnly

step "Run generated architecture tests"
dotnet test "$OUTPUT_DIR/TrailStore.Tests.Architecture/TrailStore.Tests.Architecture.csproj" \
    --configuration Release \
    --no-build \
    -v:q

step "Run generated integration smoke tests"
dotnet test "$OUTPUT_DIR/TrailStore.Tests.Integration/TrailStore.Tests.Integration.csproj" \
    --configuration Release \
    --no-build \
    -v:q

step "Headless shop template validation passed"
