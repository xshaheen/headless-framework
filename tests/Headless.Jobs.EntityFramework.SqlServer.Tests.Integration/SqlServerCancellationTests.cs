// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// SqlClient reports a cancel that interrupts a running statement as a <see cref="SqlException" />, where Npgsql and
/// Microsoft.Data.Sqlite throw <see cref="OperationCanceledException" />. EF Core already reports its own commands'
/// cancel as <see cref="OperationCanceledException" />; the Jobs store's own ADO.NET commands, such as the run lock a
/// delete takes, must too. Each test holds the lock the call needs, so the cancel always lands while the call's
/// statement waits on the server.
/// </summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerCancellationTests(SqlServerJobsCoordinationFixture fixture) : TestBase
{
    [Fact]
    public async Task should_throw_operation_canceled_when_a_delete_is_cancelled_while_it_waits_on_the_run_lock()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildHost("cancel-delete");
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync(host, ct);
        await host.StartAsync(ct);

        try
        {
            var persistence = host.Services.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
            var id = Guid.NewGuid();
            await fixture.SeedTimeJobAsync(id, "cancel-delete", (int)JobStatus.Idle, null, ct);

            await using var holder = new SqlConnection(fixture.ConnectionString);
            await holder.OpenAsync(ct);
            await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync(ct);
            var holderSession = await _HoldRunLockAsync(holder, transaction, id, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var call = persistence.RemoveTimeJobsAsync([id], cts.Token);

            await _WaitUntilBlockedAsync(holderSession, ct);
            await cts.CancelAsync();

            var thrown = await FluentActions.Awaiting(() => call).Should().ThrowAsync<OperationCanceledException>();
            thrown.Which.InnerException.Should().BeOfType<SqlException>();

            await transaction.RollbackAsync(ct);
            (await persistence.RemoveTimeJobsAsync([id], ct)).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }

    // The resource the Jobs store's run lock takes for a time job: the SHA-256 of its run identity.
    private static async Task<short> _HoldRunLockAsync(
        SqlConnection holder,
        SqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken
    )
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes("jobs:run:" + id.ToString("D")));
        await using var command = new SqlCommand(
            """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';
            SELECT CAST(@@SPID AS smallint) WHERE @result >= 0;
            """,
            holder,
            transaction
        );
        command.Parameters.AddWithValue("resource", "jobs:key:" + Convert.ToHexStringLower(digest));

        var session = await command.ExecuteScalarAsync(cancellationToken);
        session.Should().NotBeNull("the holder must own the run lock before the delete asks for it");

        return (short)session!;
    }

    // The server waits until the call's statement queues on the held lock, so the cancel cannot reach the call before
    // its command does.
    private async Task _WaitUntilBlockedAsync(short holderSession, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            WHILE NOT EXISTS (SELECT 1 FROM sys.dm_exec_requests WHERE blocking_session_id = @holder)
                WAITFOR DELAY '00:00:00.010';
            """,
            connection
        );
        command.Parameters.AddWithValue("holder", holderSession);
        command.CommandTimeout = 0;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
