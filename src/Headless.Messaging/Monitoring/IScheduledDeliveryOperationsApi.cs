// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Messaging;

/// <summary>Provider-neutral, payload-free scheduled delivery administration surface.</summary>
[PublicAPI]
public interface IScheduledDeliveryOperationsApi
{
    /// <summary>Queries pending scheduled deliveries without loading message payloads or arbitrary headers.</summary>
    /// <remarks>A read: <paramref name="authorization"/> needs an authenticated principal, not a named operator.</remarks>
    /// <exception cref="UnauthorizedAccessException">The principal is not authenticated.</exception>
    ValueTask<IndexPage<ScheduledDeliveryView>> QueryAsync(
        ScheduledDeliveryQuery query,
        OperatorAuthorizationContext authorization,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Counts the pending scheduled deliveries <paramref name="query"/> matches, stopping at <paramref name="cap"/>;
    /// its paging members are ignored.
    /// </summary>
    /// <remarks>A read: <paramref name="authorization"/> needs an authenticated principal, not a named operator.</remarks>
    /// <exception cref="UnauthorizedAccessException">The principal is not authenticated.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cap"/> is not positive.</exception>
    ValueTask<MessageCount> CountAsync(
        ScheduledDeliveryQuery query,
        OperatorAuthorizationContext authorization,
        int cap = MessageCount.DefaultCap,
        CancellationToken cancellationToken = default
    );

    /// <summary>Revokes a pending scheduled delivery before delivery attempt reservation.</summary>
    ValueTask<ScheduledDeliveryOperationResult> RevokeAsync(
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Dispatches a pending scheduled delivery immediately by advancing its schedule to the store clock.</summary>
    ValueTask<ScheduledDeliveryOperationResult> DispatchNowAsync(
        ScheduledDeliveryOperationRequest request,
        CancellationToken cancellationToken = default
    );
}
