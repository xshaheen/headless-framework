// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork.Internal;

/// <summary>The strategy of a <c>RunAsync</c> that never replays: the attempt runs once.</summary>
internal sealed class NoReplayUnitOfWorkExecutionStrategy : IUnitOfWorkExecutionStrategy
{
    public static readonly NoReplayUnitOfWorkExecutionStrategy Instance = new();

    private NoReplayUnitOfWorkExecutionStrategy() { }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> attempt,
        CancellationToken cancellationToken
    )
    {
        return attempt(cancellationToken);
    }
}
