// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// <c>RunAsync(db, …)</c> under a retrying execution strategy against SQLite in-memory: a transient failure
/// before commit replays with a fresh unit of work; a failure after commit started (or after
/// <c>PreventRetry()</c>) is rethrown outside the strategy so EF cannot replay a possibly-committed block.
/// Ported from the coordinated-transaction ambiguous-commit suite.
/// </summary>
public sealed class EfUnitOfWorkRunAsyncTests : TestBase
{
    [Fact]
    public async Task should_replay_the_block_when_a_transient_failure_occurs_before_commit()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        await using var session = host.CreateSession();
        var operationCalls = 0;
        var drains = 0;

        await session.Factory.RunAsync(
            session.Db,
            async (unitOfWork, ct) =>
            {
                operationCalls++;

                // Each attempt enlists on its own unit; only the attempt that completes may drain.
                unitOfWork.OnCompleted(() =>
                {
                    drains++;

                    return ValueTask.CompletedTask;
                });

                if (operationCalls == 1)
                {
                    // A failure BEFORE commit starts must reach the execution strategy and replay with a
                    // fresh transaction and a fresh unit of work — the pre-commit half of the contract.
                    throw new TransientMarkerException();
                }

                await session.Db.Probes.AddAsync(new ProbeRow { Name = "committed-after-retry" }, ct);
                await session.Db.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
        );

        operationCalls.Should().Be(2, "the strategy replayed the block once");
        drains.Should().Be(1, "the abandoned attempt's registrations were dropped with it");
        (await host.CountProbeRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_not_replay_the_block_when_an_exception_is_reported_after_commit_started()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(
            configureServices: services => services.AddSingleton<IInterceptor>(new CommitFailureInterceptor()),
            configureOptions: options =>
                options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        await using var session = host.CreateSession();
        var commitFailure = host
            .Root.GetRequiredService<IEnumerable<IInterceptor>>()
            .OfType<CommitFailureInterceptor>()
            .Single();
        commitFailure.Arm();
        var operationCalls = 0;
        var observed = false;

        try
        {
            await session.Factory.RunAsync(
                session.Db,
                async (unitOfWork, ct) =>
                {
                    operationCalls++;
                    await session.Db.Probes.AddAsync(new ProbeRow { Name = "committed-once" }, ct);
                    await session.Db.SaveChangesAsync(ct);
                },
                cancellationToken: AbortToken
            );
        }
        catch (TransientMarkerException)
        {
            // The commit interceptor throws AFTER the SQLite commit finished; the outcome may be ambiguous.
            observed = true;
        }

        observed.Should().BeTrue("the post-commit fault is surfaced to the caller");
        operationCalls.Should().Be(1, "the block must not be replayed after commit started");
        (await host.CountProbeRowsAsync()).Should().Be(1, "the row was committed before the fault");
    }

    [Fact]
    public async Task should_not_replay_the_block_after_prevent_retry_was_called()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        await using var session = host.CreateSession();
        var operationCalls = 0;
        var observed = false;

        try
        {
            await session.Factory.RunAsync(
                session.Db,
                (unitOfWork, ct) =>
                {
                    operationCalls++;
                    unitOfWork.PreventRetry();

                    return Task.FromException(new TransientMarkerException());
                },
                cancellationToken: AbortToken
            );
        }
        catch (TransientMarkerException)
        {
            observed = true;
        }

        observed.Should().BeTrue();
        operationCalls.Should().Be(1, "IsRetryPrevented routes the failure out of the strategy's replay loop");
        (await host.CountProbeRowsAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_replay_a_relative_update_once_when_a_save_succeeded_before_the_transient_fault()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        var id = await _SeedProbeAsync(host, counter: 100);
        await using var session = host.CreateSession();
        var operationCalls = 0;

        await session.Factory.RunAsync(
            session.Db,
            async (_, ct) =>
            {
                operationCalls++;
                var row = await session.Db.Probes.SingleAsync(x => x.Id == id, ct);
                row.Counter -= 10;
                await session.Db.SaveChangesAsync(ct);

                if (operationCalls == 1)
                {
                    // The save above succeeded inside the transaction the fault now rolls back.
                    throw new TransientMarkerException();
                }
            },
            cancellationToken: AbortToken
        );

        operationCalls.Should().Be(2);
        (await _ReadCounterAsync(host, id))
            .Should()
            .Be(90, "the replay must start from the rolled-back row, not the accepted tracker");
    }

    [Fact]
    public async Task should_write_an_idempotent_update_again_when_the_block_replays()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        var id = await _SeedProbeAsync(host, counter: 100);
        await using var session = host.CreateSession();
        var operationCalls = 0;

        await session.Factory.RunAsync(
            session.Db,
            async (_, ct) =>
            {
                operationCalls++;
                var row = await session.Db.Probes.SingleAsync(x => x.Id == id, ct);
                row.Counter = 5;
                await session.Db.SaveChangesAsync(ct);

                if (operationCalls == 1)
                {
                    throw new TransientMarkerException();
                }
            },
            cancellationToken: AbortToken
        );

        operationCalls.Should().Be(2);
        (await _ReadCounterAsync(host, id)).Should().Be(5, "the replayed save must not be skipped as already applied");
    }

    [Fact]
    public async Task should_surface_the_transient_fault_without_replay_when_the_context_tracked_entities_before_the_block()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync(configureOptions: options =>
            options.ReplaceService<IExecutionStrategyFactory, RetryExecutionStrategyFactory>()
        );
        var id = await _SeedProbeAsync(host, counter: 100);
        await using var session = host.CreateSession();
        var row = await session.Db.Probes.SingleAsync(x => x.Id == id, AbortToken);
        var operationCalls = 0;

        var act = () =>
            session.Factory.RunAsync(
                session.Db,
                async (_, ct) =>
                {
                    operationCalls++;
                    row.Counter -= 10;
                    await session.Db.SaveChangesAsync(ct);

                    throw new TransientMarkerException();
                },
                cancellationToken: AbortToken
            );

        await act.Should().ThrowAsync<TransientMarkerException>();
        operationCalls.Should().Be(1, "a replay cannot restore entities the caller tracked before the block");
        (await _ReadCounterAsync(host, id)).Should().Be(100);
    }

    [Fact]
    public async Task should_return_the_result_of_the_block()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var count = await session.Factory.RunAsync(
            session.Db,
            async (unitOfWork, ct) =>
            {
                await session.Db.Probes.AddAsync(new ProbeRow { Name = "result" }, ct);
                await session.Db.SaveChangesAsync(ct);

                return 42;
            },
            cancellationToken: AbortToken
        );

        count.Should().Be(42);
        (await host.CountProbeRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_log_and_return_the_result_when_the_drain_faults_after_a_durable_commit()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var count = await session.Factory.RunAsync(
            session.Db,
            async (unitOfWork, ct) =>
            {
                unitOfWork.OnCompleted(() => throw new InvalidOperationException("drain down"));
                await session.Db.Probes.AddAsync(new ProbeRow { Name = "durable" }, ct);
                await session.Db.SaveChangesAsync(ct);

                return 7;
            },
            cancellationToken: AbortToken
        );

        // The commit landed; surfacing the drain fault would invite a retry that double-applies the block — the
        // same policy as the Npgsql/SqlClient RunAsync.
        count.Should().Be(7);
        (await host.CountProbeRowsAsync()).Should().Be(1);
        session.Logs.Should().Contain(e => e.Message.Contains("Post-commit drain faulted", StringComparison.Ordinal));
    }

    private static async Task<int> _SeedProbeAsync(EfUnitOfWorkHost host, int counter)
    {
        await using var scope = host.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        var row = new ProbeRow { Name = "seed", Counter = counter };
        db.Probes.Add(row);
        await db.SaveChangesAsync(AbortToken);

        return row.Id;
    }

    private static async Task<int> _ReadCounterAsync(EfUnitOfWorkHost host, int id)
    {
        await using var scope = host.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        return await db.Probes.AsNoTracking().Where(x => x.Id == id).Select(x => x.Counter).SingleAsync(AbortToken);
    }

    /// <summary>
    /// Throws a retryable marker after the provider's commit finished, simulating EF's
    /// "commit outcome unknown" fault shape.
    /// </summary>
    private sealed class CommitFailureInterceptor : DbTransactionInterceptor
    {
        private int _armed;
        private int _thrown;

        public void Arm()
        {
            Volatile.Write(ref _armed, 1);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Volatile.Read(ref _armed) != 0 && Interlocked.Exchange(ref _thrown, 1) == 0)
            {
                throw new TransientMarkerException();
            }

            return Task.CompletedTask;
        }
    }
}
