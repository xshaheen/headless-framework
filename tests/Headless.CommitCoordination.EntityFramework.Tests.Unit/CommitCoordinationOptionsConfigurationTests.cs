// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Regression tests for the U1 <c>IDbContextOptionsConfiguration&lt;TContext&gt;</c> auto-attach seam:
/// a plain <c>AddDbContext&lt;TContext&gt;</c> (NO consumer <c>AddInterceptors</c>) must observe the
/// commit-coordination interceptor purely because the DI-registered options configuration attaches it
/// while EF Core builds the options. EF Core does NOT auto-discover <c>IInterceptor</c> registrations,
/// so these tests pass only when the configuration wires the interceptor itself.
/// </summary>
public sealed class CommitCoordinationOptionsConfigurationTests
{
    [Fact]
    public async Task should_attach_commit_interceptor_to_plain_add_db_context_without_explicit_add_interceptors()
    {
        await using var harness = await ConfigHarness.CreateAsync();

        await using var scope = harness.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeContext>();
        var drained = false;

        // The commit signal must come from CommitCoordinationTransactionInterceptor, which only fires if
        // the U1 options configuration attached it to the plain AddDbContext options.
        await db.ExecuteCoordinatedTransactionAsync(
            (ctx, ct) =>
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

                ctx.Set<ProbeRow>().Add(new ProbeRow { Name = "committed" });

                return ctx.SaveChangesAsync(ct);
            },
            scope.ServiceProvider,
            cancellationToken: TestContext.Current.CancellationToken
        );

        drained
            .Should()
            .BeTrue("the U1 options configuration must attach the commit interceptor to a plain AddDbContext");

        var present = _ResolveAttachedInterceptors(scope.ServiceProvider);
        present.OfType<CommitCoordinationTransactionInterceptor>().Should().ContainSingle();
    }

    [Fact]
    public async Task should_attach_commit_interceptor_only_once_when_consumer_also_adds_it()
    {
        await using var harness = await ConfigHarness.CreateAsync(consumerAlsoAddsInterceptor: true);

        await using var scope = harness.Root.CreateAsyncScope();

        var present = _ResolveAttachedInterceptors(scope.ServiceProvider);

        present
            .OfType<CommitCoordinationTransactionInterceptor>()
            .Should()
            .ContainSingle("dedup by reference must prevent attaching the interceptor twice");
    }

    [Fact]
    public async Task should_attach_only_commit_interceptor_not_unrelated_di_interceptors()
    {
        await using var harness = await ConfigHarness.CreateAsync(registerUnrelatedInterceptor: true);

        await using var scope = harness.Root.CreateAsyncScope();

        var present = _ResolveAttachedInterceptors(scope.ServiceProvider);

        present.OfType<CommitCoordinationTransactionInterceptor>().Should().ContainSingle();
        present
            .OfType<UnrelatedInterceptor>()
            .Should()
            .BeEmpty("the configuration scopes attachment to the commit interceptor only");
    }

    private static IReadOnlyList<IInterceptor> _ResolveAttachedInterceptors(IServiceProvider serviceProvider)
    {
        var db = serviceProvider.GetRequiredService<ProbeContext>();
        var coreExtension = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>();

        return coreExtension?.Interceptors?.ToArray() ?? [];
    }

    private sealed class ConfigHarness : IAsyncDisposable
    {
        // Owned by the harness: the field initializer keeps creation ownership local (CA2000-clean),
        // and DisposeAsync releases it. The in-memory DB lives only while this connection is open.
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private ServiceProvider? _root;

        public ServiceProvider Root => _root!;

        public static async Task<ConfigHarness> CreateAsync(
            bool consumerAlsoAddsInterceptor = false,
            bool registerUnrelatedInterceptor = false
        )
        {
            var harness = new ConfigHarness();

            try
            {
                await harness._connection.OpenAsync();

                var services = new ServiceCollection();
                services.AddLogging();

                services.AddEntityFrameworkCommitCoordination();

                if (registerUnrelatedInterceptor)
                {
                    services.AddSingleton<IInterceptor, UnrelatedInterceptor>();
                }

                // Deliberately PLAIN AddDbContext: no AddInterceptors unless the dedup scenario forces it.
                // EF applies IDbContextOptionsConfiguration instances in registration order, so the consumer's
                // optionsAction (itself wrapped as a configuration) is registered FIRST and our auto-attach
                // configuration runs AFTER it — letting the dedup-by-reference observe the consumer's interceptor.
                services.AddDbContext<ProbeContext>(
                    (sp, options) =>
                    {
                        options.UseSqlite(harness._connection);

                        if (consumerAlsoAddsInterceptor)
                        {
                            options.AddInterceptors(sp.GetServices<IInterceptor>());
                        }
                    }
                );

                services.AddCommitCoordinationDbContextConfiguration(typeof(ProbeContext));

                harness._root = services.BuildServiceProvider();

                await using (var scope = harness._root.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ProbeContext>();
                    await db.Database.EnsureCreatedAsync();
                }

                return harness;
            }
            catch
            {
                // Setup failed before the harness was handed back; release what it already owns.
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

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Probes => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    private sealed class UnrelatedInterceptor : IInterceptor;
}
