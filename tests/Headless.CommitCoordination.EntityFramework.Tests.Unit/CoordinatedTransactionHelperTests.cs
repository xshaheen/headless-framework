// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Rollback-discard canary for the <c>DbContext.ExecuteCoordinatedTransactionAsync</c> helper. This is the
/// negative test the silent-work-loss P0 lacked: the helper must signal the coordinator on commit (draining
/// buffered <c>OnCommit</c> work and persisting rows) and leave it un-signalled on rollback (discarding the
/// buffered work and the rows). Runs against real SQLite transactions with the EF commit-coordination
/// interceptor wired, so it exercises the actual enlist -> commit -> signal path, not a mock of it.
/// </summary>
public sealed class CoordinatedTransactionHelperTests
{
    [Fact]
    public async Task commit_drains_buffered_on_commit_work_and_persists_rows()
    {
        await using var harness = await CanaryHarness.CreateAsync();
        var drained = false;

        await harness.Context.ExecuteCoordinatedTransactionAsync(
            (context, ct) =>
            {
                // Buffered work, registered on the ambient coordinator the helper enlisted — the in-memory
                // analog of an outbox row. It must run only when the transaction actually commits.
                harness.AmbientCoordinator.OnCommit(
                    (_, _) =>
                    {
                        drained = true;
                        return ValueTask.CompletedTask;
                    }
                );

                context.Set<CanaryRow>().Add(new CanaryRow { Name = "committed" });

                return context.SaveChangesAsync(ct);
            },
            harness.Services
        );

        drained.Should().BeTrue("a committed coordinated transaction must drain buffered OnCommit work");
        (await harness.CountRowsAsync()).Should().Be(1, "the committed row must be durable");
    }

    [Fact]
    public async Task rollback_discards_buffered_on_commit_work_and_rows()
    {
        await using var harness = await CanaryHarness.CreateAsync();
        var drained = false;

        await harness
            .Context.Invoking(context =>
                context.ExecuteCoordinatedTransactionAsync(
                    async (ctx, ct) =>
                    {
                        harness.AmbientCoordinator.OnCommit(
                            (_, _) =>
                            {
                                drained = true;
                                return ValueTask.CompletedTask;
                            }
                        );

                        ctx.Set<CanaryRow>().Add(new CanaryRow { Name = "rolled-back" });
                        await ctx.SaveChangesAsync(ct);

                        // Operation throws after buffering work — the helper's un-signalled scope dispose must
                        // drain as rollback, discarding the buffered work, and the transaction must roll back.
                        throw new InvalidOperationException("boom");
                    },
                    harness.Services
                )
            )
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");

        drained.Should().BeFalse("a rolled-back coordinated transaction must discard buffered OnCommit work");
        (await harness.CountRowsAsync()).Should().Be(0, "the rolled-back row must not be durable");
    }

    private sealed class CanaryDbContext(DbContextOptions<CanaryDbContext> options) : DbContext(options)
    {
        public DbSet<CanaryRow> Rows => Set<CanaryRow>();
    }

    private sealed class CanaryRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    private sealed class CanaryHarness(
        SqliteConnection connection,
        ServiceProvider root,
        AsyncServiceScope scope
    ) : IAsyncDisposable
    {
        public IServiceProvider Services => scope.ServiceProvider;

        public CanaryDbContext Context => Services.GetRequiredService<CanaryDbContext>();

        public ICommitCoordinator AmbientCoordinator =>
            Services.GetRequiredService<ICurrentCommitCoordinator>().Current
            ?? throw new InvalidOperationException("No ambient coordinator — the helper did not enlist.");

        public static async Task<CanaryHarness> CreateAsync()
        {
            // A shared in-memory SQLite database lives only as long as its connection is open, so the harness
            // owns one open connection for the whole test.
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddEntityFrameworkCommitCoordination();
            services.AddDbContext<CanaryDbContext>(
                (sp, options) =>
                    // Wire the commit-coordination interceptor explicitly so the canary exercises the helper's
                    // enlist -> commit -> signal path directly, independent of EF's app-service-provider
                    // interceptor auto-discovery.
                    options.UseSqlite(connection).AddInterceptors(sp.GetServices<IInterceptor>())
            );

            var root = services.BuildServiceProvider();
            var scope = root.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CanaryDbContext>().Database.EnsureCreatedAsync();

            return new CanaryHarness(connection, root, scope);
        }

        public async Task<int> CountRowsAsync()
        {
            // AsNoTracking queries the database directly (bypassing the change-tracker identity map), so it
            // reflects committed/rolled-back state rather than the in-flight tracked entity.
            return await Context.Set<CanaryRow>().AsNoTracking().CountAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await root.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
