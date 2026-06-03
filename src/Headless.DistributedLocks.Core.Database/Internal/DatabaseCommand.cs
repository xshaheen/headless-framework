// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Async-only abstraction over <see cref="IDbCommand"/> for a <see cref="DatabaseConnection"/>. Smooths over
/// cancellation behavior (some ADO.NET providers throw provider-specific exceptions instead of
/// <see cref="OperationCanceledException"/> on cancel) and serializes execution against the owning connection's
/// monitor so a user query never overlaps a keepalive/monitoring probe on the same physical connection.
/// </summary>
internal sealed class DatabaseCommand(DbCommand command, DatabaseConnection connection) : IDisposable
{
    private readonly DbCommand _command = command;

#pragma warning disable CA2213 // Not owned by the command; the connection outlives every command created against it.
    private readonly DatabaseConnection _connection = connection;
#pragma warning restore CA2213

    public IDataParameterCollection Parameters => _command.Parameters;

    // SQL here is always a constant emitted by the lock strategies / monitor, never user input.
#pragma warning disable CA2100
    public void SetCommandText(string sql)
    {
        _command.CommandText = sql;
    }
#pragma warning restore CA2100

    /// <summary>
    /// Sets the command timeout. <see cref="Timeout.InfiniteTimeSpan"/> maps to the ADO.NET infinite timeout (0).
    /// A finite timeout is rounded up to whole seconds plus a 30 second buffer, matching the operation-timeout
    /// convention used by the lock strategies (the server-side wait should reach its own timeout before the client
    /// command timeout fires).
    /// </summary>
    public void SetTimeout(TimeSpan operationTimeout)
    {
        _command.CommandTimeout = operationTimeout == Timeout.InfiniteTimeSpan
            ? 0
            : (int)Math.Ceiling(operationTimeout.TotalSeconds) + 30;
    }

    /// <summary>
    /// Sets an explicit, bounded command timeout in whole seconds with no buffer. Used by <see cref="ConnectionMonitor"/>
    /// so a half-open TCP connection (a network drop with no RST) cannot hang the monitor worker indefinitely on a
    /// keepalive/monitoring probe.
    /// </summary>
    public void SetExactTimeoutSeconds(int seconds)
    {
        _command.CommandTimeout = seconds;
    }

    public void SetCommandType(CommandType type)
    {
        _command.CommandType = type;
    }

    public DbParameter AddParameter(
        string? name = null,
        object? value = null,
        DbType? type = null,
        ParameterDirection? direction = null
    )
    {
        var parameter = _command.CreateParameter();

        if (name is not null)
        {
            parameter.ParameterName = name;
        }

        if (value is not null)
        {
            parameter.Value = value;
        }

        if (type is not null)
        {
            parameter.DbType = type.Value;
        }

        if (direction is not null)
        {
            parameter.Direction = direction.Value;
        }

        _command.Parameters.Add(parameter);

        return parameter;
    }

    public ValueTask<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
        ExecuteNonQueryAsync(isConnectionMonitoringQuery: false, cancellationToken);

    /// <summary>Internal API for <see cref="ConnectionMonitor"/>: skips taking the connection lock the monitor already holds.</summary>
    internal ValueTask<int> ExecuteNonQueryAsync(bool isConnectionMonitoringQuery, CancellationToken cancellationToken) =>
        _ExecuteAsync(
            static (command, token) => command.ExecuteNonQueryAsync(token),
            isConnectionMonitoringQuery,
            cancellationToken
        );

    public ValueTask<object?> ExecuteScalarAsync(CancellationToken cancellationToken) =>
        _ExecuteAsync(
            static (command, token) => command.ExecuteScalarAsync(token),
            isConnectionMonitoringQuery: false,
            cancellationToken
        );

    private async ValueTask<TResult> _ExecuteAsync<TResult>(
        Func<DbCommand, CancellationToken, Task<TResult>> executeAsync,
        bool isConnectionMonitoringQuery,
        CancellationToken cancellationToken
    )
    {
        // check first rather than rely on a race between the registration and command execution
        cancellationToken.ThrowIfCancellationRequested();

        using var _ = await _AcquireConnectionLockIfNeededAsync(isConnectionMonitoringQuery).ConfigureAwait(false);
        await _PrepareIfNeededAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await executeAsync(_command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            // Canceled SQL operations on some providers throw a provider-specific exception instead of OCE. That would
            // leave downstream operations faulted instead of canceled, so we wrap with OCE to propagate cancellation.
            when (cancellationToken.IsCancellationRequested && _connection.IsCommandCancellationException(exception))
        {
            throw new OperationCanceledException("Command was canceled", exception, cancellationToken);
        }
    }

    private ValueTask _PrepareIfNeededAsync(CancellationToken cancellationToken)
    {
        return _connection.ShouldPrepareCommands ? new ValueTask(_command.PrepareAsync(cancellationToken)) : default;
    }

    public void Dispose()
    {
        _command.Dispose();
    }

    // NOTE: no cancellation token here — the connection lock should never be held for long except in bug scenarios
    // (such as multi-threaded use of a single connection).
    private ValueTask<IDisposable?> _AcquireConnectionLockIfNeededAsync(bool isConnectionMonitoringQuery)
    {
        return isConnectionMonitoringQuery
            ? new ValueTask<IDisposable?>(default(IDisposable?))
            : _connection.ConnectionMonitor.AcquireConnectionLockAsync(CancellationToken.None);
    }
}
