// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Headless.Jobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Headless.Jobs.Infrastructure;

internal static class JobsKeyLock
{
    internal static Task AcquireAsync(
        DbContext context,
        JobKeyScope scope,
        JobKey key,
        CancellationToken cancellationToken
    )
    {
        // Length-delimited scope avoids ambiguous separators. A digest collision only serializes unrelated keys.
        var identity = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"jobs:key:{scope.TenantId?.Length ?? -1}:{scope.TenantId}{scope.Function.Length}:{scope.Function}{key.Value.Length}:{key.Value}"
        );
        return _AcquireAsync(context, identity, cancellationToken);
    }

    internal static async Task AcquireRunsAsync(
        DbContext context,
        IEnumerable<Guid> runIds,
        CancellationToken cancellationToken
    )
    {
        if (
            context.Database.ProviderName
            is not ("Npgsql.EntityFrameworkCore.PostgreSQL" or "Microsoft.EntityFrameworkCore.SqlServer")
        )
        {
            // These other backends cannot insert keyed rows, so their existing ordinary CRUD needs no key fence.
            return;
        }

        foreach (var id in runIds.Distinct().Order())
        {
            await _AcquireAsync(context, "jobs:run:" + id.ToString("D"), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task _AcquireAsync(DbContext context, string identity, CancellationToken cancellationToken)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction =
            context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("A transaction is required for a keyed Jobs write.");
        command.CommandTimeout = 30;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@key";
        if (
            string.Equals(
                context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal
            )
        )
        {
            command.CommandText = "SELECT pg_advisory_xact_lock(@key)";
            parameter.DbType = DbType.Int64;
            parameter.Value = BinaryPrimitives.ReadInt64LittleEndian(digest);
        }
        else if (
            string.Equals(
                context.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal
            )
        )
        {
            command.CommandText =
                "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@key, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000; SELECT @result;";
            parameter.DbType = DbType.String;
            parameter.Value = "jobs:key:" + Convert.ToHexStringLower(digest);
        }
        else
        {
            throw new NotSupportedException("Keyed Jobs require PostgreSQL, SQL Server, or the in-memory provider.");
        }

        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is int status && status < 0)
        {
            throw new TimeoutException("Could not acquire the transaction-owned Jobs key lock.");
        }
    }
}
