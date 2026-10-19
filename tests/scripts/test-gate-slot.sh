#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises scripts/gate-slot.py against a scratch lock directory: a full machine makes the next gate wait and names
# the holder, a free slot lets gates overlap, a nested gate takes no second slot, a killed holder frees its slot,
# --exclusive waits for every slot, integration gates queue on their own pool, and the exit status passes through.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
gate="$repo_root/scripts/gate-slot.py"
scratch=$(mktemp -d)
pids=()
cleanup() {
  for pid in "${pids[@]+"${pids[@]}"}"; do kill "$pid" 2>/dev/null || true; done
  rm -rf "$scratch"
}
trap cleanup EXIT

export GATE_SLOT_DIR="$scratch/slots" GATE_SLOT_POLL=1
unset GATE_SLOT_HELD

fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

# record <file> <seconds>: a gate body that writes its start and end times.
cat > "$scratch/record.py" <<'PY'
import sys, time
with open(sys.argv[1], "a") as out:
    out.write(f"{time.time()} ")
    out.flush()
    time.sleep(float(sys.argv[2]))
    out.write(f"{time.time()}\n")
PY
record() { printf 'python3 %q %q %q' "$scratch/record.py" "$1" "$2"; }

# overlap <a> <b>: true when the two recorded runs overlapped in time.
overlap() { python3 -c 'import sys; a=list(map(float,open(sys.argv[1]).read().split())); b=list(map(float,open(sys.argv[2]).read().split())); sys.exit(0 if a[0] < b[1] and b[0] < a[1] else 1)' "$1" "$2"; }

# wait_for_slot_holder [pool]: until slot 0 of the pool (gate by default) carries a holder record.
wait_for_slot_holder() {
  for _ in $(seq 1 50); do
    if [ -s "$GATE_SLOT_DIR/${1:-gate}/slot-0.lock" ]; then return 0; fi
    python3 -c 'import time; time.sleep(0.1)'
  done
  fail "the first gate never took a slot"
}

# One slot: the second gate waits for the first, and its wait names who holds the slot.
GATE_SLOTS=1 python3 "$gate" --label first -- bash -c "$(record "$scratch/a" 3)" &
pids+=($!)
wait_for_slot_holder
GATE_SLOTS=1 python3 "$gate" --label second -- bash -c "$(record "$scratch/b" 0)" 2> "$scratch/second.err"
wait "${pids[0]}"
if overlap "$scratch/a" "$scratch/b"; then fail "two gates ran at once with GATE_SLOTS=1"; fi
grep -q 'slot 0: pid .* first in ' "$scratch/second.err" || fail "the waiting gate did not name the holder: $(cat "$scratch/second.err")"

# Two slots: two gates run side by side.
rm -rf "$GATE_SLOT_DIR" "$scratch/a" "$scratch/b"; pids=()
GATE_SLOTS=2 python3 "$gate" --label first -- bash -c "$(record "$scratch/a" 2)" &
pids+=($!)
wait_for_slot_holder
GATE_SLOTS=2 python3 "$gate" --label second -- bash -c "$(record "$scratch/b" 0)"
wait "${pids[0]}"
overlap "$scratch/a" "$scratch/b" || fail "a free slot did not let the second gate run"

# A gate nested inside another (make check running verify-affected) takes no second slot, so it cannot deadlock.
rm -rf "$GATE_SLOT_DIR"
GATE_SLOTS=1 timeout 20 python3 "$gate" --label outer -- python3 "$gate" --label inner -- true \
  || fail "a nested gate deadlocked at GATE_SLOTS=1"

# A holder killed with SIGKILL frees its slot: the lock dies with the process.
rm -rf "$GATE_SLOT_DIR"; pids=()
# The orphaned child keeps running after its gate dies; a unique sleep length lets cleanup find it.
GATE_SLOTS=1 python3 "$gate" --label doomed -- python3 -c 'import time; time.sleep(61.25)' &
pids+=($!)
wait_for_slot_holder
kill -9 "${pids[0]}"
wait "${pids[0]}" 2>/dev/null || true
GATE_SLOTS=1 timeout 20 python3 "$gate" --label next -- true || fail "a killed holder kept its slot"
pkill -f 'time.sleep\(61.25\)' 2>/dev/null || true

# --exclusive waits until every slot is free.
rm -rf "$GATE_SLOT_DIR" "$scratch/a" "$scratch/b"; pids=()
GATE_SLOTS=2 python3 "$gate" --label gate -- bash -c "$(record "$scratch/a" 3)" &
pids+=($!)
wait_for_slot_holder
GATE_SLOTS=2 python3 "$gate" --exclusive --label bench -- bash -c "$(record "$scratch/b" 0)" 2> /dev/null
wait "${pids[0]}"
if overlap "$scratch/a" "$scratch/b"; then fail "an exclusive run started beside another gate"; fi

# Integration gates queue on their own one-slot pool, so a second one waits while a build gate still runs beside it.
rm -rf "$GATE_SLOT_DIR" "$scratch/a" "$scratch/b" "$scratch/c"; pids=()
GATE_SLOTS=2 python3 "$gate" --pool integration --label first-integration -- bash -c "$(record "$scratch/a" 3)" &
pids+=($!)
wait_for_slot_holder integration
GATE_SLOTS=2 python3 "$gate" --label build -- bash -c "$(record "$scratch/c" 0)"
GATE_SLOTS=2 python3 "$gate" --pool integration --label second-integration -- bash -c "$(record "$scratch/b" 0)" 2> "$scratch/integration.err"
wait "${pids[0]}"
overlap "$scratch/a" "$scratch/c" || fail "a build gate waited for an integration gate"
if overlap "$scratch/a" "$scratch/b"; then fail "two integration gates ran at once"; fi
grep -q 'integration slots' "$scratch/integration.err" || fail "the waiting integration gate did not name its pool: $(cat "$scratch/integration.err")"

# A benchmark that needs both pools waits for an integration gate too.
rm -rf "$GATE_SLOT_DIR" "$scratch/a" "$scratch/b"; pids=()
GATE_SLOTS=2 python3 "$gate" --pool integration --label integration -- bash -c "$(record "$scratch/a" 3)" &
pids+=($!)
wait_for_slot_holder integration
GATE_SLOTS=2 python3 "$gate" --exclusive --pool gate --pool integration --label bench -- bash -c "$(record "$scratch/b" 0)" 2> /dev/null
wait "${pids[0]}"
if overlap "$scratch/a" "$scratch/b"; then fail "a benchmark started beside an integration gate"; fi

# The gate's exit status passes through; GATE_SLOTS=0 runs the command with no slot at all.
rm -rf "$GATE_SLOT_DIR"
set +e
GATE_SLOTS=1 python3 "$gate" --label failing -- bash -c 'exit 7'
status=$?
set -e
[ "$status" -eq 7 ] || fail "exit status 7 came back as $status"
rm -rf "$GATE_SLOT_DIR"
GATE_SLOTS=0 python3 "$gate" --label unlimited -- true || fail "GATE_SLOTS=0 did not run the command"
[ ! -e "$GATE_SLOT_DIR" ] || fail "GATE_SLOTS=0 still created the lock directory"

printf 'gate-slot: all checks passed\n'
