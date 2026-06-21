// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.PostgreSql;

/// <summary>
/// Inline PostgreSQL signal source for framework-owned transaction flows.
/// </summary>
/// <remarks>
/// Unlike SQL Server, Npgsql exposes no diagnostic listener that surfaces the native transaction commit/rollback
/// edge, so there is no out-of-band detector. The model is therefore <b>inline (caller-driven)</b>: the caller
/// enlists a scope keyed by the open <c>NpgsqlTransaction</c>, then drives the signal by calling
/// <see cref="SignalCommittedAsync(object, CancellationToken)" /> or
/// <see cref="SignalRolledBackAsync(object, CancellationToken)" /> immediately after committing or rolling that
/// transaction back. If the caller never signals, disposing the returned scope discards the enlisted work.
/// </remarks>
[PublicAPI]
public sealed partial class PostgreSqlCommitSignalSource(
    ICommitScopeFactory scopeFactory,
    ILogger<PostgreSqlCommitSignalSource>? logger = null
) : ICommitSignalSource
{
    private readonly ILogger _logger = logger ?? NullLogger<PostgreSqlCommitSignalSource>.Instance;
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
                    "A PostgreSQL commit coordination scope is already attached for this provider transaction key."
                );
            },
            cancellationToken
        );

    /// <summary>
    /// Signals a commit for a previously attached scope, draining its registered
    /// <see cref="ICommitCoordinator.OnCommit" /> callbacks.
    /// </summary>
    /// <remarks>An absent key is silently ignored — the normal case for an uncoordinated transaction.</remarks>
    /// <param name="providerTransactionKey">
    /// The provider transaction key — the <c>NpgsqlTransaction</c> instance passed to
    /// <c>NpgsqlConnection.EnlistCommitCoordination</c>.
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

        await scope.SignalAsync(CommitOutcome.Committed).ConfigureAwait(false);
    }

    /// <summary>
    /// Signals a rollback for a previously attached scope, draining its registered
    /// <see cref="ICommitCoordinator.OnRollback" /> callbacks.
    /// </summary>
    /// <param name="providerTransactionKey">
    /// The provider transaction key — the <c>NpgsqlTransaction</c> instance passed to
    /// <c>NpgsqlConnection.EnlistCommitCoordination</c>.
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

        await scope.SignalAsync(CommitOutcome.RolledBack).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "A PostgreSQL commit coordination scope is already attached for provider transaction key {ProviderTransactionKey}."
    )]
    private static partial void LogDuplicateScope(ILogger logger, object providerTransactionKey);
}
