// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Verifies the U5 generalization: AddDiRegisteredInterceptorsConfiguration&lt;TContext&gt;() attaches DI-registered
/// interceptors to a consumer's OWN plain AddDbContext&lt;TContext&gt; — not only AddHeadlessDbContext. There is
/// deliberately NO AddInterceptors call; the test passes only when the registered IDbContextOptionsConfiguration
/// wires the commit interceptor.
/// </summary>
public sealed class DiRegisteredInterceptorsConfigurationTests : TestBase
{
    [Fact]
    public async Task should_attach_di_interceptors_to_a_plain_add_dbcontext()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(AbortToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkCommitCoordination();
        services.AddDiRegisteredInterceptorsConfiguration<PlainDbContext>();
        // Plain AddDbContext — NO AddHeadlessDbContext, NO AddInterceptors.
        services.AddDbContext<PlainDbContext>(options => options.UseSqlite(connection));

        await using var root = services.BuildServiceProvider();

        await using (var setup = root.CreateAsyncScope())
        {
            await setup.ServiceProvider.GetRequiredService<PlainDbContext>().Database.EnsureCreatedAsync(AbortToken);
        }

        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlainDbContext>();
        var drained = false;

        await db.ExecuteCoordinatedTransactionAsync(
            async (ctx, ct) =>
            {
                scope
                    .ServiceProvider.GetRequiredService<ICurrentCommitCoordinator>()
                    .Current!.OnCommit(
                        (_, _) =>
                        {
                            drained = true;
                            return ValueTask.CompletedTask;
                        }
                    );

                ((PlainDbContext)ctx).Set<PlainRow>().Add(new PlainRow { Name = "x" });
                await ctx.SaveChangesAsync(ct);
            },
            scope.ServiceProvider,
            cancellationToken: AbortToken
        );

        drained
            .Should()
            .BeTrue("AddDiRegisteredInterceptorsConfiguration must wire the interceptor into a plain AddDbContext");
    }

    private sealed class PlainDbContext(DbContextOptions<PlainDbContext> options) : DbContext(options)
    {
        public DbSet<PlainRow> Rows => Set<PlainRow>();
    }

    private sealed class PlainRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}
