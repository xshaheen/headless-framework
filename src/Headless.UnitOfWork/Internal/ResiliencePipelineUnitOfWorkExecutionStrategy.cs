// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Polly;
using Polly.Retry;

namespace Headless.UnitOfWork.Internal;

/// <summary>Plugs a Polly retry strategy into the shared <c>RunAsync</c> runner.</summary>
internal sealed class ResiliencePipelineUnitOfWorkExecutionStrategy(ResiliencePipeline pipeline)
    : IUnitOfWorkExecutionStrategy
{
    /// <summary>Builds the strategy; Polly validates <paramref name="retry" /> here and throws when it is invalid.</summary>
    public static ResiliencePipelineUnitOfWorkExecutionStrategy Create(RetryStrategyOptions retry)
    {
        return new(new ResiliencePipelineBuilder().AddRetry(retry).Build());
    }

    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> attempt,
        CancellationToken cancellationToken
    )
    {
        return pipeline
            .ExecuteAsync(
                static async (attempt, ct) => await attempt(ct).ConfigureAwait(false),
                attempt,
                cancellationToken
            )
            .AsTask();
    }
}
