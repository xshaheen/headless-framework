// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>
/// An enlisted admission runs its batch on the caller's session, and a caller running with <c>XACT_ABORT ON</c> has
/// its whole transaction doomed by any error, even one the batch caught. Concurrent first admissions of one key must
/// therefore serialize on the key-range lock instead of racing an insert into a duplicate-key error.
/// </summary>
[Collection<SqlServerIdempotencyFixture>]
public sealed class SqlServerIdempotencyXactAbortTests(SqlServerIdempotencyFixture fixture) : TestBase
{
    [Fact]
    public async Task should_admit_concurrently_under_xact_abort_without_dooming_the_waiting_transaction()
    {
        // given
        var key = $"key-{Guid.NewGuid():N}";
        var fingerprint = IdempotencyFingerprint.Compute("request-a");
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var winner = await fixture.BeginUnitAsync(host, AbortToken);
        await using var waiter = await fixture.BeginUnitAsync(host, AbortToken);
        await _ExecuteAsync(winner.Unit, "SET XACT_ABORT ON;");
        await _ExecuteAsync(waiter.Unit, "SET XACT_ABORT ON;");

        var won = await winner.Unit.Idempotency.AdmitAsync(key, fingerprint, cancellationToken: AbortToken);
        won.IsAdmitted.Should().BeTrue();

        // when - the second first-admission of the key waits on the key range the winner's insert holds
        var pending = waiter.Unit.Idempotency.AdmitAsync(key, fingerprint, cancellationToken: AbortToken).AsTask();
        var first = await Task.WhenAny(
            pending,
            Task.Delay(IdempotencyFixtureExtensions.BlockedObservationWindow, AbortToken)
        );
        first.Should().NotBeSameAs(pending, "the waiter blocks on the winner's key-range lock");

        await winner.Unit.Idempotency.CompleteAsync(
            won,
            Encoding.UTF8.GetBytes("winner"),
            "test-result.v1",
            cancellationToken: AbortToken
        );
        await winner.CommitAsync(AbortToken);

        var replay = await pending.WaitAsync(IdempotencyFixtureExtensions.ReleaseTimeout, AbortToken);

        // then - no duplicate-key error doomed the waiter: its transaction is still committable
        replay.Disposition.Should().Be(IdempotentDisposition.Replay);
        Encoding.UTF8.GetString(replay.Result!.Payload.Span).Should().Be("winner");
        (await _ScalarAsync(waiter.Unit, "SELECT XACT_STATE();")).Should().Be(1);
        await waiter.CommitAsync(AbortToken);
    }

    private async Task _ExecuteAsync(IUnitOfWork unit, string sql)
    {
        await _ScalarAsync(unit, sql);
    }

    private async Task<int?> _ScalarAsync(IUnitOfWork unit, string sql)
    {
        var resource = (IRelationalUnitOfWorkResource)unit.Resource!;
        await using var command = new SqlCommand(
            sql,
            (SqlConnection)resource.Connection,
            (SqlTransaction)resource.Transaction
        );
        var value = await command.ExecuteScalarAsync(AbortToken);

        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }
}
