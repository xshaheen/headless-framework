// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Headless.CommitCoordination;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.CommitCoordination.EntityFramework;

/// <summary>
/// Correlates EF Core <c>IDbTransactionInterceptor</c> commit/rollback edges to commit coordination scopes.
/// </summary>
[PublicAPI]
public sealed partial class EntityFrameworkCommitSignalSource(
    CommitScopeFactory scopeFactory,
    ILogger<EntityFrameworkCommitSignalSource>? logger = null
) : ICommitSignalSource
{
    private readonly ILogger _logger = logger ?? NullLogger<EntityFrameworkCommitSignalSource>.Instance;
    private readonly ConcurrentDictionary<object, ICommitScope> _scopes = new();

    /// <inheritdoc />
    public ICommitScope Attach(CommitCoordinatorBindings bindings, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(bindings);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = scopeFactory.Begin(bindings.Services, bindings.Capabilities);

        if (bindings.ProviderTransactionKey is not null)
        {
            var trackedScope = new TrackedCommitScope(
                scope,
                self => _scopes.TryRemove(new KeyValuePair<object, ICommitScope>(bindings.ProviderTransactionKey, self))
            );

            if (!_scopes.TryAdd(bindings.ProviderTransactionKey, trackedScope))
            {
                trackedScope.Dispose();
                LogDuplicateScope(_logger, bindings.ProviderTransactionKey);

                throw new InvalidOperationException(
                    "An EF Core commit coordination scope is already attached for this provider transaction key."
                );
            }

            scope = trackedScope;
        }

        return scope;
    }

    /// <summary>
    /// Signals a commit for the scope correlated to the given transaction, if one is attached.
    /// </summary>
    /// <remarks>
    /// The interceptor fires for every EF transaction, most of which are not coordinated; an absent key is the
    /// normal case and is silently ignored (never a warning).
    /// </remarks>
    /// <param name="providerTransactionKey">The transaction correlation key (the intercepted <c>DbTransaction</c>).</param>
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

    internal void SignalCommittedInBackground(object providerTransactionKey)
    {
        Argument.IsNotNull(providerTransactionKey);

        _SignalInBackground(Task.Run(() => SignalCommittedAsync(providerTransactionKey, CancellationToken.None).AsTask()));
    }

    /// <summary>
    /// Signals a rollback for the scope correlated to the given transaction, if one is attached.
    /// </summary>
    /// <param name="providerTransactionKey">The transaction correlation key (the intercepted <c>DbTransaction</c>).</param>
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

    internal void SignalRolledBackInBackground(object providerTransactionKey)
    {
        Argument.IsNotNull(providerTransactionKey);

        _SignalInBackground(Task.Run(() => SignalRolledBackAsync(providerTransactionKey, CancellationToken.None).AsTask()));
    }

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
    private static partial void LogDuplicateScope(ILogger logger, object providerTransactionKey);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "An EF Core commit coordination background signal faulted."
    )]
    private static partial void LogBackgroundSignalFaulted(ILogger logger, Exception? exception);
}
