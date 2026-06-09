// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Headless.CommitCoordination;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.SqlServer;

/// <summary>
/// Correlates SQL Server diagnostic commit signals to commit coordination scopes, keyed by the connection's
/// <c>ClientConnectionId</c>. The out-of-band <see cref="SqlServerCommitDiagnosticObserver" /> turns the native
/// SqlClient commit/rollback edge into a signal here.
/// </summary>
[PublicAPI]
public sealed partial class SqlServerCommitSignalSource(
    CommitScopeFactory scopeFactory,
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

        var scope = scopeFactory.Begin(bindings.Services, bindings.Capabilities);

        if (bindings.ProviderTransactionKey is not null)
        {
            // Remove-if-equal: a tracked scope only ever evicts its OWN entry. After a commit drain removes the
            // entry and a later transaction reuses the same key, this scope's disposal must not evict the successor.
            var trackedScope = new TrackedCommitScope(
                scope,
                self => _scopes.TryRemove(new KeyValuePair<object, ICommitScope>(bindings.ProviderTransactionKey, self))
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
        await scope.SignalAsync(CommitOutcome.Committed, cancellationToken).ConfigureAwait(false);
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
        await scope.SignalAsync(CommitOutcome.RolledBack, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A SQL Server commit coordination scope is already attached for provider transaction key {ProviderTransactionKey}."
    )]
    private static partial void LogDuplicateScope(ILogger logger, object providerTransactionKey);

    private sealed class TrackedCommitScope(ICommitScope inner, Action<ICommitScope> detach) : ICommitScope
    {
        private int _disposed;

        public ICommitCoordinator Coordinator => inner.Coordinator;

        public async ValueTask SignalAsync(CommitOutcome outcome, CancellationToken cancellationToken)
        {
            await inner.SignalAsync(outcome, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            try
            {
                inner.Dispose();
            }
            finally
            {
                detach(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return ValueTask.CompletedTask;
            }

            // Not async: forward to the inner scope on this frame so its synchronous ambient pop propagates to the
            // caller; the inner drain (if any) is returned for the caller to await. Detach (registry remove-if-equal)
            // is ambient-free, so it can run synchronously once the pop has happened.
            try
            {
                return inner.DisposeAsync();
            }
            finally
            {
                detach(this);
            }
        }
    }
}
