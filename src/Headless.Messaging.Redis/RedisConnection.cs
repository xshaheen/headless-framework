// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Messaging.Redis;

/// <summary>
/// Wraps an established <c>IConnectionMultiplexer</c> with a capacity counter and owns its lifetime.
/// </summary>
internal sealed class RedisConnection(IConnectionMultiplexer connection) : IDisposable
{
    private bool _isDisposed;

    /// <summary>The underlying StackExchange.Redis connection multiplexer.</summary>
    public IConnectionMultiplexer Connection { get; } = Argument.IsNotNull(connection);

    /// <summary>
    /// The number of outstanding (in-flight) commands on this connection, used by the pool
    /// to select the least-loaded connection.
    /// </summary>
    public long ConnectionCapacity => Connection.GetCounters().TotalOutstanding;

    /// <inheritdoc/>
    public void Dispose()
    {
        _Dispose(disposing: true);
    }

    private void _Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            Connection.Dispose();
        }

        _isDisposed = true;
    }
}
