// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Headless.Messaging.Transactions;

internal sealed class NullCurrentCommitCoordinator : ICurrentCommitCoordinator
{
    public ICommitCoordinator? Current => null;
}
