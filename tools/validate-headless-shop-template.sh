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

PACKAGES_DIR="$TMPDIR/packages"
DOTNET_HOME="$TMPDIR/dotnet-home"
OUTPUT_DIR="$TMPDIR/generated"
LOCAL_PACKAGE_SOURCE="${HEADLESS_SHOP_LOCAL_PACKAGE_SOURCE:-}"

mkdir -p "$PACKAGES_DIR" "$DOTNET_HOME" "$OUTPUT_DIR"

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

step "Generate TrailStore from packed template"
DOTNET_CLI_HOME="$DOTNET_HOME" dotnet new headless-shop -n TrailStore -o "$OUTPUT_DIR"

if [ -n "$LOCAL_PACKAGE_SOURCE" ]; then
    if [ ! -d "$LOCAL_PACKAGE_SOURCE" ]; then
        echo "ERROR: Local Headless package source was not found: $LOCAL_PACKAGE_SOURCE" >&2
        exit 3
    fi

    step "Add local Headless package source"
    dotnet nuget add source "$LOCAL_PACKAGE_SOURCE" --name local-headless --configfile "$OUTPUT_DIR/nuget.config"
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
    "$OUTPUT_DIR/TrailStore.Catalog.Module/CatalogModule.cs" \
    "$OUTPUT_DIR/TrailStore.Contracts" \
    "$OUTPUT_DIR/TrailStore.Tests.Architecture/TrailStore.Tests.Architecture.csproj" \
    "$OUTPUT_DIR/TrailStore.Tests.Integration/TrailStore.Tests.Integration.csproj"; do
    if [ ! -e "$documented_path" ]; then
        echo "ERROR: Generated recipe references missing path: $documented_path" >&2
        exit 3
    fi
done

step "Restore generated solution"
dotnet restore "$OUTPUT_DIR/TrailStore.slnx"

step "Build generated solution"
dotnet build "$OUTPUT_DIR/TrailStore.slnx" --configuration Release --no-restore --no-incremental -v:q -nologo /clp:ErrorsOnly

step "Run generated architecture tests"
dotnet test --project "$OUTPUT_DIR/TrailStore.Tests.Architecture/TrailStore.Tests.Architecture.csproj" \
    --configuration Release \
    --no-build \
    -v:q

step "Run generated integration smoke tests"
dotnet test --project "$OUTPUT_DIR/TrailStore.Tests.Integration/TrailStore.Tests.Integration.csproj" \
    --configuration Release \
    --no-build \
    -v:q

step "Headless shop template validation passed"
