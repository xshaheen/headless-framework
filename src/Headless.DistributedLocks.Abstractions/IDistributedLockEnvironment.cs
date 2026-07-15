// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// The ambient state every lock provider exposes: its clock, its logger, and the defaults it applies when an acquire
/// call leaves them unspecified. Implemented by <see cref="IDistributedLock"/>, <see cref="IDistributedReadWriteLock"/>,
/// and <see cref="IDistributedSemaphoreProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// This exists because provider-agnostic coordinators need exactly these four values and nothing else. Composite
/// acquisition is the one in-tree consumer: it needs a clock for the whole-set deadline, the per-child budget, and the
/// renewal cadence, and a logger because a composite's disposal must swallow-and-log rather than throw. Naming that
/// requirement as a contract is what lets one coordinator serve all three primitives.
/// </para>
/// <para>
/// Providers implement this by republishing dependencies they already hold; it is not an extra burden. A provider with
/// no logger of its own returns <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance"/>.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDistributedLockEnvironment
{
    /// <summary>
    /// Gets the clock used by this provider for deadlines, elapsed-time measurement, and scheduled waits.
    /// Provider-agnostic coordinators must use this instance so their timing remains aligned with the
    /// provider and deterministic under test.
    /// </summary>
    /// <remarks>
    /// This schedules work; it does not arbitrate expiry. A handle is valid only while the backend says so — the
    /// clock decides when to ask, never whether ownership still holds.
    /// </remarks>
    TimeProvider TimeProvider { get; }

    /// <summary>
    /// Gets the logger used by this provider. Provider-agnostic coordinators log through this instance so their
    /// diagnostics land in the same sink as the provider's own.
    /// </summary>
    /// <remarks>
    /// Required because disposal must never throw: a handle that fails to release during <c>DisposeAsync</c> has to
    /// report that failure somewhere, and an exception would replace whatever the caller's <see langword="using"/> body was
    /// already throwing. Return <see cref="Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance"/> when a
    /// provider has no logger of its own.
    /// </remarks>
    ILogger Logger { get; }

    /// <summary>
    /// Default lease duration applied when <see cref="DistributedLockAcquireOptions.TimeUntilExpires"/> is not
    /// specified on an acquire call. Implementations refresh the handle in storage at this cadence when
    /// <see cref="LockMonitoringMode.AutoExtend"/> is enabled.
    /// </summary>
    TimeSpan DefaultTimeUntilExpires { get; }

    /// <summary>
    /// Default upper bound applied to acquire attempts when
    /// <see cref="DistributedLockAcquireOptions.AcquireTimeout"/> is not specified. After the timeout
    /// elapses, the acquire returns <see langword="null"/> (try variants) or throws
    /// <see cref="LockAcquisitionTimeoutException"/> (acquire variants).
    /// </summary>
    TimeSpan DefaultAcquireTimeout { get; }
}
