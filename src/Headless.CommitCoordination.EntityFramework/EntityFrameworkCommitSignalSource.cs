// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Correlates EF Core transaction commit and rollback edges — as reported by
/// <see cref="CommitCoordinationTransactionInterceptor" /> — to the commit coordination scopes opened via
/// <c>DatabaseFacade.EnlistCommitCoordination</c>.
/// </summary>
/// <remarks>
/// Scopes are keyed by the underlying <c>DbTransaction</c> instance. When the interceptor fires,
/// <see cref="SignalCommittedAsync" /> or <see cref="SignalRolledBackAsync" /> removes the scope by key and
/// drains its registered callbacks. An absent key means the transaction was never enrolled in commit
/// coordination — this is the normal case for uncoordinated transactions and is silently ignored.
/// </remarks>
[PublicAPI]
public sealed partial class EntityFrameworkCommitSignalSource(
    ICommitScopeFactory scopeFactory,
    ILogger<EntityFrameworkCommitSignalSource>? logger = null
) : ICommitSignalSource
{
    private readonly ILogger _logger = logger ?? NullLogger<EntityFrameworkCommitSignalSource>.Instance;
    private readonly ConcurrentDictionary<object, ICommitScope> _scopes = new();

    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken) =>
        CommitSignalSourceAttach.Attach(
            scopeFactory,
            bindings,
            _scopes,
            key =>
            {
                LogDuplicateScope(_logger, key);

                return new InvalidOperationException(
                    "An EF Core commit coordination scope is already attached for this provider transaction key."
                );
            },
            cancellationToken
        );

    /// <summary>
    /// Signals a commit for the scope correlated to the given transaction, draining its registered
    /// <see cref="ICommitCoordinator.OnCommit" /> callbacks.
    /// </summary>
    /// <remarks>
    /// The interceptor fires for every EF transaction, most of which are not coordinated; an absent key is the
    /// normal case and is silently ignored without a warning.
    /// </remarks>
    /// <param name="providerTransactionKey">
    /// The transaction correlation key — the <c>DbTransaction</c> instance passed to the interceptor.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the drain has finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerTransactionKey" /> is <see langword="null" />.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The enlisting caller owns the scope lifetime and disposes it; the signal source signals and drains only, never disposing or popping the ambient frame."
    )]
    public async ValueTask SignalCommittedAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(providerTransactionKey);

        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            return;
        }

        // Signal and drain only — never dispose or pop the ambient frame. The enlisting caller owns the scope's
        // lifetime (via its own using) and pops the ambient frame synchronously in its own frame on disposal.
        await scope.SignalAsync(CommitOutcome.Committed).ConfigureAwait(false);
    }

    internal void SignalCommittedInBackground(object providerTransactionKey)
    {
        Argument.IsNotNull(providerTransactionKey);

        // Invoke synchronously (NOT via Task.Run): the terminal claim — TryRemove plus the scope's CAS — runs on the
        // commit thread, settling before this returns and before the caller's un-signalled scope dispose can race it.
        // Only the drain (the awaited continuation, ConfigureAwait(false)) runs off-thread. Wrapping the whole call in
        // Task.Run would defer the claim itself, letting a racing Dispose claim RolledBack and discard committed work.
        _SignalInBackground(SignalCommittedAsync(providerTransactionKey, CancellationToken.None).AsTask());
    }

    /// <summary>
    /// Signals a rollback for the scope correlated to the given transaction, draining its registered
    /// <see cref="ICommitCoordinator.OnRollback" /> callbacks.
    /// </summary>
    /// <param name="providerTransactionKey">
    /// The transaction correlation key — the <c>DbTransaction</c> instance passed to the interceptor.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the drain has finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerTransactionKey" /> is <see langword="null" />.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The enlisting caller owns the scope lifetime and disposes it; the signal source signals and drains only, never disposing or popping the ambient frame."
    )]
    public async ValueTask SignalRolledBackAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(providerTransactionKey);

        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            return;
        }

        // Signal and drain only — never dispose or pop the ambient frame (the enlisting caller owns scope lifetime).
        await scope.SignalAsync(CommitOutcome.RolledBack).ConfigureAwait(false);
    }

    internal void SignalRolledBackInBackground(object providerTransactionKey)
    {
        Argument.IsNotNull(providerTransactionKey);

        // Invoke synchronously (NOT via Task.Run) so the terminal claim settles on the commit thread; see the note on
        // SignalCommittedInBackground. Only the drain runs off-thread.
        _SignalInBackground(SignalRolledBackAsync(providerTransactionKey, CancellationToken.None).AsTask());
    }

    // The claim settles synchronously on the commit thread (see SignalCommittedInBackground); only the drain runs
    // off-thread here, fire-and-forget with no shutdown drain gate — deliberately, and unlike
    // SqlServerCommitDiagnosticHostedService, which tracks _drains and awaits WaitForDrainsAsync on stop.
    // The asymmetry is safe because the drain is acceleration, not correctness: the outbox/durable rows were
    // already written in the committed transaction, so an in-flight drain abandoned by an abrupt host stop is
    // recovered by the relay/polling sweep — it degrades dispatch latency, never durability. A tracking gate
    // here would only shorten the post-commit dispatch window on graceful shutdown; it is not required for
    // correctness.
    private void _SignalInBackground(Task signal)
    {
        _ = signal.ContinueWith(
            static (t, state) => LogBackgroundSignalFaulted((ILogger)state!, t.Exception),
            _logger,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "An EF Core commit coordination scope is already attached for provider transaction key {ProviderTransactionKey}."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDuplicateScope(ILogger logger, object providerTransactionKey);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "An EF Core commit coordination background signal faulted."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogBackgroundSignalFaulted(ILogger logger, Exception? exception);
}
