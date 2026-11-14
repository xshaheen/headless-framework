// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// Optional built-in storage capability for atomically deferring a claimed received retry that cannot run yet, because
/// its consumer's circuit is open or its rate limit has no permit, while releasing that exact claimed lease generation.
/// </summary>
/// <remarks>
/// Implementations update only <c>NextRetryAt</c> (store now plus the delay) and release the row lease. The update must
/// use store-authoritative lease validity and compare the complete identity; a stale generation is a successful no-op from Core's perspective and returns
/// <see langword="false"/>.
/// </remarks>
internal interface ICircuitRetryDeferralStorage
{
    ValueTask<bool> DeferReceivedRetryAsync(
        CircuitRetryDeferral deferral,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Identifies one exact received-retry lease generation and how long until it may be claimed again, a delay the store
/// adds to its own clock.
/// </summary>
internal readonly record struct CircuitRetryDeferral(MessageLeaseIdentity Identity, TimeSpan Delay);
