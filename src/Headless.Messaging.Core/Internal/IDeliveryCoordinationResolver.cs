// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Headless.Messaging.Internal;

internal interface IDeliveryCoordinationResolver
{
    DeliveryCoordination Resolve(ICommitCoordinator coordinator);
}
