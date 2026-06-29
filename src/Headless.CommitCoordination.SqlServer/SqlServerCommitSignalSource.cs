// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Correlates SQL Server diagnostic commit signals to commit coordination scopes, keyed by the connection's
/// <c>ClientConnectionId</c>. The out-of-band <see cref="SqlServerCommitDiagnosticObserver" /> turns the native
/// SqlClient commit/rollback edge into a signal here.
/// </summary>
/// <remarks>
/// The diagnostic signal that drives this source is an acceleration hook, not the durability mechanism. If a commit
/// signal is missed, delayed, or disabled, deferred work must still be recoverable through the consumer's durable
/// store and polling recovery (e.g. messaging's outbox rows committed in-transaction plus its retry sweep); this
/// source only dispatches that work sooner. Correctness must never depend on the signal firing.
/// <para>
/// <b>Correlation boundary (known limitation).</b> Scopes are keyed by <c>ClientConnectionId</c>, and the signal path
/// removes the entry by key (the dispose path is remove-if-equal). A live duplicate scope per key cannot occur — the
/// <c>Attach</c> guard throws on a second live scope for the same key. The residual window is theoretical: a stale or
/// duplicated commit diagnostic for an already-drained transaction, arriving after a successor reused the same pooled
/// <c>ClientConnectionId</c>, could signal the successor early. Because the signal is acceleration-only and the
/// successor's own durable rows govern correctness, the impact is bounded to early dispatch on the relay-recoverable
/// path, never lost or duplicated durable work. Hardening this fully (a per-attach generation token in the key) is
/// deferred — see plan decision D13.
/// </para>
/// </remarks>
[PublicAPI]
public sealed partial class SqlServerCommitSignalSource(
    ICommitScopeFactory scopeFactory,
    ILogger<SqlServerCommitSignalSource>? logger = null
) : ICommitSignalSource
{
    private readonly ILogger _logger = logger ?? NullLogger<SqlServerCommitSignalSource>.Instance;
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
                    "A SQL Server commit coordination scope is already attached for this provider transaction key."
                );
            },
            cancellationToken
        );

    /// <summary>
    /// Signals a commit for a previously attached scope, draining its registered
    /// <see cref="ICommitCoordinator.OnCommit" /> callbacks.
    /// </summary>
    /// <remarks>
    /// The diagnostic observer fires for every SqlClient transaction edge, most of which are not coordinated; an
    /// absent key is the normal case and is silently ignored (never a warning). The terminal claim is made
    /// synchronously before returning so a racing disposal cannot observe the scope as un-signalled and drain it
    /// as a rollback.
    /// </remarks>
    /// <param name="providerTransactionKey">
    /// The provider transaction key — the <c>ClientConnectionId</c> of the connection passed to
    /// <c>SqlConnection.EnlistCommitCoordination</c>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the drain has finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerTransactionKey" /> is <see langword="null" />.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The enlisting caller owns the scope lifetime and disposes it; the signal source signals and drains only, never disposing or popping the ambient frame."
    )]
    public ValueTask SignalCommittedAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(providerTransactionKey);

        // Fast path: the diagnostic observer fires for EVERY SqlClient transaction, most uncoordinated. When no
        // scope is attached for this key, return a synchronously-completed ValueTask — no async state machine, no
        // allocation on the hot diagnostic thread. The terminal claim inside scope.SignalAsync still settles
        // synchronously (TryRemove ran first, on this thread) before the returned drain is observed off-thread.
        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            return ValueTask.CompletedTask;
        }

        // Signal and drain only — never dispose or pop the ambient frame. The enlisting caller owns the scope's
        // lifetime (via its own using) and pops the ambient frame synchronously in its own frame on disposal.
        return scope.SignalAsync(CommitOutcome.Committed);
    }

    /// <summary>
    /// Signals a rollback for a previously attached scope, draining its registered
    /// <see cref="ICommitCoordinator.OnRollback" /> callbacks.
    /// </summary>
    /// <param name="providerTransactionKey">
    /// The provider transaction key — the <c>ClientConnectionId</c> of the connection passed to
    /// <c>SqlConnection.EnlistCommitCoordination</c>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the drain has finished.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="providerTransactionKey" /> is <see langword="null" />.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The enlisting caller owns the scope lifetime and disposes it; the signal source signals and drains only, never disposing or popping the ambient frame."
    )]
    public ValueTask SignalRolledBackAsync(object providerTransactionKey, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(providerTransactionKey);

        // Fast path: see SignalCommittedAsync — synchronously-completed ValueTask for the common uncoordinated key.
        if (!_scopes.TryRemove(providerTransactionKey, out var scope))
        {
            return ValueTask.CompletedTask;
        }

        // Signal and drain only — never dispose or pop the ambient frame (the enlisting caller owns scope lifetime).
        return scope.SignalAsync(CommitOutcome.RolledBack);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A SQL Server commit coordination scope is already attached for provider transaction key {ProviderTransactionKey}."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogDuplicateScope(ILogger logger, object providerTransactionKey);
}
