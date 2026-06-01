// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Redis;

/// <summary>
/// Lua scripts for atomic Redis operations.
/// </summary>
/// <remarks>
/// <para>
/// All scripts return string values to preserve floating-point precision (Redis EVAL converts
/// Lua numbers to integers, truncating decimals).
/// </para>
/// <para>
/// Expiration parameters accept milliseconds as integer or empty string for no expiration.
/// </para>
/// </remarks>
public static class RedisScripts
{
    /// <summary>
    /// Atomically acquires a mutex lock and issues a fencing token only when the grant succeeds.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The lock key</item>
    ///   <item><c>@fenceKey</c> - The per-resource fence counter key</item>
    ///   <item><c>@lockId</c> - The lock owner id</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b> <c>{1, token}</c> when acquired, or <c>{0}</c> when already held.
    /// </remarks>
    public const string TryAcquireLockWithFence = """
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
        """;

    /// <summary>Atomically acquires a distributed semaphore slot and issues a fencing token.</summary>
    public const string TryAcquireSemaphoreWithFence = """
        local nowSecMicro = redis.call('TIME')
        local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

        redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)

        if redis.call('zcard', @holdersKey) >= tonumber(@maxCount) then
          return {0}
        end

        local expiryMs = nowMs + tonumber(@expires)
        redis.call('zadd', @holdersKey, expiryMs, @lockId)
        redis.call('pexpire', @holdersKey, tonumber(@expires) * 2)

        return {1, redis.call('incr', @fenceKey)}
        """;

    /// <summary>Atomically extends a distributed semaphore slot when the holder is still present.</summary>
    public const string TryExtendSemaphore = """
        local nowSecMicro = redis.call('TIME')
        local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

        redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)

        local expiryMs = nowMs + tonumber(@expires)
        local changed = redis.call('zadd', @holdersKey, 'XX', expiryMs, @lockId)
        if changed == 0 then
          return 0
        end

        redis.call('pexpire', @holdersKey, tonumber(@expires) * 2)
        return 1
        """;

    /// <summary>Prunes expired semaphore slots and checks whether a holder is still live.</summary>
    public const string ValidateSemaphore = """
        local nowSecMicro = redis.call('TIME')
        local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

        redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)
        if redis.call('zscore', @holdersKey, @lockId) ~= false then
          return 1
        end
        return 0
        """;

    /// <summary>Atomically releases a distributed semaphore slot.</summary>
    public const string ReleaseSemaphore = """
        return redis.call('zrem', @holdersKey, @lockId)
        """;

    /// <summary>Prunes expired semaphore slots and returns the live holder count.</summary>
    public const string GetSemaphoreCount = """
        local nowSecMicro = redis.call('TIME')
        local nowMs = (tonumber(nowSecMicro[1]) * 1000) + math.floor(tonumber(nowSecMicro[2]) / 1000)

        redis.call('zremrangebyscore', @holdersKey, '-inf', nowMs)
        return redis.call('zcard', @holdersKey)
        """;

    /// <summary>
    /// Atomically replaces a value only if it matches the expected value.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@value</c> - The new value to set</item>
    ///   <item><c>@expected</c> - The expected current value (empty string = key should not exist)</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b>
    /// <list type="bullet">
    ///   <item><c>1</c> - Value was replaced successfully</item>
    ///   <item><c>0</c> - SET command failed (rare)</item>
    ///   <item><c>-1</c> - Current value doesn't match expected</item>
    /// </list>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Compare-and-swap (CAS) operations</item>
    ///   <item>Insert-if-not-exists when expected is empty string</item>
    ///   <item>Distributed lock renewal</item>
    /// </list>
    /// </remarks>
    public const string ReplaceIfEqual = """
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
        """;

    /// <summary>
    /// Atomically removes a key only if its value matches the expected value.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@expected</c> - The expected current value</item>
    /// </list>
    /// <b>Returns:</b>
    /// <list type="bullet">
    ///   <item><c>1</c> - Key was deleted successfully</item>
    ///   <item><c>0</c> - Key doesn't exist or value doesn't match</item>
    /// </list>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Safe distributed lock release (only release if you own the lock)</item>
    ///   <item>Conditional cache invalidation</item>
    /// </list>
    /// </remarks>
    public const string RemoveIfEqual = """
        if redis.call('get', @key) == @expected then
          return redis.call('del', @key)
        else
          return 0
        end
        """;

    /// <summary>
    /// Sets a value only if it's higher than the current value. Creates the key if it doesn't exist.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@value</c> - The new value (as string to preserve precision)</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b> The final stored value as string (new value if updated, existing value if not).
    /// <list type="bullet">
    ///   <item>If key exists and new value &gt; current: returns new value</item>
    ///   <item>If key exists and new value ≤ current: returns current value (unchanged)</item>
    ///   <item>If key doesn't exist: creates key, returns new value</item>
    /// </list>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Tracking maximum values (high scores, peak metrics)</item>
    ///   <item>Monotonic counters that should never decrease</item>
    /// </list>
    /// </remarks>
    public const string SetIfHigher = """
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
        """;

    /// <summary>
    /// Sets a value only if it's lower than the current value. Creates the key if it doesn't exist.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@value</c> - The new value (as string to preserve precision)</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b> The final stored value as string (new value if updated, existing value if not).
    /// <list type="bullet">
    ///   <item>If key exists and new value &lt; current: returns new value</item>
    ///   <item>If key exists and new value ≥ current: returns current value (unchanged)</item>
    ///   <item>If key doesn't exist: creates key, returns new value</item>
    /// </list>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Tracking minimum values (lowest prices, fastest times)</item>
    ///   <item>Water-level markers that track historical lows</item>
    /// </list>
    /// </remarks>
    public const string SetIfLower = """
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
        """;

    /// <summary>
    /// Atomically increments a value and sets expiration in a single operation.
    /// </summary>
    /// <remarks>
    /// <b>Parameters:</b>
    /// <list type="bullet">
    ///   <item><c>@key</c> - The Redis key</item>
    ///   <item><c>@value</c> - The increment amount (positive or negative)</item>
    ///   <item><c>@expires</c> - TTL in milliseconds (empty string = no expiration)</item>
    /// </list>
    /// <b>Returns:</b> The new value after increment as string.
    /// <para>
    /// Automatically uses INCRBY for integers and INCRBYFLOAT for decimals.
    /// Creates the key with value 0 before incrementing if it doesn't exist.
    /// </para>
    /// <b>Use cases:</b>
    /// <list type="bullet">
    ///   <item>Rate limiting counters with automatic expiration</item>
    ///   <item>Rolling window metrics</item>
    ///   <item>Session-scoped counters</item>
    /// </list>
    /// </remarks>
    public const string IncrementWithExpire = """
        if math.modf(@value) == 0 then
          redis.call('incrby', @key, @value)
        else
          redis.call('incrbyfloat', @key, @value)
        end
        if (@expires ~= nil and @expires ~= '') then
          redis.call('pexpire', @key, math.ceil(@expires))
        end
        return redis.call('get', @key)
        """;

    // Reader-set marker suffix shared with the .NET storage layer. Kept inline in each Lua script
    // (no shared constant — Lua scripts don't compose) so a reviewer can see exactly which value
    // counts as a writer-waiting placeholder without cross-referencing the C# code.

    /// <summary>
    /// Atomically acquires a reader lock when no real writer holds the resource and no
    /// writer-waiting marker is queued.
    /// </summary>
    /// <remarks>
    /// Storage shape: <c>@readerKey</c> is a HASH whose fields are reader lockIds and whose values
    /// are per-reader expiry epochs in milliseconds. Per-entry expiry (not Redis key TTL) is the
    /// source of truth for reader liveness; the outer HASH TTL is a generous safety net only.
    /// </remarks>
    public const string TryAcquireReadLock = """
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
        """;

    /// <summary>
    /// Atomically extends a reader lock if the caller's lockId is still present in the reader
    /// HASH and no writer-waiting marker is queued.
    /// </summary>
    /// <remarks>
    /// Refusing to extend while a writer is waiting enforces the writer-preference guarantee: a
    /// reader running <c>Monitoring = AutoExtend</c> sees <c>Renewed=false</c> when a writer
    /// queues, which the provider classifies as <c>Lost</c> and fires <c>HandleLostToken</c>.
    /// </remarks>
    public const string TryExtendReadLock = """
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
        """;

    /// <summary>Atomically releases a reader lock id from the reader HASH.</summary>
    public const string ReleaseReadLock = """
        return redis.call('hdel', @readerKey, @lockId)
        """;

    /// <summary>
    /// Atomically acquires a writer lock when there are no live readers and no other writer, or
    /// plants/refreshes the caller's writer-waiting marker.
    /// </summary>
    /// <remarks>
    /// The plant/refresh branch fires whenever <c>writerValue</c> is missing OR is a string
    /// ending in <c>:_WRITERWAITING</c> (any owner). This makes the marker collectively
    /// continuous under multi-writer contention: a second queued writer keeps the marker present
    /// even if the original queued writer cancels, so readers never sneak in. The marker is
    /// stored with <c>@markerExpires</c> rather than the lease TTL so an abandoned/cancelled
    /// writer cannot block readers for the full lease window.
    /// </remarks>
    public const string TryAcquireWriteLock = """
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
        """;

    /// <summary>Atomically extends a writer lock when the writer key still belongs to the lock id.</summary>
    public const string TryExtendWriteLock = """
        if redis.call('get', @writerKey) ~= @lockId then
          return 0
        end

        if (@expires ~= nil and @expires ~= '') then
          redis.call('pexpire', @writerKey, @expires)
        end

        return 1
        """;

    /// <summary>Atomically releases a writer lock or the caller's writer-waiting marker.</summary>
    public const string ReleaseWriteLock = """
        local current = redis.call('get', @writerKey)
        if current == @lockId or current == @waitingId then
          return redis.call('del', @writerKey)
        end
        return 0
        """;
}
