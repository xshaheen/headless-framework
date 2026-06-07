// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Redis;

/// <summary>Atomically acquires a mutex lock and issues a fencing token only when the grant succeeds.</summary>
public sealed class TryAcquireLockWithFenceScriptDefinition : RedisScriptDefinition
{
    public static TryAcquireLockWithFenceScriptDefinition Instance { get; } = new();

    private TryAcquireLockWithFenceScriptDefinition()
        : base(
            """
            local result
            if (@expires ~= nil and @expires ~= '') then
              result = redis.call('set', @key, @leaseId, 'NX', 'PX', @expires)
            else
              result = redis.call('set', @key, @leaseId, 'NX')
            end

            if result then
              return {1, redis.call('incr', @fenceKey)}
            end

            return {0}
            """
        ) { }
}

/// <summary>Atomically acquires a distributed semaphore slot and issues a fencing token.</summary>
public sealed class TryAcquireSemaphoreWithFenceScriptDefinition : RedisScriptDefinition
{
    public static TryAcquireSemaphoreWithFenceScriptDefinition Instance { get; } = new();

    private TryAcquireSemaphoreWithFenceScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)

            if redis.call('zcard', @holdersKey) >= tonumber(@maxCount) then
              return {0}
            end

            local expiryMs = nowMs + tonumber(@expires)
            redis.call('zadd', @holdersKey, expiryMs, @leaseId)
            local safetyTtl = tonumber(@expires) * 2
            local currentTtl = redis.call('pttl', @holdersKey)
            if currentTtl < safetyTtl then
              redis.call('pexpire', @holdersKey, safetyTtl)
            end

            -- @fenceKey is intentionally persistent (no TTL/PEXPIRE): the monotonic fence counter
            -- must outlive every holder so tokens never reset. Do not add an expiry here.
            return {1, redis.call('incr', @fenceKey)}
            """
        ) { }
}

/// <summary>Atomically extends a distributed semaphore slot when the holder is still present.</summary>
public sealed class TryExtendSemaphoreScriptDefinition : RedisScriptDefinition
{
    public static TryExtendSemaphoreScriptDefinition Instance { get; } = new();

    private TryExtendSemaphoreScriptDefinition()
        : base(
            // No ZREMRANGEBYSCORE prune: extend is XX-gated, so it only mutates a member that
            // already exists. Pruning expired members here would be an extra write with no bearing
            // on the extending holder's own slot (whose score is simply updated). Expired-slot
            // reclamation is the acquire script's job, where it is correctness-critical.
            //
            // Soft expiry: XX matches any existing member, including one whose score has already
            // lapsed but has not yet been pruned by a competing acquire. Such a holder reclaims its
            // own slot on extend rather than losing it — standard lease-renewal semantics, and
            // capacity-safe because script atomicity orders this extend against any acquire that
            // would prune-then-take the slot. "Expired" is therefore soft until an acquire prunes.
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            local expiryMs = nowMs + tonumber(@expires)
            -- GT: a shorter extend must never shorten a live lease. An "extend" that moves expiry
            -- earlier is incoherent (it would prematurely surrender capacity), so only grow the score.
            -- This matches the in-memory provider's GREATEST semantics. XX still gates on existence so a
            -- non-holder cannot create a slot; existence is reasserted via zscore because GT suppresses the
            -- CH "changed" signal when the new score is not greater, which we must not read as "missing".
            local existing = redis.call('zscore', @holdersKey, @leaseId)
            if existing == false then
              return 0
            end
            redis.call('zadd', @holdersKey, 'XX', 'GT', expiryMs, @leaseId)

            local safetyTtl = tonumber(@expires) * 2
            local currentTtl = redis.call('pttl', @holdersKey)
            if currentTtl < safetyTtl then
              redis.call('pexpire', @holdersKey, safetyTtl)
            end
            return 1
            """
        ) { }
}

/// <summary>
/// Read-only check of whether a holder is still live (its slot's expiry score has not passed).
/// Does NOT prune expired slots — validation must not mutate state on a hot per-iteration path.
/// </summary>
public sealed class ValidateSemaphoreScriptDefinition : RedisScriptDefinition
{
    public static ValidateSemaphoreScriptDefinition Instance { get; } = new();

    private ValidateSemaphoreScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            local score = redis.call('zscore', @holdersKey, @leaseId)
            if score ~= false and tonumber(score) > nowMs then
              return 1
            end
            return 0
            """
        ) { }
}

/// <summary>Atomically releases a distributed semaphore slot.</summary>
public sealed class ReleaseSemaphoreScriptDefinition : RedisScriptDefinition
{
    public static ReleaseSemaphoreScriptDefinition Instance { get; } = new();

    private ReleaseSemaphoreScriptDefinition()
        : base(
            """
            return redis.call('zrem', @holdersKey, @leaseId)
            """
        ) { }
}

/// <summary>
/// Read-only live holder count. Does NOT prune expired slots — it counts members whose expiry
/// score is still in the future via ZCOUNT, so a stale (expired-but-unpruned) slot is excluded from
/// the result without a write. Expired-slot reclamation is the acquire script's job.
/// </summary>
public sealed class GetSemaphoreCountScriptDefinition : RedisScriptDefinition
{
    public static GetSemaphoreCountScriptDefinition Instance { get; } = new();

    private GetSemaphoreCountScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            return redis.call('zcount', @holdersKey, '(' .. nowMs, '+inf')
            """
        ) { }
}

/// <summary>Atomically increments a value and sets expiration in a single operation.</summary>
public sealed class IncrementWithExpireScriptDefinition : RedisScriptDefinition
{
    public static IncrementWithExpireScriptDefinition Instance { get; } = new();

    private IncrementWithExpireScriptDefinition()
        : base(
            """
            if math.modf(@value) == 0 then
              redis.call('incrby', @key, @value)
            else
              redis.call('incrbyfloat', @key, @value)
            end
            if (@expires ~= nil and @expires ~= '') then
              redis.call('pexpire', @key, math.ceil(@expires))
            end
            return redis.call('get', @key)
            """
        ) { }
}

/// <summary>Atomically removes a key only if its value matches the expected value.</summary>
public sealed class RemoveIfEqualScriptDefinition : RedisScriptDefinition
{
    public static RemoveIfEqualScriptDefinition Instance { get; } = new();

    private RemoveIfEqualScriptDefinition()
        : base(
            """
            if redis.call('get', @key) == @expected then
              return redis.call('del', @key)
            else
              return 0
            end
            """
        ) { }
}

/// <summary>Atomically replaces a value only if it matches the expected value.</summary>
public sealed class ReplaceIfEqualScriptDefinition : RedisScriptDefinition
{
    public static ReplaceIfEqualScriptDefinition Instance { get; } = new();

    private ReplaceIfEqualScriptDefinition()
        : base(
            """
            local currentVal = redis.call('get', @key)
            local expected = @expected
            if expected == '' then expected = false end
            if currentVal == expected then
              if (@expires ~= nil and @expires ~= '') then
                return redis.call('set', @key, @value, 'PX', @expires) and 1 or 0
              else
                return redis.call('set', @key, @value) and 1 or 0
              end
            else
              return -1
            end
            """
        ) { }
}

/// <summary>Sets a value only if it's higher than the current value. Creates the key if it doesn't exist.</summary>
public sealed class SetIfHigherScriptDefinition : RedisScriptDefinition
{
    public static SetIfHigherScriptDefinition Instance { get; } = new();

    private SetIfHigherScriptDefinition()
        : base(
            """
            local c = tonumber(redis.call('get', @key))
            local v = tonumber(@value)
            if c then
              if v > c then
                redis.call('set', @key, @value)
                if (@expires ~= nil and @expires ~= '') then
                  redis.call('pexpire', @key, math.ceil(@expires))
                end
              end
            else
              redis.call('set', @key, @value)
              if (@expires ~= nil and @expires ~= '') then
                redis.call('pexpire', @key, math.ceil(@expires))
              end
            end
            return redis.call('get', @key)
            """
        ) { }
}

/// <summary>Sets a value only if it's lower than the current value. Creates the key if it doesn't exist.</summary>
public sealed class SetIfLowerScriptDefinition : RedisScriptDefinition
{
    public static SetIfLowerScriptDefinition Instance { get; } = new();

    private SetIfLowerScriptDefinition()
        : base(
            """
            local c = tonumber(redis.call('get', @key))
            local v = tonumber(@value)
            if c then
              if v < c then
                redis.call('set', @key, @value)
                if (@expires ~= nil and @expires ~= '') then
                  redis.call('pexpire', @key, math.ceil(@expires))
                end
              end
            else
              redis.call('set', @key, @value)
              if (@expires ~= nil and @expires ~= '') then
                redis.call('pexpire', @key, math.ceil(@expires))
              end
            end
            return redis.call('get', @key)
            """
        ) { }
}

/// <summary>Atomically acquires a reader lock when no writer holds the resource.</summary>
public sealed class TryAcquireReadLockScriptDefinition : RedisScriptDefinition
{
    public static TryAcquireReadLockScriptDefinition Instance { get; } = new();

    private TryAcquireReadLockScriptDefinition()
        : base(
            """
            local writerValue = redis.call('get', @writerKey)
            if writerValue ~= false then
              return 0
            end

            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            if (@expires ~= nil and @expires ~= '') then
              local expiryMs = nowMs + tonumber(@expires)
              redis.call('hset', @readerKey, @leaseId, tostring(expiryMs))
              local readerTtl = redis.call('pttl', @readerKey)
              local safetyTtl = tonumber(@expires) * 2
              if readerTtl < safetyTtl then
                redis.call('pexpire', @readerKey, safetyTtl)
              end
            else
              redis.call('hset', @readerKey, @leaseId, '0')
            end

            return 1
            """
        ) { }
}

/// <summary>Atomically extends a reader lock if the caller's lock id is still present.</summary>
public sealed class TryExtendReadLockScriptDefinition : RedisScriptDefinition
{
    public static TryExtendReadLockScriptDefinition Instance { get; } = new();

    private TryExtendReadLockScriptDefinition()
        : base(
            """
            if redis.call('hexists', @readerKey, @leaseId) == 0 then
              return 0
            end

            local writerValue = redis.call('get', @writerKey)
            if writerValue ~= false then
              local suffix = ':_WRITERWAITING'
              if string.sub(writerValue, -string.len(suffix)) == suffix then
                return 0
              end
            end

            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            if (@expires ~= nil and @expires ~= '') then
              local expiryMs = nowMs + tonumber(@expires)
              redis.call('hset', @readerKey, @leaseId, tostring(expiryMs))
              local readerTtl = redis.call('pttl', @readerKey)
              local safetyTtl = tonumber(@expires) * 2
              if readerTtl < safetyTtl then
                redis.call('pexpire', @readerKey, safetyTtl)
              end
            end

            return 1
            """
        ) { }
}

/// <summary>Atomically releases a reader lock id from the reader hash.</summary>
public sealed class ReleaseReadLockScriptDefinition : RedisScriptDefinition
{
    public static ReleaseReadLockScriptDefinition Instance { get; } = new();

    private ReleaseReadLockScriptDefinition()
        : base(
            """
            return redis.call('hdel', @readerKey, @leaseId)
            """
        ) { }
}

/// <summary>Atomically acquires a writer lock or plants the caller's writer-waiting marker.</summary>
public sealed class TryAcquireWriteLockScriptDefinition : RedisScriptDefinition
{
    public static TryAcquireWriteLockScriptDefinition Instance { get; } = new();

    private TryAcquireWriteLockScriptDefinition()
        : base(
            """
            local writerValue = redis.call('get', @writerKey)

            local suffix = ':_WRITERWAITING'
            local markerHeld = writerValue ~= false and string.sub(writerValue, -string.len(suffix)) == suffix
            local canClaim = writerValue == false or markerHeld

            if canClaim then
              local nowSecMicro = redis.call('TIME')
              local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

              -- Prune any reader entries whose per-entry expiry has passed before checking for live
              -- readers. Single HGETALL beats HKEYS + per-field HGET: one round-trip's worth of work
              -- inside the Lua VM regardless of reader count.
              local entries = redis.call('hgetall', @readerKey)
              for i = 1, #entries, 2 do
                local field = entries[i]
                local value = entries[i + 1]
                local expiry = tonumber(value)
                if expiry and expiry > 0 and expiry <= nowMs then
                  redis.call('hdel', @readerKey, field)
                end
              end

              if redis.call('hlen', @readerKey) == 0 then
                if (@expires ~= nil and @expires ~= '') then
                  redis.call('set', @writerKey, @leaseId, 'PX', @expires)
                else
                  redis.call('set', @writerKey, @leaseId)
                end
                return 1
              end

              if (@markerExpires ~= nil and @markerExpires ~= '') then
                redis.call('set', @writerKey, @waitingId, 'PX', @markerExpires)
              else
                redis.call('set', @writerKey, @waitingId)
              end
            end

            return 0
            """
        ) { }
}

/// <summary>Atomically extends a writer lock when the writer key still belongs to the lock id.</summary>
public sealed class TryExtendWriteLockScriptDefinition : RedisScriptDefinition
{
    public static TryExtendWriteLockScriptDefinition Instance { get; } = new();

    private TryExtendWriteLockScriptDefinition()
        : base(
            """
            if redis.call('get', @writerKey) ~= @leaseId then
              return 0
            end

            if (@expires ~= nil and @expires ~= '') then
              redis.call('pexpire', @writerKey, @expires)
            end

            return 1
            """
        ) { }
}

/// <summary>Atomically releases a writer lock or the caller's writer-waiting marker.</summary>
public sealed class ReleaseWriteLockScriptDefinition : RedisScriptDefinition
{
    public static ReleaseWriteLockScriptDefinition Instance { get; } = new();

    private ReleaseWriteLockScriptDefinition()
        : base(
            """
            local current = redis.call('get', @writerKey)
            if current == @leaseId or current == @waitingId then
              return redis.call('del', @writerKey)
            end
            return 0
            """
        ) { }
}

/// <summary>Stores a guarded coordination heartbeat using Redis server time.</summary>
public sealed class RedisMembershipHeartbeatScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipHeartbeatScriptDefinition Instance { get; } = new();

    private RedisMembershipHeartbeatScriptDefinition()
        : base(
            """
            local current = redis.call('get', @genKey)
            if current == false or tonumber(current) ~= tonumber(@incarnation) then
              return 0
            end

            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            local hardExpiryMs = nowMs + tonumber(@hardMs)
            local payload = cjson.encode({
              last_beat_ms = nowMs,
              role = @role,
              metadata = @metadata
            })

            redis.call('zadd', @liveKey, hardExpiryMs, @member)
            redis.call('hset', @knownKey, @member, payload)

            return 1
            """
        ) { }
}

/// <summary>Classifies known coordination members with Redis server time.</summary>
public sealed class RedisMembershipReadScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipReadScriptDefinition Instance { get; } = new();

    private RedisMembershipReadScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            local entries = redis.call('hgetall', @knownKey)
            local result = {}

            for i = 1, #entries, 2 do
              local member = entries[i]
              local payloadText = entries[i + 1]
              local payload = cjson.decode(payloadText)
              local lastBeatMs = tonumber(payload['last_beat_ms'])
              local ageMs = nowMs - lastBeatMs

              if ageMs >= tonumber(@pruneMs) then
                redis.call('hdel', @knownKey, member)
                redis.call('zrem', @liveKey, member)
              else
                local nodeId, incarnation = string.match(member, '^(.+)@([0-9]+)$')
                if nodeId ~= nil then
                  local current = redis.call('get', @genKeyPrefix .. nodeId)
                  if current ~= false and tonumber(current) == tonumber(incarnation) then
                    local state = @aliveState
                    if ageMs >= tonumber(@hardMs) then
                      state = @deadState
                    elseif ageMs >= tonumber(@softMs) then
                      state = @suspectedState
                    end

                    table.insert(result, { member, state, payload['role'] or '', payload['metadata'] or '{}' })
                  end
                end
              end
            end

            table.sort(result, function(left, right) return left[1] < right[1] end)
            return result
            """
        ) { }
}

/// <summary>Marks a coordination member as left using Redis server time.</summary>
public sealed class RedisMembershipLeaveScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipLeaveScriptDefinition Instance { get; } = new();

    private RedisMembershipLeaveScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            local lastBeatMs = nowMs - tonumber(@hardMs)
            local role = @role
            local metadata = @metadata

            local existing = redis.call('hget', @knownKey, @member)
            if existing ~= false then
              local payload = cjson.decode(existing)
              role = payload['role'] or role
              metadata = payload['metadata'] or metadata
            end

            redis.call('hset', @knownKey, @member, cjson.encode({
              last_beat_ms = lastBeatMs,
              role = role,
              metadata = metadata
            }))
            redis.call('zrem', @liveKey, @member)
            return 1
            """
        ) { }
}

/// <summary>Prunes expired coordination liveness entries without deleting generation counters.</summary>
public sealed class RedisMembershipCleanupScriptDefinition : RedisScriptDefinition
{
    public static RedisMembershipCleanupScriptDefinition Instance { get; } = new();

    private RedisMembershipCleanupScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)
            local entries = redis.call('hgetall', @knownKey)
            local removed = 0

            redis.call('zremrangebyscore', @liveKey, '-inf', nowMs)

            for i = 1, #entries, 2 do
              local member = entries[i]
              local payload = cjson.decode(entries[i + 1])
              local ageMs = nowMs - tonumber(payload['last_beat_ms'])
              if ageMs >= tonumber(@pruneMs) then
                redis.call('hdel', @knownKey, member)
                redis.call('zrem', @liveKey, member)
                removed = removed + 1
              end
            end

            return removed
            """
        ) { }
}
