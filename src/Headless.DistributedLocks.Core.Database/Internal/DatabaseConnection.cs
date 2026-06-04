// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Async-only abstraction over <see cref="DbConnection"/> that integrates with <see cref="ConnectionMonitor"/> for
/// keepalive and connection-death detection. Concrete providers (for example PostgreSQL) subclass this to supply
/// provider-specific behavior such as command preparation, cancellation-exception mapping, and a server-side sleep.
/// </summary>
internal abstract class DatabaseConnection : IAsyncDisposable
{
    private readonly Lock _createCommandLock = new();
    private DbTransaction? _transaction;

    protected DatabaseConnection(
        DbConnection connection,
        bool isExternallyOwned,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds = ConnectionMonitor.DefaultMonitoringCommandTimeoutSeconds
    )
    {
        InnerConnection = Argument.IsNotNull(connection);
        IsExternallyOwned = isExternallyOwned;
        TimeProvider = Argument.IsNotNull(timeProvider);
        ConnectionMonitor = new ConnectionMonitor(this, TimeProvider, monitoringCommandTimeoutSeconds);
    }

    protected DatabaseConnection(
        DbTransaction transaction,
        bool isExternallyOwned,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds = ConnectionMonitor.DefaultMonitoringCommandTimeoutSeconds
    )
        : this(
            (Argument.IsNotNull(transaction).Connection)
                ?? throw new InvalidOperationException(
                    "Cannot execute queries against a transaction that has been disposed."
                ),
            isExternallyOwned,
            timeProvider,
            monitoringCommandTimeoutSeconds
        )
    {
        _transaction = transaction;
    }

    internal ConnectionMonitor ConnectionMonitor { get; }

    internal TimeProvider TimeProvider { get; }

    internal DbConnection InnerConnection { get; }

    public bool HasTransaction => _transaction is not null;

    public bool IsExternallyOwned { get; }

    /// <summary>Whether commands should be prepared (<c>PrepareAsync</c>) before execution.</summary>
    public abstract bool ShouldPrepareCommands { get; }

    internal bool CanExecuteQueries =>
        InnerConnection.State == ConnectionState.Open && (_transaction is null || _transaction.Connection is not null);

    internal void SetKeepaliveCadence(TimeSpan cadence)
    {
        ConnectionMonitor.SetKeepaliveCadence(cadence);
    }

    internal IDatabaseConnectionMonitoringHandle GetConnectionMonitoringHandle() =>
        ConnectionMonitor.GetMonitoringHandle();

    public DatabaseCommand CreateCommand()
    {
        DbCommand command;

        // Npgsql recycles commands, so CreateCommand() is not actually thread-safe. Synchronizing access here is
        // sufficient for this provider.
        lock (_createCommandLock)
        {
            command = InnerConnection.CreateCommand();
        }

        command.Transaction = _transaction;

        return new DatabaseCommand(command, this);
    }

    public async ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
    {
        Ensure.True(!HasTransaction, "Connection already has a transaction.");

        using var _ = await ConnectionMonitor.AcquireConnectionLockAsync(CancellationToken.None).ConfigureAwait(false);

        _transaction = await InnerConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        try
        {
            await InnerConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        // some providers (for example Oracle) throw a provider-specific exception instead of OCE on cancel
        catch (Exception exception)
            when (cancellationToken.IsCancellationRequested && IsCommandCancellationException(exception))
        {
            throw new OperationCanceledException("Connection open canceled.", exception, cancellationToken);
        }

        ConnectionMonitor.Start();
    }

    public ValueTask CloseAsync() => _DisposeOrCloseAsync(isDispose: false);

    public ValueTask DisposeAsync() => _DisposeOrCloseAsync(isDispose: true);

    private async ValueTask _DisposeOrCloseAsync(bool isDispose)
    {
        Ensure.True(isDispose || !IsExternallyOwned, "Cannot close an externally-owned connection.");

        try
        {
            if (isDispose)
            {
                await ConnectionMonitor.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await ConnectionMonitor.StopAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            if (!IsExternallyOwned)
            {
                try
                {
                    await _DisposeTransactionAsync(isClosingOrDisposingConnection: true).ConfigureAwait(false);
                }
                finally
                {
                    if (isDispose)
                    {
                        await InnerConnection.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await InnerConnection.CloseAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }

    public ValueTask DisposeTransactionAsync() => _DisposeTransactionAsync(isClosingOrDisposingConnection: false);

    private async ValueTask _DisposeTransactionAsync(bool isClosingOrDisposingConnection)
    {
        var transaction = _transaction;

        if (transaction is null)
        {
            return;
        }

        _transaction = null;

        // we don't need the connection lock when closing/disposing — the monitor was already stopped above
        using var _ = isClosingOrDisposingConnection
            ? null
            : await ConnectionMonitor.AcquireConnectionLockAsync(CancellationToken.None).ConfigureAwait(false);

        await transaction.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="exception"/> represents the provider's command-cancellation signal.</summary>
    public abstract bool IsCommandCancellationException(Exception exception);

    /// <summary>
    /// Executes a server-side sleep of <paramref name="sleepTime"/> using the provider's sleep mechanism (for example
    /// <c>pg_sleep</c>). The monitor uses this as a long-running, cancellable probe of the connection's liveness.
    /// </summary>
    public abstract Task SleepAsync(
        TimeSpan sleepTime,
        Func<DatabaseCommand, CancellationToken, ValueTask<int>> executor,
        CancellationToken cancellationToken
    );
}
