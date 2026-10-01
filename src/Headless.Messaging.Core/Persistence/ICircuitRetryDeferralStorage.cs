// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// Identifies one exact received-retry lease generation and how long until the circuit lets it be claimed again,
/// a delay the store adds to its own clock.
/// </summary>
internal readonly record struct CircuitRetryDeferral(MessageLeaseIdentity Identity, TimeSpan Delay);

/// <summary>
/// Optional built-in storage capability for atomically deferring a circuit-open received retry
/// while releasing that exact claimed lease generation.
/// </summary>
/// <remarks>
/// Implementations update only <c>NextRetryAt</c> (store now plus the delay), <c>Owner</c>, and
/// <c>LockedUntil</c>. The update must use store-authoritative lease validity and compare the complete
/// identity; a stale generation is a successful no-op from Core's perspective and returns
/// <see langword="false"/>.
/// </remarks>
internal interface ICircuitRetryDeferralStorage
{
    ValueTask<bool> DeferReceivedRetryAsync(
        CircuitRetryDeferral deferral,
        CancellationToken cancellationToken = default
    );
}
