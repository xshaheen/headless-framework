// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Messaging.Monitoring;

/// <summary>Provider-neutral, payload-free scheduled delivery administration surface.</summary>
[PublicAPI]
public interface IScheduledDeliveryOperationsApi
{
    /// <summary>Queries pending scheduled deliveries without loading message payloads or arbitrary headers.</summary>
    ValueTask<IndexPage<ScheduledDeliveryView>> QueryAsync(
        ScheduledDeliveryQuery query,
        OperatorAuthorizationContext authorization,
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
