// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

/// <summary>
/// Attaches the messaging outbox to every unit of work in a host that called <c>AddHeadlessMessaging</c>.
/// </summary>
internal sealed class UnitOfWorkOutboxFeatureProvider(MessagePublisher publisher) : IUnitOfWorkFeatureProvider
{
    public Type FeatureType => typeof(IUnitOfWorkOutbox);

    /// <summary>
    /// Creates the capability. The unit is deliberately unused: the capability is cached for the unit's whole
    /// life and must hold no handle, so each publish takes the caller's own handle as an argument instead.
    /// </summary>
    public object Create(IUnitOfWork unitOfWork) => new UnitOfWorkOutboxFeature(publisher);
}
