// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.Sequences;

/// <summary>Reports a driver fault that the caller's cancel caused as the cancellation it is.</summary>
/// <remarks>
/// SqlClient reports a cancel that interrupts a running command as a <c>SqlException</c> ("Operation cancelled by
/// user"), where Npgsql and Microsoft.Data.Sqlite throw <see cref="OperationCanceledException" />. Without this, the
/// same cancel would surface as a different exception per engine, and only on SQL Server as a database failure.
/// </remarks>
internal static class RelationalCancellation
{
    public static async ValueTask<T> ObserveAsync<T>(ValueTask<T> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call.ConfigureAwait(false);
        }
        catch (DbException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("The sequence call was cancelled.", exception, cancellationToken);
        }
    }
}
