#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises `scripts/proof.py analyzers`: findings come from a project's compiler log when the log is as new as its
# assembly, suppressed, hidden, compiler (CS*) and below-severity results are dropped, and a project whose log is
# missing or older than its assembly runs the fallback command instead.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT

# proof.py resolves the repository from its own path, so the scratch repository carries a copy of it.
mkdir -p "$scratch/scripts" "$scratch/src/Fresh/obj/Release/net10.0" "$scratch/src/Stale/obj/Release/net10.0" "$scratch/src/Missing" "$scratch/run"
cp "$repo_root/scripts/proof.py" "$scratch/scripts/proof.py"
cd "$scratch"
touch src/Fresh/Fresh.csproj src/Stale/Stale.csproj src/Missing/Missing.csproj

sarif() {
  cat <<'JSON'
{"version": "2.1.0", "runs": [{"results": [
  {"ruleId": "CA1822", "level": "warning", "message": {"text": "Mark as static"}, "locations": [{"physicalLocation": {"artifactLocation": {"uri": "file:///repo/src/Fresh/A.cs"}, "region": {"startLine": 3, "startColumn": 5}}}]},
  {"ruleId": "MA0020", "level": "note", "message": {"text": "Use Count"}, "locations": [{"physicalLocation": {"artifactLocation": {"uri": "file:///repo/src/Fresh/A.cs"}, "region": {"startLine": 7, "startColumn": 9}}}]},
  {"ruleId": "IDE0051", "level": "none", "message": {"text": "Hidden"}, "locations": [{"physicalLocation": {"artifactLocation": {"uri": "file:///repo/src/Fresh/A.cs"}, "region": {"startLine": 8, "startColumn": 1}}}]},
  {"ruleId": "CA2000", "level": "warning", "suppressions": [{"kind": "inSource"}], "message": {"text": "Suppressed"}, "locations": [{"physicalLocation": {"artifactLocation": {"uri": "file:///repo/src/Fresh/A.cs"}, "region": {"startLine": 9, "startColumn": 1}}}]},
  {"ruleId": "CS0169", "level": "warning", "message": {"text": "Compiler"}, "locations": [{"physicalLocation": {"artifactLocation": {"uri": "file:///repo/src/Fresh/A.cs"}, "region": {"startLine": 2, "startColumn": 1}}}]}
]}]}
JSON
}
sarif > src/Fresh/obj/Release/net10.0/analyzers.sarif
touch src/Fresh/obj/Release/net10.0/Fresh.dll
sarif > src/Stale/obj/Release/net10.0/analyzers.sarif
touch -t 202601010000 src/Stale/obj/Release/net10.0/analyzers.sarif
touch src/Stale/obj/Release/net10.0/Stale.dll

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }
ids() { python3 -c 'import json,sys; print(" ".join(sorted(c["DiagnosticId"] for d in json.load(open(sys.argv[1])) for c in d["FileChanges"])))' "$1"; }

# A fallback that records which projects it was asked to analyze and finds nothing.
cat > fallback.sh <<'SH'
#!/usr/bin/env bash
project="$1"
printf '%s\n' "${project##*/}" >> "$(dirname "$0")/fallback.txt"
SH
chmod +x fallback.sh

printf 'src/Fresh/Fresh.csproj\nsrc/Stale/Stale.csproj\nsrc/Missing/Missing.csproj\n' > run/changed.txt
rc=0
python3 scripts/proof.py analyzers --dir run --configuration Release --severity hidden -- "$scratch/fallback.sh" 2>/dev/null || rc=$?
[ "$rc" -eq 2 ] || fail "findings should exit 2, got $rc"
[ "$(ids run/analyzers/build/format-report.json)" = "CA1822 MA0020" ] || fail "unexpected findings: $(ids run/analyzers/build/format-report.json)"
[ "$(sort fallback.txt | tr '\n' ' ')" = "Missing.csproj Stale.csproj " ] || fail "fallback ran for: $(tr '\n' ' ' < fallback.txt)"
echo 'PASS: fresh log read, stale and missing logs fall back'

printf 'src/Fresh/Fresh.csproj\n' > run/changed.txt
rc=0
python3 scripts/proof.py analyzers --dir run --configuration Release --severity warn -- "$scratch/fallback.sh" 2>/dev/null || rc=$?
[ "$(ids run/analyzers/build/format-report.json)" = "CA1822" ] || fail "--severity warn should drop info: $(ids run/analyzers/build/format-report.json)"
rc=0
python3 scripts/proof.py analyzers --dir run --configuration Release --severity hidden --diagnostics MA0020 -- "$scratch/fallback.sh" 2>/dev/null || rc=$?
[ "$(ids run/analyzers/build/format-report.json)" = "MA0020" ] || fail "--diagnostics should filter: $(ids run/analyzers/build/format-report.json)"
echo 'PASS: severity and diagnostics filters'

rm src/Fresh/obj/Release/net10.0/analyzers.sarif
sarif | python3 -c 'import json,sys; d=json.load(sys.stdin); d["runs"][0]["results"]=[]; print(json.dumps(d))' > src/Fresh/obj/Release/net10.0/analyzers.sarif
touch src/Fresh/obj/Release/net10.0/Fresh.dll
python3 scripts/proof.py analyzers --dir run --configuration Release --severity hidden -- "$scratch/fallback.sh" 2>/dev/null || fail "a clean log should exit 0"
echo 'PASS: clean log passes'
