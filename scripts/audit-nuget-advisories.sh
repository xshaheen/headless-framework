#!/bin/bash
set -Eeuo pipefail

SCRIPT_NAME="$(basename "$0")"
DOTNET_CMD="${DOTNET:-dotnet}"
OUTPUT_DIR="artifacts/dependency-audit/nuget-advisories"
TIMEOUT_SECONDS=90
INCLUDE_TRANSITIVE=true
CURRENT_PID=""
PROJECTS=()
SCANS=()

usage() {
    cat <<'USAGE'
Usage: audit-nuget-advisories.sh [options]

Scans source package projects for NuGet package advisories with one bounded
dotnet-list invocation per project and scan type.

Options:
  --output-dir <path>       Directory for summary and per-scan logs.
                            Default: artifacts/dependency-audit/nuget-advisories
  --timeout-seconds <n>     Per-project, per-scan timeout in seconds. Default: 90
  --project <path>          Project to scan. May be repeated. Defaults to src/*/*.csproj.
  --scan <name>             Scan to run: vulnerable or deprecated. May be repeated.
                            Default: vulnerable
  --include-transitive      Include transitive packages for vulnerable scans. Default.
  --no-include-transitive   Check direct packages only for vulnerable scans.
  -h, --help                Show this help text.
USAGE
}

die() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 2
}

cleanup() {
    if [ -n "$CURRENT_PID" ] && kill -0 "$CURRENT_PID" 2>/dev/null; then
        kill -15 "$CURRENT_PID" 2>/dev/null || true
        sleep 1
        if kill -0 "$CURRENT_PID" 2>/dev/null; then
            kill -9 "$CURRENT_PID" 2>/dev/null || true
        fi
    fi
}

trap cleanup EXIT
trap 'cleanup; exit 130' INT TERM

validate_positive_integer() {
    local -r value="$1"
    local -r name="$2"

    case "$value" in
        ''|*[!0-9]*)
            die "$name must be a positive integer"
            ;;
    esac

    if [ "$value" -le 0 ]; then
        die "$name must be greater than zero"
    fi
}

validate_scan() {
    case "$1" in
        vulnerable|deprecated)
            ;;
        *)
            die "--scan must be vulnerable or deprecated"
            ;;
    esac
}

parse_args() {
    while [ "$#" -gt 0 ]; do
        case "$1" in
            --output-dir)
                [ "$#" -ge 2 ] || die "--output-dir requires a value"
                OUTPUT_DIR="$2"
                shift 2
                ;;
            --timeout-seconds)
                [ "$#" -ge 2 ] || die "--timeout-seconds requires a value"
                TIMEOUT_SECONDS="$2"
                shift 2
                ;;
            --project)
                [ "$#" -ge 2 ] || die "--project requires a value"
                PROJECTS+=("$2")
                shift 2
                ;;
            --scan)
                [ "$#" -ge 2 ] || die "--scan requires a value"
                validate_scan "$2"
                SCANS+=("$2")
                shift 2
                ;;
            --include-transitive)
                INCLUDE_TRANSITIVE=true
                shift
                ;;
            --no-include-transitive)
                INCLUDE_TRANSITIVE=false
                shift
                ;;
            -h|--help)
                usage
                exit 0
                ;;
            *)
                die "Unknown argument: $1"
                ;;
        esac
    done
}

discover_projects() {
    local project

    if [ "${#PROJECTS[@]}" -gt 0 ]; then
        return
    fi

    while IFS= read -r project; do
        PROJECTS+=("$project")
    done < <(find src -mindepth 2 -maxdepth 2 -name '*.csproj' -type f | sort)
}

ensure_default_scans() {
    if [ "${#SCANS[@]}" -gt 0 ]; then
        return
    fi

    SCANS+=(vulnerable)
}

safe_log_name() {
    local -r project="$1"
    local -r scan="$2"
    local name

    name="$(dirname "$project")"
    name="$(basename "$name")"
    printf '%s.%s.log\n' "$name" "$scan"
}

run_with_timeout() {
    local -r timeout_seconds="$1"
    local -r log_file="$2"
    shift 2

    "$@" >"$log_file" 2>&1 &
    CURRENT_PID="$!"

    local elapsed=0
    while kill -0 "$CURRENT_PID" 2>/dev/null; do
        if [ "$elapsed" -ge "$timeout_seconds" ]; then
            printf 'Command timed out after %ss.\n' "$timeout_seconds" >>"$log_file"
            kill -15 "$CURRENT_PID" 2>/dev/null || true
            sleep 1
            if kill -0 "$CURRENT_PID" 2>/dev/null; then
                kill -9 "$CURRENT_PID" 2>/dev/null || true
            fi
            wait "$CURRENT_PID" 2>/dev/null || true
            CURRENT_PID=""
            return 124
        fi

        sleep 1
        elapsed=$((elapsed + 1))
    done

    local status=0
    wait "$CURRENT_PID" || status="$?"
    CURRENT_PID=""
    return "$status"
}

build_scan_command() {
    local -r project="$1"
    local -r scan="$2"

    case "$scan" in
        vulnerable)
            SCAN_COMMAND=("$DOTNET_CMD" list "$project" package --vulnerable --no-restore --verbosity quiet)
            if [ "$INCLUDE_TRANSITIVE" = true ]; then
                SCAN_COMMAND+=(--include-transitive)
            fi
            ;;
        deprecated)
            SCAN_COMMAND=("$DOTNET_CMD" list "$project" package --deprecated --no-restore --verbosity quiet)
            ;;
        *)
            die "Unsupported scan: $scan"
            ;;
    esac
}

classify_log() {
    local -r scan="$1"
    local -r status="$2"
    local -r log_file="$3"

    if [ "$status" -eq 124 ]; then
        printf 'timeout'
        return
    fi

    if [ "$status" -ne 0 ]; then
        printf 'failed'
        return
    fi

    case "$scan" in
        vulnerable)
            if grep -qi 'has the following vulnerable packages' "$log_file"; then
                printf 'vulnerable'
                return
            fi
            ;;
        deprecated)
            if grep -qi 'has the following deprecated packages' "$log_file"; then
                printf 'deprecated'
                return
            fi
            ;;
        *)
            die "Unsupported scan: $scan"
            ;;
    esac

    printf 'clean'
}

write_summary_header() {
    local -r summary_file="$1"
    local -r summary_md="$2"

    printf 'project\tscan\tstatus\tlog\n' >"$summary_file"
    {
        printf '# NuGet Advisory Audit\n\n'
        printf -- "- Per-project, per-scan timeout: \`%s\` seconds\n" "$TIMEOUT_SECONDS"
        printf -- "- Include transitive vulnerable packages: \`%s\`\n" "$INCLUDE_TRANSITIVE"
        printf -- "- Project count: \`%s\`\n" "${#PROJECTS[@]}"
        printf -- "- Scans: \`%s\`\n\n" "${SCANS[*]}"
        printf '| Project | Scan | Status | Log |\n'
        printf '| --- | --- | --- | --- |\n'
    } >"$summary_md"
}

append_summary_row() {
    local -r project="$1"
    local -r scan="$2"
    local -r status="$3"
    local -r log_file="$4"
    local -r summary_file="$5"
    local -r summary_md="$6"

    printf '%s\t%s\t%s\t%s\n' "$project" "$scan" "$status" "$log_file" >>"$summary_file"
    printf "| \`%s\` | \`%s\` | \`%s\` | \`%s\` |\n" "$project" "$scan" "$status" "$log_file" >>"$summary_md"
}

main() {
    parse_args "$@"
    validate_positive_integer "$TIMEOUT_SECONDS" "--timeout-seconds"

    command -v "$DOTNET_CMD" >/dev/null 2>&1 || die "dotnet command not found: $DOTNET_CMD"

    discover_projects
    ensure_default_scans
    [ "${#PROJECTS[@]}" -gt 0 ] || die "No source package projects found"

    mkdir -p "$OUTPUT_DIR/logs"

    local -r summary_file="$OUTPUT_DIR/summary.tsv"
    local -r summary_md="$OUTPUT_DIR/summary.md"
    local project
    local scan
    local log_file
    local status
    local result
    local failures=0
    local SCAN_COMMAND=()

    write_summary_header "$summary_file" "$summary_md"

    for project in "${PROJECTS[@]}"; do
        [ -f "$project" ] || die "Project not found: $project"

        for scan in "${SCANS[@]}"; do
            log_file="$OUTPUT_DIR/logs/$(safe_log_name "$project" "$scan")"
            printf 'Scanning %s (%s)\n' "$project" "$scan"

            build_scan_command "$project" "$scan"

            if run_with_timeout "$TIMEOUT_SECONDS" "$log_file" "${SCAN_COMMAND[@]}"; then
                status=0
            else
                status="$?"
            fi

            result="$(classify_log "$scan" "$status" "$log_file")"
            append_summary_row "$project" "$scan" "$result" "$log_file" "$summary_file" "$summary_md"

            if [ "$result" != "clean" ]; then
                failures=$((failures + 1))
            fi
        done
    done

    printf '\nWrote %s and %s\n' "$summary_file" "$summary_md"

    if [ "$failures" -gt 0 ]; then
        printf '%s: %s scan(s) reported advisories, failed, or timed out.\n' "$SCRIPT_NAME" "$failures" >&2
        return 1
    fi
}

main "$@"
