// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Redis;
using StackExchange.Redis;

namespace Headless.DistributedLocks.Redis.Scripts;

/// <summary>Atomically extends a writer lock when the writer key still belongs to the lock id.</summary>
internal sealed class TryExtendWriteLockScriptDefinition : RedisScriptDefinition
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

#pragma warning disable IDE1006 // camelCase mirrors the Lua @param token names
/// <summary>
/// Parameters shared by the writer-only scripts (<see cref="TryExtendWriteLockScriptDefinition"/>,
/// <see cref="ReleaseWriteLockScriptDefinition"/>).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ReaderWriterWriterOnlyParams(
    RedisKey writerKey,
    string leaseId,
    string waitingId,
    RedisValue expires
);
#pragma warning restore IDE1006
