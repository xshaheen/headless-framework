---
title: "Redis ZSET distributed semaphore: separate pruning from counting and validation"
date: 2026-06-02
category: design-patterns
module: Headless.DistributedLocks.Redis
problem_type: design_pattern
component: database
severity: medium
applies_when:
  - implementing a Redis ZSET-backed N-holder distributed semaphore
  - read paths (count, validate) run at lease-monitor cadence or must be replica-routable
  - lease expiry is tracked as a sorted-set score and pruning cannot be coupled to every read
  - fencing tokens are required for stale-write rejection
tags:
  - redis
  - distributed-semaphore
  - zset
  - lua-scripts
  - lease-expiry
  - fencing-token
  - distributed-locks
  - concurrency
related_components:
  - Headless.DistributedLocks.Abstractions
  - Headless.Redis
---

# Redis ZSET distributed semaphore: separate pruning from counting and validation

## Context

A distributed N-holder semaphore on Redis models each holder as a member of a sorted set (ZSET), scored by the holder's lease-expiry timestamp. Expired holders must be reclaimed, the live count must be checked before granting a slot, and lease monitors must be able to validate/renew a slot on a heartbeat. Three forces shape the design:

1. **Clock skew** — multiple app nodes have drifting clocks. Trusting a caller's wall clock for slot expiry lets a slow node believe a slot is live while a fast node has already reclaimed it. All scores must come from the Redis server clock (`TIME`), never the caller.
2. **Write amplification** — lease monitors check liveness every heartbeat tick. If every read path also prunes, every tick becomes a ZSET write, inflating primary IOPS under contention and blocking replica routing.
3. **Atomicity** — the "count < maxCount → add" window is a race; without a single atomic operation, two acquires can both observe a free slot and both grant, exceeding the limit.

The recipe (Josiah Carlson, *Redis in Action* §6.3, adapted) is: model each slot as a **ZSET member scored by expiry**, derive time from `redis.call('TIME')`, and enforce a strict separation — the **write path** (acquire) prunes; the **read paths** (count, validate) and **extend** do not.

**How this design was reached (session history).** The first cut pruned (`ZREMRANGEBYSCORE`) inside the validate script on every monitor tick. Code review then proposed the opposite extreme — a *pure `ZSCORE`* validate with no time logic at all. That was caught as **wrong**: a `ZSCORE` on an expired-but-not-yet-pruned member still returns a score, so a dead lease would validate as held. The correct read-only form is `ZSCORE` **plus a comparison to server `TIME`** — neither prune-on-read nor a bare existence check. The same score-comparison principle then carried over to count (`ZCOUNT` by score range instead of `ZCARD`). *(session history)*

This pattern was implemented and tested in `Headless.DistributedLocks.Redis` (PR #368, issue #291).

## Guidance

### Data model

Each resource maps to two keys, kept on the same cluster slot via a hash tag:

```
{resource}:holders   — ZSET; member = lockId (string), score = expiry in Unix ms (Redis server clock)
fence:{resource}     — STRING; integer, monotonic fencing-token counter
```

The caller never supplies a slot expiry. All timestamps come from `redis.call('TIME')` inside Lua, so there is zero clock-skew exposure.

### Acquire — the ONLY script that prunes

Acquire is the single script that mutates membership. It must be atomic: prune expired slots, check the live count, conditionally add — one Lua execution. Redis single-threads Lua, so no two acquires interleave in the check-then-add window.

```lua
local nowSecMicro = redis.call('TIME')
local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)   -- prune expired

if redis.call('zcard', @holdersKey) >= tonumber(@maxCount) then
  return {0}
end

local expiryMs = nowMs + tonumber(@expires)
redis.call('zadd', @holdersKey, expiryMs, @lockId)

local safetyTtl = tonumber(@expires) * 2                     -- monotonic safety TTL on the key
local currentTtl = redis.call('pttl', @holdersKey)
if currentTtl < safetyTtl then
  redis.call('pexpire', @holdersKey, safetyTtl)
end

return {1, redis.call('incr', @fenceKey)}                    -- success + fencing token
```

The fencing token is incremented **only in the success branch**. Contended or failed acquires never advance the counter, so token values are strictly monotonic across granted slots. After pruning, `ZCARD` is safe here because every surviving member is live.

### Count — score-range filter, no prune

```lua
local nowSecMicro = redis.call('TIME')
local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

return redis.call('zcount', @holdersKey, '(' .. nowMs, '+inf')
```

`ZCOUNT` with an exclusive lower bound (`(nowMs`) counts only members whose score is strictly in the future. Expired-but-unpruned members score `<= nowMs` and are excluded without a single write. `ZCARD` here would over-count them.

### Validate — ZSCORE + score comparison, no prune

```lua
local nowSecMicro = redis.call('TIME')
local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

local score = redis.call('zscore', @holdersKey, @lockId)
if score ~= false and tonumber(score) > nowMs then
  return 1
end
return 0
```

`ZSCORE` returns `false` if the member is absent. The `score > nowMs` predicate is the load-bearing part — it rejects a present-but-expired member. A bare `ZSCORE` (no comparison) would validate a dead lease.

### Extend — ZADD XX/CH only, no prune

```lua
local expiryMs = nowMs + tonumber(@expires)
local changed = redis.call('zadd', @holdersKey, 'XX', 'CH', expiryMs, @lockId)
if changed == 0 then
  return 0    -- member absent; holder lost the slot
end
-- (refresh safety TTL with the same pttl-guard as acquire)
return 1
```

`ZADD XX` only updates an existing member; `CH` makes the return reflect whether the score changed, so a lost slot returns `0` and the caller aborts. Extend does **not** prune: the extending holder's slot is present by definition, and reclaiming other expired slots here has no bearing on the operation's correctness.

### Key construction

The C# layer wraps the resource in a hash tag so both keys land on the same cluster slot (required for multi-key Lua under Redis Cluster), and validates the boundary invariant:

```csharp
private static (RedisKey HoldersKey, RedisKey FenceKey) _GetKeys(string resource)
{
    var hashTag = "{" + resource + "}";
    return (hashTag + ":holders", "fence:" + hashTag);
}
```

Callers must not embed `{` or `}` in the resource name — the storage layer owns hash-tag construction.

## Why This Matters

**Correctness under partial expiry.** The common mistake is `ZCARD`/bare-`ZSCORE` on read paths. Both ignore the score. If acquire has not run recently, expired slots accumulate, and the read reports a full semaphore that actually has open slots — or validates a holder whose lease is gone. Score-filtered reads (`ZCOUNT` with a bound, `ZSCORE` with a comparison) are immune to unpruned members because they inspect the score, not the cardinality.

**Replica routability and write amplification.** Pruning on a read forces it to the primary and defeats replica offload. At lease-monitor frequency (every few seconds per holder), prune-on-read turns every liveness check into a primary write even when nothing expired. Deferring all pruning to acquire keeps reads genuinely read-only.

**Atomic last-slot protection.** Because acquire is a single Lua script (prune + count + add), two concurrent callers cannot both observe `count == maxCount - 1` and both win — one runs in the Lua VM while the other queues, then sees `count == maxCount` and returns `{0}`.

**Monotonic fencing.** `INCR` only on the granted branch means the per-resource counter advances exactly once per real acquisition, so a consumer passing the token to a protected resource can reject stale writers by token ordering.

## When to Apply

- A **counting semaphore** (N > 1 holders), not a mutex.
- Slots have a **finite TTL** and must auto-reclaim after lease expiry.
- **Multiple nodes** compete (clock skew rules out caller-supplied expiry).
- **Lease monitors** extend slots on a heartbeat and must not inflate write IOPS.
- A **fencing token** is needed to detect out-of-order operations at the protected resource.

The same separation principle ports to other backends. On PostgreSQL/SQL Server the acquire equivalent takes a row/advisory lock (`pg_advisory_xact_lock`, `sp_getapplock`), deletes expired rows, counts live rows, inserts if under the limit; count and validate use `WHERE expiry > NOW()` predicates and delete nothing.

Do **not** use this for a mutex (N = 1) — a mutex is cheaper as `SET key lockId NX PX ttl` + compare-and-delete.

## Examples

### The test that actually guards the property

A test that reaches `count == 0` / `validate == false` by calling `TryAcquireAsync` first and waiting for expiry **does not test the read-only path** — acquire prunes, so the expired member never accumulates. To guard the score-filter, **plant** a past-expiry member directly and read without acquiring:

```csharp
[Fact]
public async Task should_exclude_expired_but_unpruned_slot_from_count_and_validate()
{
    var resource = "test-resource";
    var holdersKey = "{" + resource + "}:holders";
    var pastExpiry = DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeMilliseconds();

    // Plant an expired member that acquire has never pruned.
    await _db.SortedSetAddAsync(holdersKey, "stale-holder-id", pastExpiry);

    var count = await _storage.GetCountAsync(resource, TestContext.Current.CancellationToken);
    var valid = await _storage.ValidateAsync(resource, "stale-holder-id", TestContext.Current.CancellationToken);

    count.Should().Be(0);     // ZCOUNT score-range excludes it
    valid.Should().BeFalse(); // ZSCORE present but score <= now
}
```

If you wrote this by acquiring with a short TTL and `Task.Delay`-ing past expiry, it would still pass — but it would test the acquire-prune path, not the read-path score filter. The property only has teeth when the stale member was never pruned.

### Acquire result shape (C#)

```csharp
var result = await _TryAcquireSemaphoreAsync(keys.HoldersKey, keys.FenceKey, lockId, maxCount, ttl, ct);

return result.Acquired
    ? new DistributedLockAcquireResult(Acquired: true, result.FencingToken)
    : DistributedLockAcquireResult.Failed;
```

The fencing token exists only when `Acquired == true`; the script never `INCR`s on the failed branch, so a `Failed` caller must not read a token.

## Related

- [redlock-multi-instance-not-adopted](../tooling-decisions/redlock-multi-instance-not-adopted-2026-05-19.md) — why single-instance Redis locking is the chosen primitive and what fencing tokens guarantee; the per-resource `INCR` here is that fencing pattern. Do not present this semaphore as RedLock-equivalent consensus.
- [messaging-keyed-di-lock-isolation](../architecture-patterns/messaging-keyed-di-lock-isolation-2026-05-19.md) — keyed-DI isolation, the same pattern the shared Redis script loader uses so cache and lock packages don't shadow each other's registration.
- [circuit-breaker-transport-thread-safety-patterns](../concurrency/circuit-breaker-transport-thread-safety-patterns.md) — acquire-before-scope slot-leak and stale-timer pitfalls relevant to the semaphore's lease lifecycle.
- GitHub issue #291 (Phase 3b: N-holder Redis semaphore) — direct tracking issue; #287 is the parent roadmap.
