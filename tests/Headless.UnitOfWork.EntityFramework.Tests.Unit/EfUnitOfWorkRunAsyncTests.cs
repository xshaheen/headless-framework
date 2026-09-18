// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

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

        await session.Manager.RunAsync(
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
            await session.Manager.RunAsync(
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
            await session.Manager.RunAsync(
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
    public async Task should_return_the_result_of_the_block()
    {
        await using var host = await EfUnitOfWorkHost.CreateAsync();
        await using var session = host.CreateSession();

        var count = await session.Manager.RunAsync(
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

        var count = await session.Manager.RunAsync(
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
