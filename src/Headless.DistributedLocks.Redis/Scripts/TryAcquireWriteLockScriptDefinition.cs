// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically acquires a writer lock or plants the caller's writer-waiting marker.</summary>
internal sealed class TryAcquireWriteLockScriptDefinition : RedisScriptDefinition
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

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>Parameters for <see cref="TryAcquireWriteLockScriptDefinition"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReaderWriterWriteParams(
    RedisKey writerKey,
    RedisKey readerKey,
    string leaseId,
    string waitingId,
    RedisValue expires,
    RedisValue markerExpires
);
#pragma warning restore IDE1006
