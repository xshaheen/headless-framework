// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
}
