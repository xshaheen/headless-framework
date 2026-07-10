// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.CommitCoordination;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class CoordinatedTransactionAmbiguousCommitTests : TestBase
{
    [Fact]
    public async Task should_not_replay_operation_when_an_exception_is_reported_after_commit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        var commitFailure = new OneShotCommittedFailureInterceptor();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkCommitCoordination();
        services.AddSingleton<IInterceptor>(commitFailure);
        services.AddDbContext<AmbiguousCommitDbContext>(
            (sp, options) =>
                options
                    .UseSqlite(connection)
                    .ReplaceService<IExecutionStrategyFactory, OneShotRetryExecutionStrategyFactory>()
                    .AddInterceptors(sp.GetServices<IInterceptor>())
        );
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AmbiguousCommitDbContext>();
        await db.Database.EnsureCreatedAsync(AbortToken);
        commitFailure.Arm();
        var operationCalls = 0;

        var commitOutcomeWasAmbiguous = false;
        try
        {
            await db.ExecuteCoordinatedTransactionAsync(
                async (context, cancellationToken) =>
                {
                    operationCalls++;
                    context.Set<ProbeRow>().Add(new ProbeRow { Name = "committed-once" });
                    await context.SaveChangesAsync(cancellationToken);
                },
                scope.ServiceProvider,
                cancellationToken: AbortToken
            );
        }
        catch (TransientCommitMarkerException)
        {
            commitOutcomeWasAmbiguous = true;
        }

        commitOutcomeWasAmbiguous.Should().BeTrue();
        operationCalls.Should().Be(1);
        (await db.Set<ProbeRow>().AsNoTracking().CountAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_replay_operation_when_a_transient_failure_occurs_before_commit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(AbortToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkCommitCoordination();
        services.AddDbContext<AmbiguousCommitDbContext>(
            (_, options) =>
                options
                    .UseSqlite(connection)
                    .ReplaceService<IExecutionStrategyFactory, OneShotRetryExecutionStrategyFactory>()
        );
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AmbiguousCommitDbContext>();
        await db.Database.EnsureCreatedAsync(AbortToken);
        var operationCalls = 0;

        await db.ExecuteCoordinatedTransactionAsync(
            async (context, cancellationToken) =>
            {
                operationCalls++;
                if (operationCalls == 1)
                {
                    // A failure BEFORE commit starts must propagate to the execution strategy and replay
                    // with a fresh transaction and coordinator — the documented pre-commit half of the contract.
                    throw new TransientCommitMarkerException();
                }

                context.Set<ProbeRow>().Add(new ProbeRow { Name = "committed-after-retry" });
                await context.SaveChangesAsync(cancellationToken);
            },
            scope.ServiceProvider,
            cancellationToken: AbortToken
        );

        operationCalls.Should().Be(2);
        (await db.Set<ProbeRow>().AsNoTracking().CountAsync(AbortToken)).Should().Be(1);
    }

    private sealed class AmbiguousCommitDbContext(DbContextOptions<AmbiguousCommitDbContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProbeRow>().HasKey(x => x.Id);
        }
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }

        public required string Name { get; init; }
    }

    private sealed class TransientCommitMarkerException() : Exception("Simulated unknown commit outcome.");

    private sealed class OneShotCommittedFailureInterceptor : DbTransactionInterceptor
    {
        private int _armed;
        private int _thrown;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (Volatile.Read(ref _armed) != 0 && Interlocked.Exchange(ref _thrown, 1) == 0)
            {
                throw new TransientCommitMarkerException();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class OneShotRetryExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientCommitMarkerException;
    }

    private sealed class OneShotRetryExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
        : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new OneShotRetryExecutionStrategy(dependencies);
    }
}
