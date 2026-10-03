// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The replay loop a <c>RunAsync</c> attempt runs inside. The strategy decides only whether and when a faulted
/// attempt runs again; <see cref="UnitOfWorkRunner" /> owns what an attempt is and which faults may reach the
/// strategy at all, so every provider applies the same refusal rules whatever loop it plugs in.
/// </summary>
/// <remarks>
/// A provider adapts its own loop rather than reimplementing one: EF Core wraps the context's
/// <c>IExecutionStrategy</c>, because the host may have configured <c>EnableRetryOnFailure</c> with its own
/// settings. An attempt that must not replay never throws into the strategy; the runner captures the fault and
/// rethrows it after <see cref="ExecuteAsync{TResult}" /> returns.
/// </remarks>
internal interface IUnitOfWorkExecutionStrategy
{
    /// <summary>
    /// Runs <paramref name="attempt" />, running it again for each fault the strategy classifies as retryable,
    /// and returns the first successful result or rethrows the last fault.
    /// </summary>
    /// <param name="attempt">One whole attempt: begin, block, and complete on a fresh unit of work.</param>
    /// <param name="cancellationToken">Forwarded to every attempt and to the strategy's delays.</param>
    Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> attempt,
        CancellationToken cancellationToken
    );
}
