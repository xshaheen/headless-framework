// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork.Internal;
using Polly;
using Polly.Retry;

namespace Headless.UnitOfWork;

/// <summary>
/// The host's default replay policy for the raw-ADO <c>RunAsync</c> overloads that open a connection per attempt:
/// <c>RunAsync(NpgsqlDataSource, …)</c> and <c>RunAsync(Func&lt;CancellationToken, ValueTask&lt;SqlConnection&gt;&gt;, …)</c>.
/// Configure it with <c>services.Configure&lt;UnitOfWorkRetryOptions&gt;(…)</c>; a call that passes its own
/// <see cref="RetryStrategyOptions" /> uses that instead.
/// </summary>
/// <remarks>
/// <para>
/// Replay is off by default. Turning it on changes failure semantics for every block that runs through those
/// overloads, and a block that performs a non-transactional effect (an HTTP call, a file write) repeats it on
/// every attempt.
/// </para>
/// <para>
/// A replay re-runs the whole block on a fresh connection, transaction, and unit. Whatever the strategy classifies,
/// the runner never replays a block whose commit started — the commit may have succeeded on the server before it
/// failed on the wire — or one that called <see cref="IUnitOfWork.PreventRetry" />; those faults surface to the
/// caller.
/// </para>
/// <para>
/// EF Core ignores these options: <c>RunAsync(DbContext, …)</c> replays through the context's own execution
/// strategy (<c>EnableRetryOnFailure</c>). The overloads that take a caller-owned connection never replay.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class UnitOfWorkRetryOptions
{
    /// <summary>
    /// Gets the default replay classification: replay a relational failure the driver or the database reports as
    /// transient — a dropped connection, a serialization failure (SQLSTATE <c>40001</c>, SQL Server <c>3960</c>),
    /// or a deadlock (<c>40P01</c>, SQL Server <c>1205</c>) — and never a cancellation. Reuse (or compose) this
    /// predicate when supplying a <see cref="RetryStrategy" /> so a custom strategy keeps the framework's
    /// classification; Polly's own default replays every exception that is not a cancellation.
    /// </summary>
    public static Func<RetryPredicateArguments<object>, ValueTask<bool>> DefaultShouldHandle { get; } =
        static args =>
            ValueTask.FromResult(
                args.Outcome.Exception is { } exception
                    && RelationalTransientFaults.IsTransient(exception, args.Context.CancellationToken)
            );

    /// <summary>
    /// Gets or sets the Polly retry strategy that decides whether, how often, and after what delay a faulted
    /// attempt replays. <see langword="null" /> (the default) disables replay. Set
    /// <see cref="RetryStrategyOptions{TResult}.ShouldHandle" /> to <see cref="DefaultShouldHandle" />, or a
    /// predicate composed with it.
    /// </summary>
    public RetryStrategyOptions? RetryStrategy { get; set; }
}
