// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

/// <summary>
/// The enlisted publish surface behind <c>unit.Outbox</c>: every call runs the publisher with the caller's own
/// handle and coordination required, so the durable row joins that unit's transaction or the call throws.
/// </summary>
/// <remarks>
/// One instance is cached per unit of work and shared by every handle over it, so it holds no handle of its own
/// — a child view can complete while the unit stays active, and a captured child would be dead for the root's
/// later publishes.
/// </remarks>
internal sealed class UnitOfWorkOutboxFeature(MessagePublisher publisher) : IUnitOfWorkOutbox
{
    public Task<PublishReceipt> PublishAsync<T>(
        IUnitOfWork unitOfWork,
        T? contentObj,
        OutboxPublishOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureUsable(unitOfWork);

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
        OutboxQueueOptions? options,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureUsable(unitOfWork);

        return publisher.PublishAsync(
            MessageLane.Queue,
            contentObj,
            options,
            unitOfWork,
            requireCoordination: true,
            cancellationToken
        );
    }

    private static void _EnsureUsable(IUnitOfWork unitOfWork)
    {
        Argument.IsNotNull(unitOfWork);

        // Per publish, and asked of the handle rather than of the unit: a binding can be held in a local past
        // its own view's completion, and State forwards to the root, so a completed child still reports Active.
        // The check has to run before the write, because the writer stores the row before attaching the buffer
        // that dispatches it — a late refusal would leave a stored row nothing ever sends.
        unitOfWork.ThrowIfUnusable();
    }
}
