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
    private const int _LockTimeoutSeconds = 30;

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
        return _AcquireAsync(context, [identity], cancellationToken);
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

        var identities = runIds.Distinct().Order().Select(id => "jobs:run:" + id.ToString("D")).ToArray();
        if (identities.Length != 0)
        {
            await _AcquireAsync(context, identities, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task _AcquireAsync(DbContext context, string[] identities, CancellationToken cancellationToken)
    {
        var digests = identities.Select(identity => SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToArray();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction =
            context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("A transaction is required for a keyed Jobs write.");
        // The server bounds the whole batch; transport cancellation must not win an ordinary contention timeout.
        command.CommandTimeout = _LockTimeoutSeconds * 2;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@keys";
        var timeout = command.CreateParameter();
        timeout.ParameterName = "@timeout";
        timeout.DbType = DbType.Int32;
        timeout.Value = _LockTimeoutSeconds;
        command.Parameters.Add(timeout);
        if (
            string.Equals(
                context.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal
            )
        )
        {
            // Retry only the first unavailable lock, preserving the supplied order and already acquired prefix.
            // Try-locks return normally on contention, keeping the caller transaction and its settings intact.
            command.CommandText = """
                WITH RECURSIVE attempts(position, acquired) AS (
                    VALUES (0, true)
                    UNION ALL
                    SELECT next.position, pg_try_advisory_xact_lock((@keys)[next.position])
                    FROM attempts
                    CROSS JOIN LATERAL (
                        SELECT attempts.position + CASE WHEN attempts.acquired THEN 1 ELSE 0 END AS position
                    ) AS next
                    CROSS JOIN LATERAL (
                        SELECT pg_sleep(CASE WHEN attempts.acquired THEN 0 ELSE 0.05 END)
                    ) AS pause
                    WHERE next.position <= cardinality(@keys)
                      AND clock_timestamp() < statement_timestamp() + make_interval(secs => @timeout)
                )
                SELECT COALESCE(max(position) FILTER (WHERE acquired), 0) FROM attempts;
                """;
            parameter.Value = digests.Select(digest => BinaryPrimitives.ReadInt64LittleEndian(digest)).ToArray();
        }
        else if (
            string.Equals(
                context.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal
            )
        )
        {
            // One JSON parameter avoids SQL Server's parameter limit for large bulk writes.
            command.CommandText = """
                DECLARE @started datetime2 = SYSUTCDATETIME(), @position int = 0,
                    @resource nvarchar(255), @result int, @remaining int, @count int;
                DECLARE @locks TABLE (position int PRIMARY KEY, resource nvarchar(255));
                INSERT INTO @locks SELECT CONVERT(int, [key]), value FROM OPENJSON(@keys);
                SET @count = (SELECT COUNT(*) FROM @locks);
                WHILE @position < @count
                BEGIN
                    SET @remaining = @timeout * 1000 - DATEDIFF(millisecond, @started, SYSUTCDATETIME());
                    IF @remaining <= 0 BREAK;
                    SELECT @resource = resource FROM @locks WHERE position = @position;
                    EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive',
                        @LockOwner='Transaction', @LockTimeout=@remaining;
                    IF @result < 0
                    BEGIN
                        IF @result <> -1
                        BEGIN
                            SELECT @result;
                            RETURN;
                        END;
                        BREAK;
                    END;
                    SET @position += 1;
                END;
                SELECT @position;
                """;
            parameter.DbType = DbType.String;
            // Only the fixed prefix and hexadecimal digits enter JSON, so no escaping or reflection is needed.
            parameter.Value =
                "[\""
                + string.Join("\",\"", digests.Select(digest => "jobs:key:" + Convert.ToHexStringLower(digest)))
                + "\"]";
        }
        else
        {
            throw new NotSupportedException("Keyed Jobs require PostgreSQL, SQL Server, or the in-memory provider.");
        }

        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (result is not int acquired || acquired != identities.Length)
        {
            if (result is int failure && failure < 0)
            {
                throw new InvalidOperationException($"Transaction-owned Jobs key lock failed with status {failure}.");
            }
            throw new TimeoutException("Could not acquire the transaction-owned Jobs key lock.");
        }
    }
}
