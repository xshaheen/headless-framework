// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// The registration a context's change tracker makes when it tracks an entity over an active unit's connection
/// (the hook <c>HeadlessDbContext</c> installs): the unit then counts the context in its replay decision without
/// the context adopting the unit's transaction.
/// </summary>
public sealed class DbContextReplayRegistrationTests : TestBase
{
    [Fact]
    public async Task should_register_nothing_when_no_unit_runs_over_the_connection()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        DbContextUnitOfWorkBinding.RegisterTrackingContext(session.Db).Should().BeNull();
    }

    [Fact]
    public async Task should_refuse_the_replay_when_a_registered_sibling_still_tracks_a_row()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();
        var conflict = new DbUpdateConcurrencyException("Simulated optimistic concurrency fault.");
        IUnitOfWork? registeredWith = null;
        var blockRuns = 0;

        var act = () =>
            owner.Factory.RunAsync(
                owner.Db,
                (unit, _) =>
                {
                    blockRuns++;
                    sibling.Db.Probes.Add(new ProbeRow { Name = "audit" });
                    registeredWith = DbContextUnitOfWorkBinding.RegisterTrackingContext(sibling.Db);

                    // Registering is not joining: the sibling has not adopted the unit's transaction.
                    sibling.Db.Database.CurrentTransaction.Should().BeNull();
                    registeredWith.Should().BeSameAs(unit);

                    return Task.FromException(conflict);
                },
                IsolationLevel.ReadCommitted,
                EntityFrameworkConflictRetry.Create(),
                AbortToken
            );

        (await act.Should().ThrowAsync<DbUpdateConcurrencyException>()).Which.Should().BeSameAs(conflict);
        blockRuns.Should().Be(1, "a replay would save the row the sibling still tracks a second time");
        (await host.CountProbeRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_keep_the_block_replayable_when_its_own_context_registers()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        var blockRuns = 0;

        await owner.Factory.RunAsync(
            owner.Db,
            async (unit, ct) =>
            {
                blockRuns++;
                owner.Db.Probes.Add(new ProbeRow { Name = "audit" });

                // The block's own context is cleared by the replay itself, so it is never counted as a sibling.
                DbContextUnitOfWorkBinding.RegisterTrackingContext(owner.Db).Should().BeSameAs(unit);

                if (blockRuns == 1)
                {
                    throw new DbUpdateConcurrencyException("Simulated optimistic concurrency fault.");
                }

                await owner.Db.SaveChangesAsync(ct);
            },
            IsolationLevel.ReadCommitted,
            EntityFrameworkConflictRetry.Create(),
            AbortToken
        );

        blockRuns.Should().Be(2);
        (await host.CountProbeRowsAsync()).Should().Be(1);
    }
}
