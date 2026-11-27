// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sql;

/// <summary>
/// Reports a driver fault that the caller's cancel caused as the <see cref="OperationCanceledException" /> it is, so a
/// cancelled store call throws the same exception on every engine.
/// </summary>
/// <remarks>
/// <para>
/// SqlClient reports a cancel that interrupts a running command as a <c>SqlException</c> ("Operation cancelled by
/// user"), where Npgsql and Microsoft.Data.Sqlite throw <see cref="OperationCanceledException" />. A fault that already
/// is an <see cref="OperationCanceledException" /> passes through unchanged, so the PostgreSQL and SQLite paths are
/// never wrapped twice.
/// </para>
/// <para>
/// The translation keys on the caller's token: a database fault raised after the token was cancelled is reported as the
/// cancel, with the driver's fault as its <see cref="Exception.InnerException" />. A fault raised while the token is
/// still live surfaces unchanged.
/// </para>
/// <para>
/// <see cref="SqlAutonomousTransaction" /> and <see cref="SqlAutonomousRead" /> apply it to every attempt already. Wrap
/// a command you run on a caller's connection or transaction yourself with <see cref="ObserveAsync{T}(Task{T}, CancellationToken)" />.
/// </para>
/// </remarks>
[PublicAPI]
public static class SqlCancellation
{
    /// <summary>Returns whether <paramref name="exception" /> is a database fault the caller's cancel caused.</summary>
    /// <param name="exception">The failure, as thrown; wrappers such as EF's <c>DbUpdateException</c> are walked.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// <see langword="true" /> when <paramref name="cancellationToken" /> is cancelled and the failure carries a
    /// <see cref="System.Data.Common.DbException" />, unless it already is an <see cref="OperationCanceledException" />.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static bool IsCancellation(Exception exception, CancellationToken cancellationToken)
    {
        Argument.IsNotNull(exception);

        return cancellationToken.IsCancellationRequested
            && exception is not OperationCanceledException
            && RelationalFaults.FindDatabaseException(exception) is not null;
    }

    /// <summary>Creates the <see cref="OperationCanceledException" /> that reports <paramref name="exception" />.</summary>
    /// <param name="exception">The driver's fault; it becomes the inner exception.</param>
    /// <param name="cancellationToken">The caller's cancelled token.</param>
    /// <returns>The cancellation, carrying <paramref name="cancellationToken" /> and the driver's fault.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception" /> is <see langword="null" />.</exception>
    public static OperationCanceledException ToOperationCanceled(
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(exception);

        return new OperationCanceledException("The database command was cancelled.", exception, cancellationToken);
    }

    /// <summary>Awaits <paramref name="call" />, reporting a database fault the cancel caused as the cancellation.</summary>
    /// <param name="call">The started database call.</param>
    /// <param name="cancellationToken">The token the call runs with.</param>
    /// <returns>The call's result.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    public static async Task<T> ObserveAsync<T>(Task<T> call, CancellationToken cancellationToken)
    {
        _ = Argument.IsNotNull(call);

        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCancellation(exception, cancellationToken))
        {
            throw ToOperationCanceled(exception, cancellationToken);
        }
    }

    /// <summary>Awaits <paramref name="call" />, reporting a database fault the cancel caused as the cancellation.</summary>
    /// <param name="call">The started database call.</param>
    /// <param name="cancellationToken">The token the call runs with.</param>
    /// <returns>A task that completes when the call completes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    public static async Task ObserveAsync(Task call, CancellationToken cancellationToken)
    {
        _ = Argument.IsNotNull(call);

        try
        {
            await call.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCancellation(exception, cancellationToken))
        {
            throw ToOperationCanceled(exception, cancellationToken);
        }
    }

    /// <summary>Awaits <paramref name="call" />, reporting a database fault the cancel caused as the cancellation.</summary>
    /// <param name="call">The started database call.</param>
    /// <param name="cancellationToken">The token the call runs with.</param>
    /// <returns>The call's result.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    public static async ValueTask<T> ObserveAsync<T>(ValueTask<T> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCancellation(exception, cancellationToken))
        {
            throw ToOperationCanceled(exception, cancellationToken);
        }
    }

    /// <summary>Awaits <paramref name="call" />, reporting a database fault the cancel caused as the cancellation.</summary>
    /// <param name="call">The started database call.</param>
    /// <param name="cancellationToken">The token the call runs with.</param>
    /// <returns>A task that completes when the call completes.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> was cancelled.</exception>
    public static async ValueTask ObserveAsync(ValueTask call, CancellationToken cancellationToken)
    {
        try
        {
            await call.ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCancellation(exception, cancellationToken))
        {
            throw ToOperationCanceled(exception, cancellationToken);
        }
    }
}
