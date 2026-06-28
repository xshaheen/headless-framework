// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically acquires a reader lock when no writer holds the resource.</summary>
internal sealed class TryAcquireReadLockScriptDefinition : RedisScriptDefinition
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

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>
/// Parameters shared by the read-lock scripts (<see cref="TryAcquireReadLockScriptDefinition"/>,
/// <see cref="TryExtendReadLockScriptDefinition"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReaderWriterReadParams(
    RedisKey WriterKey,
    RedisKey ReaderKey,
    string LeaseId,
    RedisValue Expires
);
#pragma warning restore IDE1006
