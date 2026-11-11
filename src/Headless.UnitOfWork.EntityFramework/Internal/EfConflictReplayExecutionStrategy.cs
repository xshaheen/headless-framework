// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Polly;
using Polly.Retry;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The replay loop of a <c>RunAsync(db, …)</c> call that passed a retry policy of its own: the policy's loop runs
/// inside the context's execution strategy, so a fault the policy handles (a lost optimistic-concurrency check)
/// replays without leaving the strategy, and any other fault reaches the strategy as it would without the policy.
/// </summary>
/// <remarks>
/// The strategy runs the policy's loop again for each transient fault it retries, and a loop that started over
/// with a full budget each time would multiply the two limits. The replays the policy starts are therefore counted
/// across the whole call: once <see cref="RetryStrategyOptions{TResult}.MaxRetryAttempts" /> of them have run, an
/// attempt's fault is carried past the policy and rethrown to the strategy, so the block runs at most once, plus
/// the policy's replays, plus the strategy's retries. One instance serves one call.
/// </remarks>
internal sealed class EfConflictReplayExecutionStrategy(IExecutionStrategy strategy, RetryStrategyOptions retry)
    : IUnitOfWorkExecutionStrategy
{
    // Polly validates the options here and throws when they are invalid. Its delays wait real time: a back-off
    // between two writers has nothing to do with the times the application records.
    private readonly ResiliencePipeline _pipeline = new ResiliencePipelineBuilder().AddRetry(retry).Build();
    private int _replays;

    public Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> attempt,
        CancellationToken cancellationToken
    )
    {
        return strategy.ExecuteAsync(
            (Self: this, Attempt: attempt),
            static (state, ct) => state.Self._ExecutePolicyAsync(state.Attempt, ct),
            cancellationToken
        );
    }

    private async Task<TResult> _ExecutePolicyAsync<TResult>(
        Func<CancellationToken, Task<TResult>> attempt,
        CancellationToken cancellationToken
    )
    {
        var replaying = false;

        var (result, pastBudget) = await _pipeline
            .ExecuteAsync<(TResult Result, ExceptionDispatchInfo? PastBudget)>(
                async ct =>
                {
                    if (replaying)
                    {
                        _replays++;
                    }

                    replaying = true;

                    try
                    {
                        return (await attempt(ct).ConfigureAwait(false), null);
                    }
                    catch (Exception ex) when (_replays >= retry.MaxRetryAttempts)
                    {
                        return (default!, ExceptionDispatchInfo.Capture(ex));
                    }
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        pastBudget?.Throw();

        return result;
    }
}
