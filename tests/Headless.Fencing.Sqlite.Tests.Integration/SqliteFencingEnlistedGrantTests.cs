// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// SQLite refuses a grant inside a caller's unit, because SQLite has no counter that survives the caller's rollback:
/// the generation a rolled-back grant drew would be drawn again as another holder's fencing token. The scenarios the
/// shared suite runs on an enlisted grant run here on an autonomous one.
/// </summary>
[Collection<SqliteFencingFixture>]
public sealed class SqliteFencingEnlistedGrantTests(SqliteFencingFixture fixture) : TestBase
{
    private static readonly TimeSpan _LongDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _ShortDuration = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task should_refuse_an_enlisted_grant_before_writing_and_name_the_reason()
    {
        var (kind, resource) = _Create();
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);
        await using var unit = await fixture.BeginUnitAsync(host, AbortToken);

        var act = async () => await unit.Unit.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken);

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should()
            .Contain("survives the caller's rollback");
        unit.Unit.State.Should().Be(UnitOfWorkState.Active, "the refusal comes before the unit is touched");
        await unit.CommitAsync(AbortToken);
        (await fixture.ReadLeaseAsync(new LeaseKey("", kind, resource), AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_refuse_the_fence_once_the_ttl_elapses_inside_an_open_transaction()
    {
        var (kind, resource) = _Create();
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);
        var granted = await host.Leases.GrantAsync(kind, resource, _ShortDuration, AbortToken);
        await using var unit = await fixture.BeginUnitAsync(host, AbortToken);
        await unit.Unit.Leases.FenceAsync(granted.Lease!, AbortToken);

        // The transaction stays open: a clock frozen at transaction start would still call the lease live.
        await Task.Delay(_ShortDuration + TimeSpan.FromMilliseconds(500), AbortToken);
        var late = async () => await unit.Unit.Leases.FenceAsync(granted.Lease!, AbortToken);

        (await late.Should().ThrowAsync<StaleLeaseException>()).Which.Reason.Should().Be(LeaseFenceStatus.Expired);
        await unit.RollbackAsync();
    }

    [Fact]
    public async Task should_mark_only_an_observed_unit_non_retryable_and_only_for_writes()
    {
        var (kind, resource) = _Create();
        await using var host = await fixture.CreateHostAsync(cancellationToken: AbortToken);
        var lease = (await host.Leases.GrantAsync(kind, resource, _LongDuration, AbortToken)).Lease!;

        await using (var owned = await fixture.BeginUnitAsync(host, AbortToken))
        {
            (await owned.Unit.Leases.RenewAsync(lease, _LongDuration, AbortToken)).IsRenewed.Should().BeTrue();
            owned.Unit.IsRetryPrevented.Should().BeFalse("replaying an owned unit re-runs the renewal");
            await owned.CommitAsync(AbortToken);
        }

        await using (var observed = await fixture.EnlistUnitAsync(host, AbortToken))
        {
            await observed.Unit.Leases.FenceAsync(lease, AbortToken);
            observed.Unit.IsRetryPrevented.Should().BeFalse("a fence only reads");

            (await observed.Unit.Leases.SettleAsync(lease, AbortToken)).Should().Be(LeaseSettlementStatus.Settled);
            observed.Unit.IsRetryPrevented.Should().BeTrue();
            await observed.CommitAsync(AbortToken);
        }

        (await fixture.ReadLeaseAsync(new LeaseKey("", kind, resource), AbortToken))!
            .State.Should()
            .Be(StoredLeaseState.Settled);
    }

    private static (string Kind, string Resource) _Create()
    {
        return ($"kind-{Guid.NewGuid():N}"[..20], $"resource-{Guid.NewGuid():N}");
    }
}
