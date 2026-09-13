// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

#pragma warning disable CA1707 // Test names follow the repo's readable snake_case convention.

/// <summary>
/// Drives the public <c>DatabaseFacade.EnlistCommitCoordination</c> seam end-to-end over SQLite: the interceptor
/// signals the real EF commit/rollback edges, and a caller that also signals the returned scope (the inbox-runner
/// shape) drains once with nothing logged.
/// </summary>
public sealed class EnlistCommitCoordinationTests : TestBase
{
    [Fact]
    public async Task should_drain_once_when_the_transaction_commits_through_the_interceptor()
    {
        await using var harness = await Harness.CreateAsync();
        await using var scope = harness.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        var calls = 0;

        await using (var transaction = await db.Database.BeginTransactionAsync(AbortToken))
        {
            await using var commitScope = db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider);
            commitScope.Coordinator.OnCommit(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            db.Probes.Add(new ProbeRow { Name = "committed" });
            await db.SaveChangesAsync(AbortToken);
            await transaction.CommitAsync(AbortToken);

            calls.Should().Be(1, "the async commit edge awaits the drain");
            harness
                .Interceptor.EnlistedTransactionCount.Should()
                .Be(0, "the commit edge evicts the finished entry before the scope is disposed");
        }

        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
        (await db.Probes.AsNoTracking().CountAsync(AbortToken)).Should().Be(1);
    }

    [Fact]
    public async Task should_enlist_the_same_transaction_again_after_it_committed_while_the_first_scope_is_alive()
    {
        await using var harness = await Harness.CreateAsync();
        await using var scope = harness.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        await using var first = db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider);
        await transaction.CommitAsync(AbortToken);
        first.Coordinator.State.Should().Be(CommitCoordinatorState.Committed);

        // Drivers that reuse one DbTransaction instance per connection re-enlist the same key before the previous
        // scope is disposed; finished work must not read as a duplicate enlistment.
        await using var second = db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider);

        second.Coordinator.Should().NotBeSameAs(first.Coordinator);
        second.Coordinator.State.Should().Be(CommitCoordinatorState.Active);
        harness.Interceptor.EnlistedTransactionCount.Should().Be(1);
        harness.Logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_discard_when_the_transaction_rolls_back()
    {
        await using var harness = await Harness.CreateAsync();
        await using var scope = harness.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        var calls = 0;
        ICommitCoordinator coordinator;

        await using (var transaction = await db.Database.BeginTransactionAsync(AbortToken))
        {
            await using var commitScope = db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider);
            coordinator = commitScope.Coordinator;
            coordinator.OnCommit(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            db.Probes.Add(new ProbeRow { Name = "rolled-back" });
            await db.SaveChangesAsync(AbortToken);
            await transaction.RollbackAsync(AbortToken);
        }

        calls.Should().Be(0);
        coordinator.State.Should().Be(CommitCoordinatorState.RolledBack);
        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
        (await db.Probes.AsNoTracking().CountAsync(AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_drain_once_with_no_warning_when_the_caller_also_signals_committed()
    {
        await using var harness = await Harness.CreateAsync();
        await using var scope = harness.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        var calls = 0;

        await using (var transaction = await db.Database.BeginTransactionAsync(AbortToken))
        {
            await using var commitScope = db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider);
            commitScope.Coordinator.OnCommit(() =>
            {
                calls++;

                return ValueTask.CompletedTask;
            });

            db.Probes.Add(new ProbeRow { Name = "committed" });
            await db.SaveChangesAsync(AbortToken);
            await transaction.CommitAsync(AbortToken);

            // The inbox runner settles the outcome itself after CommitAsync returns; the interceptor already did.
            await commitScope.SignalAsync(CommitOutcome.Committed);
        }

        calls.Should().Be(1);
        harness.Logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_before_pushing_a_scope_when_the_token_is_already_cancelled()
    {
        await using var harness = await Harness.CreateAsync();
        await using var scope = harness.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        var act = () => db.Database.EnlistCommitCoordination(transaction, scope.ServiceProvider, cancelled.Token);

        act.Should().Throw<OperationCanceledException>();
        harness.Interceptor.EnlistedTransactionCount.Should().Be(0);
        scope.ServiceProvider.GetRequiredService<ICurrentCommitCoordinator>().Current.Should().BeNull();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private ServiceProvider? _root;

        public ServiceProvider Root => _root!;

        public CapturingLoggerProvider Logs { get; } = new();

        public CommitCoordinationTransactionInterceptor Interceptor =>
            Root.GetRequiredService<CommitCoordinationTransactionInterceptor>();

        public static async Task<Harness> CreateAsync()
        {
            var harness = new Harness();

            try
            {
                await harness._connection.OpenAsync();

                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddProvider(harness.Logs));
                services.AddEntityFrameworkCommitCoordination();
                services.AddDbContext<ProbeDbContext>(
                    (sp, options) =>
                        options.UseSqlite(harness._connection).AddInterceptors(sp.GetServices<IInterceptor>())
                );

                harness._root = services.BuildServiceProvider();

                await using (var scope = harness._root.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<ProbeDbContext>().Database.EnsureCreatedAsync();
                }

                return harness;
            }
            catch
            {
                await harness.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_root is not null)
            {
                await _root.DisposeAsync();
            }

            await _connection.DisposeAsync();
        }
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Probes => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}
