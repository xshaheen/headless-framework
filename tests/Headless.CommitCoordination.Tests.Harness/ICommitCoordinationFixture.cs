// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Tests;

public interface ICommitCoordinationFixture
{
    ValueTask<ICommitScope> BeginScopeAsync(CancellationToken cancellationToken);
}
