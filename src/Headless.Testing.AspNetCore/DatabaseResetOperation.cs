// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Xunit;

namespace Headless.Testing.AspNetCore;

internal static class DatabaseResetOperation
{
    public static CancellationToken ResolveCancellationToken(CancellationToken cancellationToken) =>
        cancellationToken.CanBeCanceled ? cancellationToken : TestContext.Current.CancellationToken;

    public static async Task<T> RunAsync<T>(
        DbConnection connection,
        Func<Task<T>> operation,
        CancellationToken cancellationToken
    )
    {
        cancellationToken = ResolveCancellationToken(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var registration = cancellationToken.Register(
            static state => _CloseConnection((DbConnection)state!),
            connection
        );

        try
        {
            var result = await operation().ConfigureAwait(false);
            await registration.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is not OperationCanceledException)
        {
            throw new OperationCanceledException("Database reset was cancelled.", ex, cancellationToken);
        }
        finally
        {
            await registration.DisposeAsync().ConfigureAwait(false);
        }
    }

    public static async Task RunAsync(
        DbConnection connection,
        Func<Task> operation,
        CancellationToken cancellationToken
    )
    {
        await RunAsync(
                connection,
                async () =>
                {
                    await operation().ConfigureAwait(false);
                    return true;
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static void _CloseConnection(DbConnection connection)
    {
        try
        {
            // Respawn 7 has no CancellationToken overloads; closing the connection is the only
            // provider-neutral way to interrupt its active command while still observing completion.
            connection.Close();
        }
#pragma warning disable ERP022, RCS1075 // Cancellation must not be replaced by a provider close failure.
        catch (Exception)
        {
            // Cancellation must win even when a broken provider throws while closing.
        }
#pragma warning restore ERP022, RCS1075
    }
}
