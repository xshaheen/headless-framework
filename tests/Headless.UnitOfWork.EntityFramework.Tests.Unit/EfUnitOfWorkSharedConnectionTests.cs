// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests;

/// <summary>
/// A second <see cref="DbContext" /> built over the connection of a context that carries a unit joins that unit:
/// it adopts the unit's transaction, so both contexts' writes commit or roll back together. This is the shape of
/// a modular monolith with one context per module over one database. The host's contexts all share one SQLite
/// connection, so a context resolved from a second scope is exactly such a sibling.
/// </summary>
public sealed class EfUnitOfWorkSharedConnectionTests : TestBase
{
    private const string _SharedConnectionPattern =
        "*connection beneath this DbContext already carries an active unit of work begun on another DbContext*RunAsync(db*";

    [Fact]
    public async Task should_join_the_owners_unit_when_run_async_runs_on_a_sibling_context_over_the_same_connection()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();
        await using var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken);
        IUnitOfWork? joined = null;

        await owner.Db.Probes.AddAsync(new ProbeRow { Name = "owner" }, AbortToken);
        await owner.Db.SaveChangesAsync(AbortToken);

        // when
        await sibling.Factory.RunAsync(
            sibling.Db,
            async (u, ct) =>
            {
                joined = u;
                await sibling.Db.Probes.AddAsync(new ProbeRow { Name = "sibling" }, ct);
                await sibling.Db.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
        );

        // then
        joined.Should().BeSameAs(unit);
        unit.State.Should().Be(UnitOfWorkState.Active, "the joined block must not complete the owner's unit");
        sibling
            .Db.Database.CurrentTransaction!.GetDbTransaction()
            .Should()
            .BeSameAs(owner.Db.Database.CurrentTransaction!.GetDbTransaction());

        await unit.CompleteAsync(AbortToken);

        (await host.CountProbeRowsAsync()).Should().Be(2);
    }

    [Fact]
    public async Task should_roll_back_the_sibling_writes_when_the_owner_abandons_the_unit()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();

        await using (var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken))
        {
            await sibling.Factory.RunAsync(
                sibling.Db,
                async (_, ct) =>
                {
                    await sibling.Db.Probes.AddAsync(new ProbeRow { Name = "sibling" }, ct);
                    await sibling.Db.SaveChangesAsync(ct);
                },
                cancellationToken: AbortToken
            );

            unit.State.Should().Be(UnitOfWorkState.Active);
        }

        (await host.CountProbeRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_return_the_owners_unit_and_save_inside_it_when_a_sibling_reads_the_accessor()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();
        await using var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken);

        // when — a repository handed only the sibling context reads the unit, then saves plainly
        var read = sibling.Db.UnitOfWork();
        await sibling.Db.Probes.AddAsync(new ProbeRow { Name = "sibling" }, AbortToken);
        await sibling.Db.SaveChangesAsync(AbortToken);

        // then
        read.Should().BeSameAs(unit);

        await unit.RollbackAsync();

        (await host.CountProbeRowsAsync()).Should().Be(0, "the sibling's save ran inside the rolled-back unit");
    }

    [Fact]
    public async Task should_release_the_adopted_transaction_so_the_sibling_begins_fresh_after_the_unit_completes()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();

        await using (var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken))
        {
            sibling.Db.UnitOfWork().Should().BeSameAs(unit);
            await unit.CompleteAsync(AbortToken);
        }

        // then — no stale transaction and no stale unit on the sibling
        sibling.Db.Database.CurrentTransaction.Should().BeNull();
        sibling.Db.UnitOfWork().Should().BeNull();

        await using var next = await sibling.Factory.BeginAsync(sibling.Db, cancellationToken: AbortToken);
        await sibling.Db.Probes.AddAsync(new ProbeRow { Name = "next" }, AbortToken);
        await sibling.Db.SaveChangesAsync(AbortToken);
        await next.CompleteAsync(AbortToken);

        (await host.CountProbeRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_release_the_adopted_transaction_when_the_owner_rolls_back()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();

        await using (var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken))
        {
            sibling.Db.UnitOfWork().Should().BeSameAs(unit);
            await unit.RollbackAsync();
        }

        sibling.Db.Database.CurrentTransaction.Should().BeNull();
    }

    [Fact]
    public async Task should_commit_the_unit_when_a_sibling_that_joined_it_is_disposed_first()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken);

        // A sibling resolved from a shorter-lived scope (a module's request-scoped service) joins, writes, and is
        // disposed while the owner's unit is still open.
        await using (var sibling = host.CreateSession())
        {
            sibling.Db.UnitOfWork().Should().BeSameAs(unit);
            await sibling.Db.Probes.AddAsync(new ProbeRow { Name = "sibling" }, AbortToken);
            await sibling.Db.SaveChangesAsync(AbortToken);
        }

        // when
        await unit.CompleteAsync(AbortToken);

        // then — the release skips the disposed sibling, and disposing it did not end the shared transaction
        unit.State.Should().Be(UnitOfWorkState.Completed);
        (await host.CountProbeRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_roll_back_the_unit_when_a_sibling_that_joined_it_is_disposed_first()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken);

        await using (var sibling = host.CreateSession())
        {
            sibling.Db.UnitOfWork().Should().BeSameAs(unit);
            await sibling.Db.Probes.AddAsync(new ProbeRow { Name = "sibling" }, AbortToken);
            await sibling.Db.SaveChangesAsync(AbortToken);
        }

        // when
        await unit.RollbackAsync();

        // then
        unit.State.Should().Be(UnitOfWorkState.Failed, "a rollback ends the unit as failed");
        (await host.CountProbeRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_refuse_begin_on_a_sibling_context_without_adopting_the_transaction()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();
        await using var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken);

        // when
        var begin = async () => await sibling.Factory.BeginAsync(sibling.Db, cancellationToken: AbortToken);

        // then
        await begin.Should().ThrowAsync<InvalidOperationException>().WithMessage(_SharedConnectionPattern);
        sibling.Db.Database.CurrentTransaction.Should().BeNull("a refused begin leaves the context as it found it");
        unit.State.Should().Be(UnitOfWorkState.Active);
    }

    [Fact]
    public async Task should_refuse_enlist_on_a_sibling_context_with_the_shared_connection_remedy()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var owner = host.CreateSession();
        await using var sibling = host.CreateSession();
        await using var unit = await owner.Factory.BeginAsync(owner.Db, cancellationToken: AbortToken);

        // when — the sibling tries to observe the owner's own transaction as a second unit
        var enlist = () => sibling.Factory.Enlist(sibling.Db, owner.Db.Database.CurrentTransaction!);

        // then
        enlist.Should().Throw<InvalidOperationException>().WithMessage(_SharedConnectionPattern);
    }
}
