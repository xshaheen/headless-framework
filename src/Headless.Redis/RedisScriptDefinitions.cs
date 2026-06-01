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
              result = redis.call('set', @key, @lockId, 'NX', 'PX', @expires)
            else
              result = redis.call('set', @key, @lockId, 'NX')
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
            redis.call('zadd', @holdersKey, expiryMs, @lockId)
            local safetyTtl = tonumber(@expires) * 2
            local currentTtl = redis.call('pttl', @holdersKey)
            if currentTtl < safetyTtl then
              redis.call('pexpire', @holdersKey, safetyTtl)
            end

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
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)

            local expiryMs = nowMs + tonumber(@expires)
            local changed = redis.call('zadd', @holdersKey, 'XX', 'CH', expiryMs, @lockId)
            if changed == 0 then
              return 0
            end

            local safetyTtl = tonumber(@expires) * 2
            local currentTtl = redis.call('pttl', @holdersKey)
            if currentTtl < safetyTtl then
              redis.call('pexpire', @holdersKey, safetyTtl)
            end
            return 1
            """
        ) { }
}

/// <summary>Prunes expired semaphore slots and checks whether a holder is still live.</summary>
public sealed class ValidateSemaphoreScriptDefinition : RedisScriptDefinition
{
    public static ValidateSemaphoreScriptDefinition Instance { get; } = new();

    private ValidateSemaphoreScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)
            if redis.call('zscore', @holdersKey, @lockId) ~= false then
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
            return redis.call('zrem', @holdersKey, @lockId)
            """
        ) { }
}

/// <summary>Prunes expired semaphore slots and returns the live holder count.</summary>
public sealed class GetSemaphoreCountScriptDefinition : RedisScriptDefinition
{
    public static GetSemaphoreCountScriptDefinition Instance { get; } = new();

    private GetSemaphoreCountScriptDefinition()
        : base(
            """
            local nowSecMicro = redis.call('TIME')
            local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

            redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)
            return redis.call('zcard', @holdersKey)
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
              redis.call('hset', @readerKey, @lockId, tostring(expiryMs))
              local readerTtl = redis.call('pttl', @readerKey)
              local safetyTtl = tonumber(@expires) * 2
              if readerTtl < safetyTtl then
                redis.call('pexpire', @readerKey, safetyTtl)
              end
            else
              redis.call('hset', @readerKey, @lockId, '0')
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
            if redis.call('hexists', @readerKey, @lockId) == 0 then
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
              redis.call('hset', @readerKey, @lockId, tostring(expiryMs))
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
            return redis.call('hdel', @readerKey, @lockId)
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
                  redis.call('set', @writerKey, @lockId, 'PX', @expires)
                else
                  redis.call('set', @writerKey, @lockId)
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
            if redis.call('get', @writerKey) ~= @lockId then
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
            if current == @lockId or current == @waitingId then
              return redis.call('del', @writerKey)
            end
            return 0
            """
        ) { }
}
