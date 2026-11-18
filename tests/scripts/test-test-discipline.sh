#!/usr/bin/env bash
# Copyright (c) Mahmoud Shaheen. All rights reserved.
# Exercises `scripts/test-discipline.py` in a scratch repository: a frozen file passes, a new real wait, wall-clock
# read, per-test retry, or unreasoned skip fails naming the file and the fix, a reason comment opts a line out except
# in the messaging fast tier, a fall fails until the baseline is rewritten, the rewrite never raises a line, and
# a renamed file fails until the rewrite moves its line, and a line raised by hand fails.
set -euo pipefail

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
repo="$scratch/repo"

fail() { printf 'FAIL: %s\n' "$1" >&2; [ -f "$scratch/out" ] && cat "$scratch/out" >&2; exit 1; }

mkdir -p "$repo/scripts" "$repo/eng" "$repo/tests/Foo.Tests.Unit" "$repo/tests/Headless.Messaging.Tests.Unit"
cp "$repo_root/scripts/test-discipline.py" "$repo/scripts/"
git -C "$repo" init -q -b main
git -C "$repo" config user.email test@example.com
git -C "$repo" config user.name test

cat >"$repo/tests/Foo.Tests.Unit/OldTests.cs" <<'CS'
public sealed class OldTests
{
    public async Task waits() { await Task.Delay(100, AbortToken); var now = DateTime.UtcNow; }
}
CS
git -C "$repo" add -A
git -C "$repo" -c core.hooksPath=/dev/null commit -q -m seed
git -C "$repo" update-ref refs/remotes/origin/main HEAD

run() { python3 -I "$repo/scripts/test-discipline.py" "$@" >"$scratch/out" 2>&1; }
# expect <label> <exit> [text the output must contain]; runs the script with the arguments in `args`.
expect() {
  local label="$1" code="$2" needle="${3:-}" rc=0
  run "${args[@]}" || rc=$?
  [ "$rc" -eq "$code" ] || fail "$label: exit $rc, expected $code"
  if [ -n "$needle" ] && ! grep -qF -- "$needle" "$scratch/out"; then fail "$label: output lacks '$needle'"; fi
}
new() { cat >"$repo/tests/$1"; }

args=(baseline)
expect "seed the baseline" 0 "recorded 2 findings"
grep -qxF 'real-wait 1 tests/Foo.Tests.Unit/OldTests.cs' "$repo/eng/test-discipline-baseline.txt" || fail "seed lacks the real-wait line"
args=(check)
expect "a frozen file holds" 0 "hold the baseline"

new Foo.Tests.Unit/NewTests.cs <<'CS'
public sealed class NewTests
{
    public void sleeps() { Thread.Sleep(5); }
}
CS
expect "a new Thread.Sleep fails" 3 "tests/Foo.Tests.Unit/NewTests.cs: 1 real-wait (lines 3), and the file has no baseline line"
grep -qF 'await the signal' "$scratch/out" || fail "the real-wait failure does not name the fix"

new Foo.Tests.Unit/NewTests.cs <<'CS'
public sealed class NewTests
{
    // real-wait: the test proves the client gives up after its real 5 ms socket timeout
    public void sleeps() { Thread.Sleep(5); }
}
CS
expect "a reason comment opts the line out" 0

new Headless.Messaging.Tests.Unit/FastTests.cs <<'CS'
public sealed class FastTests
{
    public async Task waits() { await Task.Delay(5, AbortToken); } // real-wait: runs on simulated time anyway
}
CS
expect "the messaging fast tier ignores a real-wait opt-out" 3 "FastTests.cs: 1 real-wait"
grep -qF 'does not apply' "$scratch/out" || fail "the fast-tier failure does not say the opt-out is ignored there"
rm "$repo/tests/Headless.Messaging.Tests.Unit/FastTests.cs"

new Foo.Tests.Unit/IgnoredTests.cs <<'CS'
public sealed class IgnoredTests
{
    // await Task.Delay(100, AbortToken);
    public async Task hangs() { await Task.Delay(Timeout.InfiniteTimeSpan, AbortToken); }
    public void stamps() { var url = "http://localhost"; // DateTime.UtcNow
    }
}
CS
expect "comments and infinite waits are not counted" 0

new Foo.Tests.Unit/RetryTests.cs <<'CS'
public sealed class RetryTests
{
    [RetryFact(MaxRetries = 2)]
    public void flaky() { }

    [Fact(Timeout = 1000)]
    public void slow() { }
}
CS
expect "a retry or per-test timeout fails" 3 "RetryTests.cs: 2 test-timeout"
rm "$repo/tests/Foo.Tests.Unit/RetryTests.cs"

new Foo.Tests.Unit/SkipTests.cs <<'CS'
public sealed class SkipTests
{
    [Fact(Skip = "flaky")]
    public void unreasoned() { }

    [Fact]
    public void quarantined() { Assert.Skip("Hangs under load. https://github.com/owner/repo/issues/12"); }

    [Fact]
    public void gated()
    {
        if (!Fixture.SupportsLeases)
        {
            Assert.Skip("The store has no lease API.");
        }

        Assert.SkipUnless(Fixture.SupportsClaims, "The store cannot claim.");
    }

    // quarantine: #34 times out under a full module run
    [Fact(Skip = "times out")]
    public void marked() { }

    // quarantine: times out
    [Fact(Skip = "times out")]
    public void marked_without_issue() { }
}
CS
expect "only skips without an issue or capability fail" 3 "SkipTests.cs: 2 skip (lines 3, 25)"
rm "$repo/tests/Foo.Tests.Unit/SkipTests.cs"

mkdir -p "$repo/src/Foo/wwwroot/test"
new ../src/Foo/wwwroot/test/view.test.tsx <<'TS'
it('waits', async () => {
  await new Promise((done) => setTimeout(done, 50))
  expect(Date.now()).toBeGreaterThan(0)
})
it.skip('later', () => {})
TS
expect "dashboard tests follow the same rules" 3 "view.test.tsx: 1 skip"
grep -qF 'view.test.tsx: 1 real-wait' "$scratch/out" || fail "setTimeout as a sleep was not counted"
rm -r "$repo/src"

# A removed wait fails until the baseline is rewritten, and the rewrite keeps the clock read it did not remove.
rm "$repo/tests/Foo.Tests.Unit/NewTests.cs" "$repo/tests/Foo.Tests.Unit/IgnoredTests.cs"
printf 'public sealed class OldTests { public void now() { var now = DateTime.UtcNow; } }\n' >"$repo/tests/Foo.Tests.Unit/OldTests.cs"
expect "a fall fails and names the command" 3 "make test-discipline-baseline"
args=(baseline)
expect "the rewrite lowers the line" 0 "baseline holds 1 findings (was 2)"
args=(check)
expect "the lowered baseline holds" 0

# The rewrite never raises a line: a new finding stays failing after it.
printf 'public sealed class OldTests { public void now() { var a = DateTime.UtcNow; var b = DateTimeOffset.UtcNow; } }\n' >"$repo/tests/Foo.Tests.Unit/OldTests.cs"
args=(baseline)
expect "the rewrite refuses a rise" 0 "1 findings above the baseline stay failing"
args=(check)
expect "the rise still fails" 3 "OldTests.cs: 2 wall-clock (lines 1, 1), above its baseline of 1"

# A renamed file is held to its old line, and the check fails until the rewrite moves the line to the new path.
printf 'public sealed class OldTests { public void now() { var a = DateTime.UtcNow; } }\n' >"$repo/tests/Foo.Tests.Unit/OldTests.cs"
git -C "$repo" add -A
git -C "$repo" -c core.hooksPath=/dev/null commit -q -m lowered
git -C "$repo" update-ref refs/remotes/origin/main HEAD
git -C "$repo" mv tests/Foo.Tests.Unit/OldTests.cs tests/Foo.Tests.Unit/MovedTests.cs
args=(check --base origin/main)
expect "a renamed file asks for its line to move" 3 "MovedTests.cs: wall-clock 1, moved from tests/Foo.Tests.Unit/OldTests.cs"
args=(check)
expect "the full check agrees on the rename" 3 "make test-discipline-baseline"
args=(baseline)
expect "the rewrite moves the line" 0 "baseline holds 1 findings (was 1)"
grep -qxF 'wall-clock 1 tests/Foo.Tests.Unit/MovedTests.cs' "$repo/eng/test-discipline-baseline.txt" || fail "the rewrite did not move the line"
args=(check --base origin/main)
expect "the moved line holds; a changed baseline checks every file" 0 "test files hold the baseline."
# After the merge no rename is left to detect, and the moved line must still hold.
git -C "$repo" add -A
git -C "$repo" -c core.hooksPath=/dev/null commit -q -m moved
git -C "$repo" update-ref refs/remotes/origin/main HEAD
args=(check)
expect "the moved line holds after the merge" 0

# Raising a line by hand to admit a new wait fails in both modes, though the wait itself is within the edited line.
printf 'public sealed class MovedTests { public void now() { var a = DateTime.UtcNow; Thread.Sleep(1); } }\n' >"$repo/tests/Foo.Tests.Unit/MovedTests.cs"
printf 'real-wait 1 tests/Foo.Tests.Unit/MovedTests.cs\n' >>"$repo/eng/test-discipline-baseline.txt"
args=(check --base origin/main)
expect "a hand-raised line fails the affected check" 3 "real-wait 1, was 0 on the merge base"
args=(check)
expect "a hand-raised line fails the full check" 3 "the baseline only falls"

echo "test-test-discipline: all cases passed"
