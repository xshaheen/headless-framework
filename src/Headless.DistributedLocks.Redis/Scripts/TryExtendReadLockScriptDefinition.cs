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
