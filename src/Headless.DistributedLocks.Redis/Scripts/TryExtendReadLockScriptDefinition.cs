// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically extends a reader lock if the caller's lock id is still present.</summary>
internal sealed class TryExtendReadLockScriptDefinition : RedisScriptDefinition
{
    public static TryExtendReadLockScriptDefinition Instance { get; } = new();

    private TryExtendReadLockScriptDefinition()
        : base(
            """
            local existing = redis.call('hget', @readerKey, @leaseId)
            if existing == false then
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

            -- A lease that already passed is not renewed, even before an acquire prunes it, as in the in-memory
            -- provider. '0' marks an infinite reader.
            local existingExpiry = tonumber(existing)
            if existingExpiry ~= 0 and existingExpiry <= nowMs then
              return 0
            end

            if (@expires ~= nil and @expires ~= '') then
              local expiryMs = nowMs + tonumber(@expires)
              -- Never shorten a live lease, and never turn an infinite reader finite: a shorter extend keeps the
              -- later expiry, matching the in-memory provider and the semaphore script.
              if existingExpiry ~= 0 and expiryMs > existingExpiry then
                redis.call('hset', @readerKey, @leaseId, tostring(expiryMs))
              end
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
