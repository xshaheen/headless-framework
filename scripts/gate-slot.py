#!/usr/bin/env python3
# Copyright (c) Mahmoud Shaheen. All rights reserved.
"""Run a heavy gate only while holding one of a few machine-wide slots.

Every worktree and every agent session on a machine shares one CPU, one memory pool, and one Docker
daemon. Gates started side by side from several worktrees drove the load average past 180, made
benchmarks meaningless, hung a broker integration suite for an hour, and left agents serializing by
hand with `pgrep` loops that could wait on themselves. A slot is an exclusive lock on one file in a
directory every checkout shares, so the limit holds across worktrees with no coordination between them.

The lock is an flock on a descriptor only this process holds (Python opens files non-inheritable), so
it is released the moment this process exits for any reason, and a dotnet build server the gate leaves
running cannot keep a slot. The flip side: a SIGKILL of this wrapper frees the slot while its gate keeps
running as an orphan, since no portable call lets a child die with its parent on macOS. Stop a gate with
Ctrl-C or SIGTERM, which this wrapper forwards, rather than kill -9. A nested gate (`make check` running `verify-affected`) sees GATE_SLOT_HELD
and runs without taking a second slot, which would otherwise deadlock at GATE_SLOTS=1.

Slots come in pools, each with its own size. `gate` limits the build-and-unit-test gates. `integration` limits
the gates that start containers: TEST_MAX_PARALLEL stays at 3 because six integration modules at once broke
timing-sensitive suites, so two integration gates side by side would recreate that. A gate names the pools it
needs; they are always taken in POOLS order, so no two gates can each hold what the other waits for.

A benchmark needs the machine to itself, so --exclusive takes every slot of every pool it names. It takes them
in index order and keeps each one while it waits for the next, so two exclusive runs cannot each hold half the
slots forever.

Usage: gate-slot.py [--exclusive] [--pool gate|integration]... --label <name> -- <command> [args...]

Environment:
  GATE_SLOTS              slots in the gate pool (default 2); 0 lifts that pool's limit
  GATE_INTEGRATION_SLOTS  slots in the integration pool (default 1); 0 lifts that pool's limit
  GATE_SLOT_DIR           the shared lock directory (default ~/.cache/headless-framework/gate-slots)
  GATE_SLOT_POLL          seconds between attempts while every slot is taken (default 2)

Standard library only; exits with the command's status (128 + signal when a signal ended it).
"""

from __future__ import annotations

import argparse
import fcntl
import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path

REPORT_EVERY_SECONDS = 60.0
# Acquisition order and each pool's size variable and default.
POOLS = {"gate": ("GATE_SLOTS", 2), "integration": ("GATE_INTEGRATION_SLOTS", 1)}


def _env_int(name: str, default: int) -> int:
    raw = os.environ.get(name, "").strip()
    if not raw:
        return default
    try:
        value = int(raw)
    except ValueError:
        sys.exit(f"gate-slot: {name}={raw!r} is not an integer")
    if value < 0:
        sys.exit(f"gate-slot: {name}={raw!r} must not be negative")
    return value


def _slot_dir() -> Path:
    configured = os.environ.get("GATE_SLOT_DIR", "").strip()
    return Path(configured) if configured else Path.home() / ".cache" / "headless-framework" / "gate-slots"


def _try_acquire(path: Path) -> int | None:
    fd = os.open(path, os.O_RDWR | os.O_CREAT, 0o644)
    try:
        fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        os.close(fd)
        return None
    return fd


def _holders(directory: Path, slots: int) -> list[str]:
    # A slot file keeps its last holder's record after release, so only a slot that is locked right now
    # names a live holder; a probe that gets the lock releases it at once.
    lines = []
    for index in range(slots):
        path = directory / f"slot-{index}.lock"
        probe = _try_acquire(path)
        if probe is not None:
            os.close(probe)
            continue
        try:
            record = json.loads(path.read_text() or "{}")
        except (OSError, ValueError):
            record = {}
        started = record.get("started")
        held_for = f"{int((time.time() - started) / 60)} min" if isinstance(started, (int, float)) else "?"
        lines.append(
            f"slot {index}: pid {record.get('pid', '?')} {record.get('label', '?')} in {record.get('cwd', '?')} ({held_for})"
        )
    return lines


def _claim(fd: int, label: str) -> None:
    record = {"pid": os.getpid(), "label": label, "cwd": os.getcwd(), "started": time.time()}
    os.ftruncate(fd, 0)
    os.pwrite(fd, json.dumps(record).encode(), 0)


def _acquire(directory: Path, slots: int, label: str, poll: float, exclusive: bool) -> list[int]:
    directory.mkdir(parents=True, exist_ok=True)
    held: list[int] = []
    waited_since = None
    # None, not 0.0: the monotonic clock may start near zero (Linux counts from boot), so a fresh CI runner would
    # otherwise skip the first report and a waiting gate would never say who holds the slots.
    last_report: float | None = None
    while True:
        if exclusive:
            while len(held) < slots:
                fd = _try_acquire(directory / f"slot-{len(held)}.lock")
                if fd is None:
                    break
                _claim(fd, label)
                held.append(fd)
            done = len(held) == slots
        else:
            for index in range(slots):
                fd = _try_acquire(directory / f"slot-{index}.lock")
                if fd is not None:
                    _claim(fd, label)
                    held.append(fd)
                    break
            done = bool(held)
        if done:
            if waited_since is not None:
                print(
                    f"[gate-slot] {label}: got {'every slot' if exclusive else 'a slot'} after "
                    f"{int(time.monotonic() - waited_since)} s",
                    file=sys.stderr,
                    flush=True,
                )
            return held
        now = time.monotonic()
        if waited_since is None:
            waited_since = now
        if last_report is None or now - last_report >= REPORT_EVERY_SECONDS:
            last_report = now
            holders = "\n  ".join(_holders(directory, slots)) or "(holders exited; retrying)"
            print(
                f"[gate-slot] {label}: waiting for {'every' if exclusive else 'one'} of {slots} {directory.name} slots "
                f"({POOLS[directory.name][0]}=<n> changes the limit, 0 lifts it):\n  {holders}",
                file=sys.stderr,
                flush=True,
            )
        time.sleep(poll)


def _run(command: list[str], env: dict[str, str]) -> int:
    # close_fds=False keeps inherited descriptors open, such as a make jobserver's under `make -j`.
    child = subprocess.Popen(command, env=env, close_fds=False)

    def forward(signum: int, _frame: object) -> None:
        child.send_signal(signum)

    for signum in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
        signal.signal(signum, forward)
    code = child.wait()
    return 128 - code if code < 0 else code


def main() -> int:
    parser = argparse.ArgumentParser(description="Run a command while holding a machine-wide gate slot.")
    parser.add_argument("--label", required=True, help="the gate's name, shown to anyone waiting for a slot")
    parser.add_argument("--exclusive", action="store_true", help="hold every slot, for a run that needs the machine")
    parser.add_argument("--pool", action="append", choices=list(POOLS), help="a pool to take a slot from (default: gate)")
    parser.add_argument("command", nargs=argparse.REMAINDER, help="the command after --")
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        parser.error("no command given after --")

    env = dict(os.environ)
    if env.get("GATE_SLOT_HELD"):
        return _run(command, env)

    wanted = set(args.pool or ["gate"])
    poll = float(_env_int("GATE_SLOT_POLL", 2))
    held: list[int] = []
    for pool, (variable, default) in POOLS.items():
        slots = _env_int(variable, default)
        if pool in wanted and slots > 0:
            held += _acquire(_slot_dir() / pool, slots, args.label, poll, args.exclusive)
    env["GATE_SLOT_HELD"] = "1"
    try:
        return _run(command, env)
    finally:
        for fd in held:
            os.close(fd)


if __name__ == "__main__":
    sys.exit(main())
