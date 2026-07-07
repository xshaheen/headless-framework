// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Codifies the coordinator's savepoint stance: <b>savepoints are invisible to commit coordination</b>.
/// Work enlisted on the coordinator is bound to the OUTERMOST commit edge only — a
/// <c>RollbackToSavepoint</c> discards the database writes made after the savepoint but does NOT discard
/// commit work buffered during that window. Consumers who publish/enqueue inside a partial-rollback
/// region own that mismatch: enlist work only after the last possible partial rollback, or manage the
/// registration manually.
/// </summary>
public sealed class SavepointBehaviorTests : TestBase
{
    [Fact]
    public async Task savepoint_rollback_discards_rows_but_not_buffered_commit_work()
    {
        // Shared in-memory SQLite lives only while a connection is open.
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(AbortToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkCommitCoordination();
        services.AddDbContext<SavepointDbContext>(
            (sp, options) => options.UseSqlite(connection).AddInterceptors(sp.GetServices<IInterceptor>())
        );

        await using var root = services.BuildServiceProvider();

        await using (var setupScope = root.CreateAsyncScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<SavepointDbContext>();
            await setupDb.Database.EnsureCreatedAsync(AbortToken);
        }

        var beforeSavepointDrained = false;
        var insideSavepointDrained = false;

        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SavepointDbContext>();
        var coordinator = () =>
            scope.ServiceProvider.GetRequiredService<ICurrentCommitCoordinator>().Current
            ?? throw new InvalidOperationException("No ambient coordinator.");

        await db.ExecuteCoordinatedTransactionAsync(
            async (ctx, ct) =>
            {
                coordinator()
                    .OnCommit(
                        (_, _) =>
                        {
                            beforeSavepointDrained = true;
                            return ValueTask.CompletedTask;
                        }
                    );

                ctx.Set<SavepointRow>().Add(new SavepointRow { Name = "before-savepoint" });
                await ctx.SaveChangesAsync(ct);

                var transaction = ctx.Database.CurrentTransaction!;
                await transaction.CreateSavepointAsync("sp1", ct);

                // Work buffered INSIDE the savepoint window: the row is rolled back below, the
                // commit work is NOT — the coordinator does not track savepoints.
                coordinator()
                    .OnCommit(
                        (_, _) =>
                        {
                            insideSavepointDrained = true;
                            return ValueTask.CompletedTask;
                        }
                    );

                ctx.Set<SavepointRow>().Add(new SavepointRow { Name = "inside-savepoint" });
                await ctx.SaveChangesAsync(ct);

                await transaction.RollbackToSavepointAsync("sp1", ct);
                ctx.ChangeTracker.Clear();

                ctx.Set<SavepointRow>().Add(new SavepointRow { Name = "after-rollback" });
                await ctx.SaveChangesAsync(ct);
            },
            scope.ServiceProvider,
            cancellationToken: AbortToken
        );

        beforeSavepointDrained.Should().BeTrue("work buffered before the savepoint drains on commit");
        insideSavepointDrained
            .Should()
            .BeTrue(
                "work buffered inside a rolled-back savepoint window STILL drains — the coordinator is savepoint-blind; this is the documented trade-off, not a bug"
            );

        await using var verifyScope = root.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<SavepointDbContext>();
        var names = await verifyDb.Set<SavepointRow>().AsNoTracking().Select(x => x.Name).ToListAsync(AbortToken);

        names.Should().BeEquivalentTo(["before-savepoint", "after-rollback"]);
    }
}

public sealed class SavepointDbContext(DbContextOptions<SavepointDbContext> options) : DbContext(options)
{
    public DbSet<SavepointRow> Rows => Set<SavepointRow>();
}

public sealed class SavepointRow
{
    public int Id { get; set; }

    public string? Name { get; set; }
}
