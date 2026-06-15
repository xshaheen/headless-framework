// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.CommitCoordination;
using Microsoft.Extensions.DependencyInjection;
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
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        var ownedServices = bindings.Services.CreateAsyncScope();
        ICommitScope scope;

        try
        {
            scope = scopeFactory.Begin(ownedServices.ServiceProvider, bindings.Capabilities);
        }
        catch
        {
            ownedServices.Dispose();
            throw;
        }

        if (bindings.ProviderTransactionKey is not null)
        {
            // Remove-if-equal: a tracked scope only ever evicts its OWN entry. After a commit drain removes the
            // entry and a later transaction reuses the same key, this scope's disposal must not evict the successor.
            var trackedScope = new TrackedCommitScope(
                scope,
                self =>
                    _scopes.TryRemove(new KeyValuePair<object, ICommitScope>(bindings.ProviderTransactionKey, self)),
                ownedServices
            );

            if (!_scopes.TryAdd(bindings.ProviderTransactionKey, trackedScope))
            {
                trackedScope.Dispose();
                LogDuplicateScope(_logger, bindings.ProviderTransactionKey);

                throw new InvalidOperationException(
                    "A SQL Server commit coordination scope is already attached for this provider transaction key."
                );
            }

            scope = trackedScope;
        }
        else
        {
            scope = new TrackedCommitScope(scope, static _ => { }, ownedServices);
        }

        return scope;
    }

    /// <summary>
    /// Signals a commit for a previously attached provider transaction key.
    /// </summary>
    /// <remarks>
    /// The diagnostic observer fires for every SqlClient transaction edge, most of which are not coordinated; an
    /// absent key is the normal case and is silently ignored (never a warning).
    /// </remarks>
    /// <param name="providerTransactionKey">The provider transaction key (the connection's <c>ClientConnectionId</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signal task.</returns>
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

    /// <summary>
    /// Signals a rollback for a previously attached provider transaction key.
    /// </summary>
    /// <param name="providerTransactionKey">The provider transaction key (the connection's <c>ClientConnectionId</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The signal task.</returns>
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

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A SQL Server commit coordination scope is already attached for provider transaction key {ProviderTransactionKey}."
    )]
    private static partial void LogDuplicateScope(ILogger logger, object providerTransactionKey);
}
