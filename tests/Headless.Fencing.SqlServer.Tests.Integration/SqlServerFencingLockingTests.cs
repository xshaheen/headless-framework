// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>
/// SQL Server behavior the provider-neutral suite cannot pin down: that no step of a fence, a waiting grant, and a
/// settlement is ever chosen as a deadlock victim; that a grant never raises a duplicate key inside a caller's
/// <c>XACT_ABORT ON</c> transaction; and that keys SQL Server would pad together never reach the table.
/// </summary>
[Collection<SqlServerFencingFixture>]
public sealed class SqlServerFencingLockingTests(SqlServerFencingFixture fixture) : TestBase
{
    private static readonly TimeSpan _LongDuration = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task should_settle_under_an_open_fence_while_an_enlisted_grant_waits_without_a_deadlock()
    {
        var (kind, resource) = (_CreateKind(), _CreateResource());
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken);

        await using var fencing = await fixture.BeginUnitAsync(host, AbortToken);
        await fencing.Unit.Leases.FenceAsync(granted.Lease!, AbortToken);

        // Both sides are enlisted, so neither retries: a deadlock victim on either side would surface as error 1205
        // instead of being absorbed by the autonomous retry.
        await using var waiting = await fixture.BeginUnitAsync(host, AbortToken);
        var pending = waiting.Unit.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken).AsTask();
        var first = await Task.WhenAny(
            pending,
            Task.Delay(LeasesFixtureExtensions.BlockedObservationWindow, AbortToken)
        );

        first.Should().NotBeSameAs(pending, "the grant waits on the row the fence holds");

        (await fencing.Unit.Leases.SettleAsync(granted.Lease!, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
        await fencing.CommitAsync(AbortToken);

        var next = await pending.WaitAsync(LeasesFixtureExtensions.ReleaseTimeout, AbortToken);
        await waiting.CommitAsync(AbortToken);

        next.Status.Should().Be(LeaseGrantStatus.Granted, "the waiting grant sees the committed settlement");
        next.Lease!.Generation.Should().BeGreaterThan(granted.Lease!.Generation);
        (await fixture.ReadLeaseAsync(new LeaseKey("", kind, resource), AbortToken))!
            .Generation.Should()
            .Be(next.Lease.Generation);
    }

    [Fact]
    public async Task should_report_held_without_a_duplicate_key_inside_an_xact_abort_transaction()
    {
        var (kind, resource) = (_CreateKind(), _CreateResource());
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);

        await using var first = await _BeginXactAbortAsync(host);
        await using var second = await _BeginXactAbortAsync(host);

        // Both see no row. The first inserts; the second's locking read waits on the key range the first holds,
        // then finds the committed row instead of inserting over it.
        var winner = await first.Unit.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken);
        var pending = second.Unit.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken).AsTask();
        var blocked = await Task.WhenAny(
            pending,
            Task.Delay(LeasesFixtureExtensions.BlockedObservationWindow, AbortToken)
        );

        blocked.Should().NotBeSameAs(pending, "the second grant waits on the first grant's key range");

        await first.CommitAsync(AbortToken);
        var held = await pending.WaitAsync(LeasesFixtureExtensions.ReleaseTimeout, AbortToken);

        winner.Status.Should().Be(LeaseGrantStatus.Granted);
        held.Status.Should().Be(LeaseGrantStatus.Held);
        held.HolderGeneration.Should().Be(winner.Lease!.Generation);
        (await second.XactStateAsync(AbortToken)).Should().Be(1, "the caller's transaction is still committable");

        // A second refused grant over the now-committed live lease also leaves the transaction usable.
        (await second.Unit.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken))
            .Status.Should()
            .Be(LeaseGrantStatus.Held);
        (await second.XactStateAsync(AbortToken)).Should().Be(1);
        await second.CommitAsync(AbortToken);
    }

    [Fact]
    public async Task should_refuse_a_key_that_differs_only_by_a_trailing_space_before_any_write()
    {
        var (kind, resource) = (_CreateKind(), _CreateResource());
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken);
        var before = await fixture.ReadLeaseAsync(new LeaseKey("", kind, resource), AbortToken);

        var padded = async () => await host.Leases.GrantAsync(kind, resource + " ", _LongDuration, AbortToken);
        var paddedKind = async () => await host.Leases.GrantAsync(kind + " ", resource, _LongDuration, AbortToken);
        var paddedLease = async () =>
            await host.Leases.SettleAsync(granted.Lease! with { Resource = resource + " " }, AbortToken);

        await padded.Should().ThrowAsync<ArgumentException>();
        await paddedKind.Should().ThrowAsync<ArgumentException>();
        await paddedLease.Should().ThrowAsync<ArgumentException>();

        using (host.CurrentTenant.Change("tenant-a "))
        {
            var paddedTenant = async () => await host.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken);
            await paddedTenant.Should().ThrowAsync<ArgumentException>();
        }

        (
            await fixture.ScalarAsync(
                "SELECT COUNT(*) FROM [fencing].[leases] WHERE [kind] = @kind",
                AbortToken,
                ("kind", kind)
            )
        )
            .Should()
            .Be(1, "SQL Server pads trailing spaces when comparing, so a padded key would have matched this row");
        (await fixture.ReadLeaseAsync(new LeaseKey("", kind, resource), AbortToken)).Should().Be(before);
    }

    private async Task<XactAbortUnit> _BeginXactAbortAsync(LeasesHost host)
    {
#pragma warning disable CA2000 // False positive: the returned XactAbortUnit owns the connection and disposes it; the catch disposes it on failure.
        var connection = new SqlConnection(fixture.LeaseConnectionString);
#pragma warning restore CA2000

        try
        {
            await connection.OpenAsync(AbortToken);

            await using (var set = new SqlCommand("SET XACT_ABORT ON;", connection))
            {
                await set.ExecuteNonQueryAsync(AbortToken);
            }

            var transaction = (SqlTransaction)
                await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, AbortToken);

            return new XactAbortUnit(host.Factory.Enlist(connection, transaction), connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }
    }

    private static string _CreateKind()
    {
        return $"kind-{Guid.NewGuid():N}";
    }

    private static string _CreateResource()
    {
        return $"resource-{Guid.NewGuid():N}";
    }

    /// <summary>An observed unit over a transaction the test owns, on a session running with XACT_ABORT ON.</summary>
    private sealed class XactAbortUnit(IUnitOfWork unit, SqlConnection connection, SqlTransaction transaction)
        : IAsyncDisposable
    {
        public IUnitOfWork Unit => unit;

        public async Task<int> XactStateAsync(CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("SELECT CAST(XACT_STATE() AS int);", connection, transaction);

            return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await transaction.CommitAsync(cancellationToken);
            await unit.CompleteAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            await unit.DisposeAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
