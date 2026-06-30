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

    /// <summary>
    /// Initializes a connection wrapper around an existing <see cref="DbConnection"/>.
    /// </summary>
    /// <param name="connection">The underlying ADO.NET connection.</param>
    /// <param name="isExternallyOwned">
    /// <see langword="true"/> when the caller owns the connection lifetime; the wrapper will not open or
    /// close it, and keepalive and monitoring are disabled. <see langword="false"/> for internally-owned
    /// connections managed by this wrapper.
    /// </param>
    /// <param name="timeProvider">Clock forwarded to the <see cref="ConnectionMonitor"/>.</param>
    /// <param name="monitoringCommandTimeoutSeconds">Bounded timeout for keepalive/monitoring probes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="connection"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Initializes a connection wrapper around an existing <see cref="DbTransaction"/> (and its connection).
    /// </summary>
    /// <param name="transaction">The transaction whose connection backs this wrapper.</param>
    /// <param name="isExternallyOwned">Passed through to the connection constructor.</param>
    /// <param name="timeProvider">Clock forwarded to the <see cref="ConnectionMonitor"/>.</param>
    /// <param name="monitoringCommandTimeoutSeconds">Bounded timeout for keepalive/monitoring probes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transaction"/> or <paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the transaction's connection is <see langword="null"/> (the transaction has been disposed).</exception>
    protected DatabaseConnection(
        DbTransaction transaction,
        bool isExternallyOwned,
        TimeProvider timeProvider,
        int monitoringCommandTimeoutSeconds = ConnectionMonitor.DefaultMonitoringCommandTimeoutSeconds
    )
        : this(
            Argument.IsNotNull(transaction).Connection
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

    /// <summary>The connection-death monitor and keepalive worker attached to this connection.</summary>
    internal ConnectionMonitor ConnectionMonitor { get; }

    /// <summary>The <see cref="TimeProvider"/> passed at construction; used for timestamps and delays.</summary>
    internal TimeProvider TimeProvider { get; }

    /// <summary>The underlying <see cref="DbConnection"/>.</summary>
    internal DbConnection InnerConnection { get; }

    /// <summary><see langword="true"/> when a transaction is active on this connection.</summary>
    public bool HasTransaction => _transaction is not null;

    /// <summary>
    /// <see langword="true"/> when the connection was supplied by the caller and its lifetime is not managed
    /// by this wrapper. The wrapper will not open, close, or run keepalive on externally-owned connections.
    /// </summary>
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

    /// <summary>
    /// Creates a <see cref="DatabaseCommand"/> bound to this connection (and the current transaction, if any).
    /// Thread-safe: <see cref="DbConnection.CreateCommand"/> on some providers (e.g. Npgsql) is not
    /// thread-safe; creation is serialized by an internal lock.
    /// </summary>
    /// <returns>A new <see cref="DatabaseCommand"/> bound to this connection.</returns>
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

    /// <summary>
    /// Begins a transaction on the underlying connection. Only valid when no transaction is already active.
    /// The connection lock is acquired to serialize against the monitor worker.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the begin-transaction call.</param>
    /// <exception cref="InvalidOperationException">Thrown when a transaction is already active on this connection.</exception>
    public async ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
    {
        Ensure.True(!HasTransaction, "Connection already has a transaction.");

        using var _ = await ConnectionMonitor.AcquireConnectionLockAsync(CancellationToken.None).ConfigureAwait(false);

        _transaction = await InnerConnection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the underlying connection and starts the <see cref="ConnectionMonitor"/>. Provider-specific
    /// cancellation exceptions are wrapped as <see cref="OperationCanceledException"/> for uniform propagation.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the open operation.</param>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> fires or when the provider throws a
    /// provider-specific cancellation exception while the token is cancelled.
    /// </exception>
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

    /// <summary>
    /// Stops the connection monitor and closes the underlying connection. Only valid for internally-owned
    /// connections.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when called on an externally-owned connection.</exception>
    public ValueTask CloseAsync() => _DisposeOrCloseAsync(isDispose: false);

    /// <summary>
    /// Disposes the connection monitor and the underlying connection (if internally owned), and disposes
    /// any active transaction.
    /// </summary>
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
