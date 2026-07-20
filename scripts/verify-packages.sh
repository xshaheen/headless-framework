#!/bin/bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd -- "$SCRIPT_DIR/.." && pwd -P)"
MANIFEST="$REPO_ROOT/eng/expected-packages.txt"
PROJECTS_ROOT="$REPO_ROOT/src"
PACKAGES_DIR="$REPO_ROOT/artifacts/packages-results"
EXPECTED_VERSION="${EXPECTED_PACKAGE_VERSION:-}"
EXPECTED_REPOSITORY_URL="${EXPECTED_REPOSITORY_URL:-https://github.com/xshaheen/headless-framework.git}"
EXPECTED_REPOSITORY_COMMIT="${EXPECTED_REPOSITORY_COMMIT:-${GITHUB_SHA:-}}"
MODE="full"
NUGET_PREFLIGHT=false
TEMP_DIR=""
TEMP_BASE=""

usage() {
    cat <<'USAGE'
Usage: verify-packages.sh [options]

Options:
  --manifest PATH             Canonical package-ID manifest.
  --projects-root PATH        Root containing packable source projects.
  --packages-dir PATH         Directory containing .nupkg files.
  --expected-version VERSION  Expected NuGet package and SBOM root version.
  --repository-url URL        Expected nuspec repository URL.
  --repository-commit SHA     Expected nuspec repository commit.
  --projects-only             Verify manifest against evaluated projects only.
  --packages-only             Verify package archives only.
  --preflight-nuget           Also fail if any ID/version already exists on NuGet.org.
  -h, --help                  Show this help.
USAGE
}

die() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 3
}

require_value() {
    local -r option="$1"
    local -r count="$2"

    if [[ "$count" -lt 2 ]]; then
        printf 'ERROR: %s requires a value.\n' "$option" >&2
        usage >&2
        exit 2
    fi
}

require_command() {
    local -r command_name="$1"
    command -v "$command_name" >/dev/null 2>&1 || {
        printf 'ERROR: Required command not found: %s\n' "$command_name" >&2
        exit 4
    }
}

cleanup() {
    if [[ -n "$TEMP_DIR" && -d "$TEMP_DIR" && "$TEMP_DIR" == "$TEMP_BASE/"* ]]; then
        rm -rf -- "$TEMP_DIR"
    fi
}

trap cleanup EXIT
trap 'printf "ERROR: Package verification failed at line %s.\n" "$LINENO" >&2' ERR

while [[ "$#" -gt 0 ]]; do
    case "$1" in
        --manifest)
            require_value "$1" "$#"
            MANIFEST="$2"
            shift 2
            ;;
        --projects-root)
            require_value "$1" "$#"
            PROJECTS_ROOT="$2"
            shift 2
            ;;
        --packages-dir)
            require_value "$1" "$#"
            PACKAGES_DIR="$2"
            shift 2
            ;;
        --expected-version)
            require_value "$1" "$#"
            EXPECTED_VERSION="$2"
            shift 2
            ;;
        --repository-url)
            require_value "$1" "$#"
            EXPECTED_REPOSITORY_URL="$2"
            shift 2
            ;;
        --repository-commit)
            require_value "$1" "$#"
            EXPECTED_REPOSITORY_COMMIT="$2"
            shift 2
            ;;
        --projects-only)
            MODE="projects"
            shift
            ;;
        --packages-only)
            MODE="packages"
            shift
            ;;
        --preflight-nuget)
            NUGET_PREFLIGHT=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            printf 'ERROR: Unknown option: %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

[[ -f "$MANIFEST" ]] || die "Package manifest not found: $MANIFEST"

require_command awk
require_command cmp
require_command find
require_command sort
require_command uniq

TEMP_BASE="${TMPDIR:-/tmp}"
TEMP_BASE="${TEMP_BASE%/}"
TEMP_DIR="$(mktemp -d "$TEMP_BASE/headless-package-verification.XXXXXX")"
MANIFEST_IDS="$TEMP_DIR/manifest-ids.txt"
SORTED_MANIFEST_IDS="$TEMP_DIR/manifest-ids.sorted.txt"

awk '
    NF > 0 && $1 !~ /^#/ {
        if (NF != 1) {
            exit 2
        }
        print $1
    }
' "$MANIFEST" > "$MANIFEST_IDS" || die "Package manifest must contain exactly one package ID per non-comment line"
[[ -s "$MANIFEST_IDS" ]] || die "Package manifest contains no package IDs: $MANIFEST"
LC_ALL=C sort -u "$MANIFEST_IDS" > "$SORTED_MANIFEST_IDS"
cmp -s "$MANIFEST_IDS" "$SORTED_MANIFEST_IDS" || die "Package manifest must contain sorted, unique IDs"

while IFS= read -r package_id; do
    [[ "$package_id" == Headless.* ]] || die "Package ID must use the Headless.* prefix: $package_id"
done < "$MANIFEST_IDS"

verify_project_manifest() {
    local project
    local project_index=0
    local properties_file
    local is_packable
    local package_id
    local -r evaluated_ids="$TEMP_DIR/evaluated-project-package-ids.txt"
    local -r evaluated_ids_sorted="$TEMP_DIR/evaluated-project-package-ids.sorted.txt"

    require_command dotnet
    [[ -d "$PROJECTS_ROOT" ]] || die "Projects root not found: $PROJECTS_ROOT"

    : > "$evaluated_ids"
    while IFS= read -r project; do
        project_index=$((project_index + 1))
        properties_file="$TEMP_DIR/project-$project_index.json"
        dotnet msbuild "$project" -nologo \
            -getProperty:IsPackable \
            -getProperty:PackageId > "$properties_file"

        is_packable="$(jq -r '.Properties.IsPackable // ""' "$properties_file")"
        if [[ "$is_packable" != "true" ]]; then
            continue
        fi

        package_id="$(jq -r '.Properties.PackageId // ""' "$properties_file")"
        [[ -n "$package_id" ]] || die "Packable project has no evaluated PackageId: $project"
        printf '%s\n' "$package_id" >> "$evaluated_ids"
    done < <(find "$PROJECTS_ROOT" -mindepth 2 -maxdepth 2 -type f -name '*.csproj' -print | LC_ALL=C sort)

    LC_ALL=C sort "$evaluated_ids" > "$evaluated_ids_sorted"
    [[ "$(uniq -d "$evaluated_ids_sorted" | wc -l | tr -d ' ')" -eq 0 ]] || die "Evaluated packable projects contain duplicate package IDs"
    cmp -s "$MANIFEST_IDS" "$evaluated_ids_sorted" || {
        diff -u "$MANIFEST_IDS" "$evaluated_ids_sorted" >&2 || true
        die "Canonical package manifest does not match evaluated packable src projects"
    }

    printf 'Verified %s canonical package IDs against evaluated packable projects.\n' "$(wc -l < "$evaluated_ids" | tr -d ' ')"
}

read_nuspec_metadata() {
    local -r nuspec_file="$1"

    python3 - "$nuspec_file" <<'PY'
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
metadata = next((item for item in root.iter() if item.tag.rsplit("}", 1)[-1] == "metadata"), None)
if metadata is None:
    raise SystemExit("nuspec metadata element is missing")

def child_text(name: str) -> str:
    element = next((item for item in metadata if item.tag.rsplit("}", 1)[-1] == name), None)
    return "" if element is None or element.text is None else element.text.strip()

repository = next((item for item in metadata if item.tag.rsplit("}", 1)[-1] == "repository"), None)
print(child_text("id"))
print(child_text("version"))
print("" if repository is None else repository.attrib.get("url", "").strip())
print("" if repository is None else repository.attrib.get("commit", "").strip())
PY
}

verify_sbom() {
    local -r sbom_file="$1"
    local -r package_id="$2"
    local -r package_version="$3"

    jq -e \
        --arg package_id "$package_id" \
        --arg package_version "$package_version" '
            .spdxVersion == "SPDX-2.2"
            and .SPDXID == "SPDXRef-DOCUMENT"
            and .dataLicense == "CC0-1.0"
            and (.documentNamespace | type == "string" and length > 0)
            and (.creationInfo.creators | type == "array" and length > 0)
            and (.packages | type == "array")
            and (.relationships | type == "array")
            and ([
                .packages[]
                | select(
                    .SPDXID == "SPDXRef-RootPackage"
                    and .name == $package_id
                    and .versionInfo == $package_version
                )
            ] | length == 1)
        ' "$sbom_file" >/dev/null || die "Invalid or mismatched SPDX SBOM for $package_id $package_version"
}

verify_package_archives() {
    local package
    local package_index=0
    local entries_file
    local nuspec_entry
    local nuspec_count
    local sbom_count
    local nuspec_file
    local sbom_file
    local metadata_file
    local package_id
    local package_version
    local repository_url
    local repository_commit
    local -r actual_ids="$TEMP_DIR/actual-package-ids.txt"
    local -r actual_ids_sorted="$TEMP_DIR/actual-package-ids.sorted.txt"
    local -r actual_identities="$TEMP_DIR/actual-package-identities.txt"
    local -r duplicate_identities="$TEMP_DIR/duplicate-package-identities.txt"
    local packages=()

    require_command jq
    require_command python3
    require_command unzip

    [[ -d "$PACKAGES_DIR" ]] || die "Packages directory not found: $PACKAGES_DIR"
    [[ -n "$EXPECTED_VERSION" ]] || die "--expected-version is required when verifying package archives"
    [[ -n "$EXPECTED_REPOSITORY_COMMIT" ]] || die "--repository-commit or GITHUB_SHA is required"
    [[ "$EXPECTED_REPOSITORY_COMMIT" =~ ^[0-9a-fA-F]{40,64}$ ]] || die "Expected repository commit is not a full Git SHA"

    while IFS= read -r -d '' package; do
        packages+=("$package")
    done < <(find "$PACKAGES_DIR" -maxdepth 1 -type f -name '*.nupkg' -print0)
    [[ "${#packages[@]}" -gt 0 ]] || die "No .nupkg files found in $PACKAGES_DIR"

    : > "$actual_ids"
    : > "$actual_identities"

    for package in "${packages[@]}"; do
        package_index=$((package_index + 1))
        unzip -tqq "$package" >/dev/null || die "Corrupt NuGet archive: $package"

        entries_file="$TEMP_DIR/package-$package_index.entries.txt"
        unzip -Z1 "$package" > "$entries_file"

        nuspec_count="$(awk '/\.nuspec$/ { count++ } END { print count + 0 }' "$entries_file")"
        [[ "$nuspec_count" -eq 1 ]] || die "Package must contain exactly one .nuspec: $package"
        nuspec_entry="$(awk '/\.nuspec$/ { print }' "$entries_file")"

        sbom_count="$(awk '$0 == "_manifest/spdx_2.2/manifest.spdx.json" { count++ } END { print count + 0 }' "$entries_file")"
        [[ "$sbom_count" -eq 1 ]] || die "Package must contain one embedded SPDX 2.2 manifest: $package"

        nuspec_file="$TEMP_DIR/package-$package_index.nuspec"
        sbom_file="$TEMP_DIR/package-$package_index.spdx.json"
        metadata_file="$TEMP_DIR/package-$package_index.metadata.txt"
        unzip -p "$package" "$nuspec_entry" > "$nuspec_file"
        unzip -p "$package" '_manifest/spdx_2.2/manifest.spdx.json' > "$sbom_file"
        read_nuspec_metadata "$nuspec_file" > "$metadata_file"

        package_id="$(sed -n '1p' "$metadata_file")"
        package_version="$(sed -n '2p' "$metadata_file")"
        repository_url="$(sed -n '3p' "$metadata_file")"
        repository_commit="$(sed -n '4p' "$metadata_file")"

        [[ "$package_id" == Headless.* ]] || die "Package ID must use the Headless.* prefix: $package_id"
        [[ "$package_version" == "$EXPECTED_VERSION" ]] || die "Unexpected version for $package_id: $package_version"
        [[ "$repository_url" == "$EXPECTED_REPOSITORY_URL" ]] || die "Unexpected repository URL for $package_id"
        [[ "$repository_commit" == "$EXPECTED_REPOSITORY_COMMIT" ]] || die "Unexpected repository commit for $package_id"

        verify_sbom "$sbom_file" "$package_id" "$package_version"

        printf '%s\n' "$package_id" >> "$actual_ids"
        printf '%s\t%s\n' "$package_id" "$package_version" >> "$actual_identities"
    done

    LC_ALL=C sort "$actual_ids" > "$actual_ids_sorted"
    LC_ALL=C sort "$actual_identities" | uniq -d > "$duplicate_identities"
    [[ ! -s "$duplicate_identities" ]] || {
        printf 'Duplicate package identities:\n' >&2
        cat "$duplicate_identities" >&2
        die "Duplicate package identity/version detected"
    }

    cmp -s "$MANIFEST_IDS" "$actual_ids_sorted" || {
        diff -u "$MANIFEST_IDS" "$actual_ids_sorted" >&2 || true
        die "Produced package IDs do not exactly match the canonical manifest"
    }

    printf 'Verified %s immutable package archive(s) at version %s.\n' "${#packages[@]}" "$EXPECTED_VERSION"
}

preflight_nuget_org() {
    local package_id
    local package_version
    local normalized_version
    local lowercase_id
    local response_file
    local http_code
    local preflight_index=0
    local -r identities_file="$TEMP_DIR/actual-package-identities.txt"

    require_command curl
    require_command jq
    [[ -s "$identities_file" ]] || die "Package identities are unavailable for NuGet.org preflight"

    while IFS=$'\t' read -r package_id package_version; do
        preflight_index=$((preflight_index + 1))
        normalized_version="${package_version%%+*}"
        normalized_version="$(printf '%s' "$normalized_version" | tr '[:upper:]' '[:lower:]')"
        lowercase_id="$(printf '%s' "$package_id" | tr '[:upper:]' '[:lower:]')"
        response_file="$TEMP_DIR/nuget-$preflight_index.json"

        http_code="$(curl --silent --show-error \
            --proto '=https' \
            --tlsv1.2 \
            --connect-timeout 10 \
            --max-time 30 \
            --retry 2 \
            --retry-all-errors \
            --retry-max-time 60 \
            --output "$response_file" \
            --write-out '%{http_code}' \
            "https://api.nuget.org/v3-flatcontainer/$lowercase_id/index.json")" || die "NuGet.org preflight request failed for $package_id"

        case "$http_code" in
            200)
                if jq -e --arg version "$normalized_version" \
                    'any(.versions[]?; ascii_downcase == $version)' "$response_file" >/dev/null; then
                    die "NuGet.org already contains $package_id $package_version"
                fi
                ;;
            404)
                ;;
            *)
                die "NuGet.org preflight returned HTTP $http_code for $package_id"
                ;;
        esac
    done < "$identities_file"

    printf 'NuGet.org preflight confirmed every expected ID/version is unpublished.\n'
}

require_command jq

if [[ "$MODE" != "packages" ]]; then
    verify_project_manifest
fi

if [[ "$MODE" != "projects" ]]; then
    verify_package_archives
fi

if [[ "$NUGET_PREFLIGHT" == true ]]; then
    [[ "$MODE" != "projects" ]] || die "--preflight-nuget cannot be used with --projects-only"
    preflight_nuget_org
fi
