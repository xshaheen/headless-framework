// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// Plugs a context's EF Core execution strategy into the shared <c>RunAsync</c> runner. An adapter, never a
/// reimplementation: the host may have configured <c>EnableRetryOnFailure</c> (or a custom strategy) with its own
/// retry count, delay, and transient classification, and replay must honor exactly that configuration.
/// </summary>
internal sealed class EfUnitOfWorkExecutionStrategy(IExecutionStrategy strategy) : IUnitOfWorkExecutionStrategy
{
    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> attempt,
        CancellationToken cancellationToken
    )
    {
        return strategy.ExecuteAsync(attempt, static (attempt, ct) => attempt(ct), cancellationToken);
    }
}
