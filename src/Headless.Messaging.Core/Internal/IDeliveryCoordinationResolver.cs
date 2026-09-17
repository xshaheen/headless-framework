// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;

namespace Headless.Messaging.Internal;

internal interface IDeliveryCoordinationResolver
{
    /// <summary>
    /// Evaluates whether <paramref name="unitOfWork" /> is a compatible enlistment target for this storage.
    /// Only called when a unit of work is active; a resource-less unit is not automatically incompatible —
    /// a relational storage treats it the same as "no unit of work" (<see cref="DeliveryCoordination.None" />),
    /// while a storage that captures rows on the unit itself (in-memory) is compatible with it directly.
    /// </summary>
    DeliveryCoordination Resolve(IUnitOfWork unitOfWork);
}
