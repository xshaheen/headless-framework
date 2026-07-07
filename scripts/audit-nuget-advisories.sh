#!/bin/bash
set -Eeuo pipefail

SCRIPT_NAME="$(basename "$0")"
DOTNET_CMD="${DOTNET:-dotnet}"
OUTPUT_DIR="artifacts/dependency-audit/nuget-advisories"
TIMEOUT_SECONDS=90
CURRENT_PID=""
PROJECTS=()

usage() {
    cat <<'USAGE'
Usage: audit-nuget-advisories.sh [options]

Scans source package projects for NuGet vulnerability advisories with one
bounded dotnet-list invocation per project.

Options:
  --output-dir <path>       Directory for summary and per-project logs.
                            Default: artifacts/dependency-audit/nuget-advisories
  --timeout-seconds <n>     Per-project timeout in seconds. Default: 90
  --project <path>          Project to scan. May be repeated. Defaults to src/*/*.csproj.
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

safe_log_name() {
    local -r project="$1"
    local name

    name="$(dirname "$project")"
    name="$(basename "$name")"
    printf '%s.log\n' "$name"
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

classify_log() {
    local -r status="$1"
    local -r log_file="$2"

    if [ "$status" -eq 124 ]; then
        printf 'timeout'
        return
    fi

    if [ "$status" -ne 0 ]; then
        printf 'failed'
        return
    fi

    if grep -qi 'has the following vulnerable packages' "$log_file"; then
        printf 'vulnerable'
        return
    fi

    printf 'clean'
}

write_summary_header() {
    local -r summary_file="$1"
    local -r summary_md="$2"

    printf 'project\tstatus\tlog\n' >"$summary_file"
    {
        printf '# NuGet Advisory Audit\n\n'
        printf -- "- Per-project timeout: \`%s\` seconds\n" "$TIMEOUT_SECONDS"
        printf -- "- Project count: \`%s\`\n\n" "${#PROJECTS[@]}"
        printf '| Project | Status | Log |\n'
        printf '| --- | --- | --- |\n'
    } >"$summary_md"
}

append_summary_row() {
    local -r project="$1"
    local -r status="$2"
    local -r log_file="$3"
    local -r summary_file="$4"
    local -r summary_md="$5"

    printf '%s\t%s\t%s\n' "$project" "$status" "$log_file" >>"$summary_file"
    printf "| \`%s\` | \`%s\` | \`%s\` |\n" "$project" "$status" "$log_file" >>"$summary_md"
}

main() {
    parse_args "$@"
    validate_positive_integer "$TIMEOUT_SECONDS" "--timeout-seconds"

    command -v "$DOTNET_CMD" >/dev/null 2>&1 || die "dotnet command not found: $DOTNET_CMD"

    discover_projects
    [ "${#PROJECTS[@]}" -gt 0 ] || die "No source package projects found"

    mkdir -p "$OUTPUT_DIR/logs"

    local -r summary_file="$OUTPUT_DIR/summary.tsv"
    local -r summary_md="$OUTPUT_DIR/summary.md"
    local project
    local log_file
    local status
    local result
    local failures=0

    write_summary_header "$summary_file" "$summary_md"

    for project in "${PROJECTS[@]}"; do
        [ -f "$project" ] || die "Project not found: $project"

        log_file="$OUTPUT_DIR/logs/$(safe_log_name "$project")"
        printf 'Scanning %s\n' "$project"

        if run_with_timeout "$TIMEOUT_SECONDS" "$log_file" "$DOTNET_CMD" list "$project" package --vulnerable --include-transitive --no-restore; then
            status=0
        else
            status="$?"
        fi

        result="$(classify_log "$status" "$log_file")"
        append_summary_row "$project" "$result" "$log_file" "$summary_file" "$summary_md"

        if [ "$result" != "clean" ]; then
            failures=$((failures + 1))
        fi
    done

    printf '\nWrote %s and %s\n' "$summary_file" "$summary_md"

    if [ "$failures" -gt 0 ]; then
        printf '%s: %s project(s) reported vulnerabilities, failed, or timed out.\n' "$SCRIPT_NAME" "$failures" >&2
        return 1
    fi
}

main "$@"
