// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace Tests;

/// <summary>
/// <c>RunAsync(db, …)</c> on a context that already carries a live unit joins it: the block receives the
/// owner's unit, runs inside the owner's transaction, and leaves commit and rollback to the owner. This is what
/// lets a service wrap its own work in <c>RunAsync</c> and still compose under a caller that opened the
/// transaction — the propagation an ambient design gave for free, keyed on the context instead of a scope.
/// </summary>
public sealed class EfUnitOfWorkJoinTests : TestBase
{
    private const string _JoinedBlockEndedUnitPattern =
        "*joined a unit of work through RunAsync completed, rolled back, or disposed it*owner of the unit decides its outcome*";

    [Fact]
    public async Task should_join_the_owners_unit_when_run_async_runs_on_a_bound_context()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        await using var owner = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);
        IUnitOfWork? joined = null;

        // when — a callee wraps its own write the way it would if it had opened the transaction itself
        await session.Factory.RunAsync(
            session.Db,
            async (unit, ct) =>
            {
                joined = unit;
                await session.Db.Probes.AddAsync(new ProbeRow { Name = "joined" }, ct);
                await session.Db.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
        );

        // then — same handle, same still-open transaction; the durability question is the rollback test's
        // (the shared SQLite in-memory connection makes a mid-transaction count see uncommitted rows).
        joined.Should().BeSameAs(owner);
        owner.State.Should().Be(UnitOfWorkState.Active, "the joined block must not complete the owner's unit");
        session.Db.Database.CurrentTransaction.Should().NotBeNull("the owner's transaction is still open");

        await owner.CompleteAsync(AbortToken);

        (await host.CountProbeRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_join_when_run_async_nests_inside_run_async()
    {
        // The outer RunAsync owns the unit (and the execution strategy); the inner one joins it.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        var handles = new List<IUnitOfWork>();

        await session.Factory.RunAsync(
            session.Db,
            async (outer, ct) =>
            {
                handles.Add(outer);
                await session.Db.Probes.AddAsync(new ProbeRow { Name = "outer" }, ct);
                await session.Db.SaveChangesAsync(ct);

                await session.Factory.RunAsync(
                    session.Db,
                    async (inner, innerCt) =>
                    {
                        handles.Add(inner);
                        await session.Db.Probes.AddAsync(new ProbeRow { Name = "inner" }, innerCt);
                        await session.Db.SaveChangesAsync(innerCt);
                    },
                    cancellationToken: ct
                );

                outer.State.Should().Be(UnitOfWorkState.Active, "the inner block commits nothing on its own");
                session.Db.Database.CurrentTransaction.Should().NotBeNull();
            },
            cancellationToken: AbortToken
        );

        handles.Should().HaveCount(2);
        handles[1].Should().BeSameAs(handles[0]);
        (await host.CountProbeRowsAsync()).Should().Be(2, "one commit, by the outer block, makes both rows durable");
    }

    [Fact]
    public async Task should_propagate_a_joined_blocks_fault_to_the_owner_and_leave_the_unit_to_it()
    {
        // The joined block owns nothing: its fault reaches the owner unchanged, and the owner's rollback
        // discards the joined block's row with everything else.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        await using var owner = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                async (_, ct) =>
                {
                    await session.Db.Probes.AddAsync(new ProbeRow { Name = "doomed" }, ct);
                    await session.Db.SaveChangesAsync(ct);
                    throw new InvalidOperationException("joined block failed");
                },
                cancellationToken: AbortToken
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("joined block failed");
        owner.State.Should().Be(UnitOfWorkState.Active, "only the owner decides the outcome");

        await owner.RollbackAsync();

        (await host.CountProbeRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_bind_the_same_unit_to_the_connection_beneath_the_context()
    {
        // A raw-ADO helper handed db.Database.GetDbConnection() reaches the context's unit through the
        // connection binding, and RunAsync(connection, …) in an ADO provider joins it the same way.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        var connection = session.Db.Database.GetDbConnection();

        var unitOfWork = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);

        connection.UnitOfWork().Should().BeSameAs(unitOfWork);
        connection.UnitOfWork().Should().BeSameAs(session.Db.UnitOfWork());

        await unitOfWork.CompleteAsync(AbortToken);

        connection.UnitOfWork().Should().BeNull("a terminal unit is evicted from the connection binding too");
    }

    [Fact]
    public async Task should_evict_an_owned_unit_whose_transaction_ended_without_it()
    {
        // The transaction under an owned unit can end without the unit knowing — a pooled context reset, a
        // transaction disposed by hand. The stale binding must not hand that unit to a later RunAsync, which
        // would run its writes outside any transaction and report success: the binding abandons the unit,
        // its retained handle refuses registrations, and the next entry point begins fresh.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        var stale = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);
        var connection = session.Db.Database.GetDbConnection();

        await session.Db.Database.CurrentTransaction!.DisposeAsync();

        session.Db.UnitOfWork().Should().BeNull("an owned unit whose transaction ended is evicted");
        connection.UnitOfWork().Should().BeNull();
        stale.State.Should().Be(UnitOfWorkState.Failed, "the eviction abandons the unit");
        var register = () => stale.OnCompleted(() => ValueTask.CompletedTask);
        register.Should().Throw<ObjectDisposedException>("a retained handle over the evicted unit is dead");

        IUnitOfWork? fresh = null;
        await session.Factory.RunAsync(
            session.Db,
            async (unit, ct) =>
            {
                fresh = unit;
                await session.Db.Probes.AddAsync(new ProbeRow { Name = "fresh" }, ct);
                await session.Db.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
        );

        fresh.Should().NotBeSameAs(stale, "RunAsync begins a new unit instead of joining the stale one");
        (await host.CountProbeRowsAsync()).Should().Be(1, "the fresh unit's own commit made the row durable");
    }

    [Fact]
    public async Task should_refuse_a_joined_block_that_completes_the_owners_unit()
    {
        // A joined block does not own the outcome. One that completes the unit anyway is refused once it
        // returns, instead of leaving the owner to hit "already completed" on its own commit and read it as a
        // swallowed drain fault. The commit itself already happened: the refusal names the misuse, it cannot
        // undo it.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        await using var owner = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                async (unit, ct) =>
                {
                    await session.Db.Probes.AddAsync(new ProbeRow { Name = "committed-by-callee" }, ct);
                    await session.Db.SaveChangesAsync(ct);
                    await unit.CompleteAsync(ct);
                },
                cancellationToken: AbortToken
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(_JoinedBlockEndedUnitPattern);
        owner.State.Should().Be(UnitOfWorkState.Completed);
        (await host.CountProbeRowsAsync())
            .Should()
            .Be(1, "the refusal reports the misuse; it does not undo the commit");
    }

    [Fact]
    public async Task should_refuse_a_joined_block_that_rolls_the_owners_unit_back()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        await using var owner = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                async (unit, _) => await unit.RollbackAsync(),
                cancellationToken: AbortToken
            );

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(_JoinedBlockEndedUnitPattern);
        owner.State.Should().Be(UnitOfWorkState.Failed);
        session.Db.UnitOfWork().Should().BeNull("the rolled-back unit is terminal and evicted");
    }

    [Fact]
    public async Task should_run_a_joined_block_at_the_owners_isolation_and_ignore_the_isolation_argument()
    {
        // The isolation argument describes a unit the call would begin. A joined block runs inside the
        // owner's already-open transaction, so the argument is ignored — proven with a level the provider
        // refuses on a real begin, which the join never attempts.
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();
        const IsolationLevel refused = IsolationLevel.Chaos;

        var begin = () => session.Factory.BeginAsync(session.Db, refused, AbortToken).AsTask();
        await begin.Should().ThrowAsync<ArgumentException>("the test needs a level the provider refuses on begin");
        session.Db.UnitOfWork().Should().BeNull("a refused begin binds nothing");

        await using var owner = await session.Factory.BeginAsync(session.Db, cancellationToken: AbortToken);
        IUnitOfWork? joined = null;

        await session.Factory.RunAsync(
            session.Db,
            (unit, _) =>
            {
                joined = unit;

                return Task.CompletedTask;
            },
            refused,
            AbortToken
        );

        joined.Should().BeSameAs(owner);
        owner.State.Should().Be(UnitOfWorkState.Active);
    }
}
