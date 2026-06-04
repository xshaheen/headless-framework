// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// A handle on the connection-death monitoring performed by <see cref="ConnectionMonitor"/>. While the handle is
/// held, the monitor keeps probing the underlying connection; <see cref="ConnectionLostToken"/> is cancelled if the
/// connection is observed to have died. Disposing the handle releases the monitoring registration.
/// </summary>
internal interface IDatabaseConnectionMonitoringHandle : IDisposable
{
    /// <summary>Cancelled when the monitored connection is observed to be lost (closed or silently dead).</summary>
    CancellationToken ConnectionLostToken { get; }
}
