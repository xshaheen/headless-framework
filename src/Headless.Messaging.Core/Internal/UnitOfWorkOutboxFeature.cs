// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

/// <summary>
/// The enlisted publish surface behind <c>unit.Outbox</c>: every call runs the publisher with the caller's own
/// handle and coordination required, so the durable row joins that unit's transaction or the call throws.
/// </summary>
/// <remarks>
/// A singleton resolved from the unit's scope, so it holds no handle of its own: the caller's handle arrives as
/// an argument, and the outbox writer's first registration on it is what refuses a handle that can no longer
/// carry work — before any storage effect.
/// </remarks>
internal sealed class UnitOfWorkOutboxFeature(MessagePublisher publisher) : IUnitOfWorkOutbox
{
    public Task<PublishReceipt> PublishAsync<T>(
        IUnitOfWork unitOfWork,
        T? contentObj,
        OutboxOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);

        return publisher.PublishAsync(
            MessageLane.Bus,
            contentObj,
            options,
            unitOfWork,
            requireCoordination: true,
            cancellationToken
        );
    }

    public Task<PublishReceipt> EnqueueAsync<T>(
        IUnitOfWork unitOfWork,
        T? contentObj,
        OutboxOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(unitOfWork);

        return publisher.PublishAsync(
            MessageLane.Queue,
            contentObj,
            options,
            unitOfWork,
            requireCoordination: true,
            cancellationToken
        );
    }
}
