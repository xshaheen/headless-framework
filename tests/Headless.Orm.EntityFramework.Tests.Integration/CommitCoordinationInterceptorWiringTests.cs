// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.CommitCoordination;
using Headless.EntityFramework;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tests.Fixtures;

namespace Tests;

/// <summary>
/// Regression tests for the production interceptor wiring: <c>AddHeadlessDbContext</c> must apply
/// DI-registered <c>IInterceptor</c> services to the context options, because EF Core does NOT
/// auto-discover them from the application service provider. Before the fix, the commit-coordination
/// transaction interceptor never fired on the Headless ORM path, so every coordinated EF transaction
/// drained as rollback — silent work loss. These tests use NO explicit <c>AddInterceptors</c> call:
/// they pass only when the framework wires the interceptor itself.
/// </summary>
public sealed class CommitCoordinationInterceptorWiringTests : TestBase
{
    [Fact]
    public async Task should_drain_on_commit_work_via_framework_wired_interceptor_without_explicit_add_interceptors()
    {
        await using var harness = await WiringHarness.CreateAsync();
        await using var scope = harness.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WiringTestDbContext>();
        var drained = false;

        // The param-free HeadlessDbContext overload: self-sources the request scope; the commit signal
        // must come from CommitCoordinationTransactionInterceptor, which only fires if AddHeadlessDbContext
        // wired it into the options.
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

                ctx.Set<WiringRow>().Add(new WiringRow { Name = "committed" });

                return ctx.SaveChangesAsync(ct);
            },
            cancellationToken: AbortToken
        );

        drained
            .Should()
            .BeTrue("AddHeadlessDbContext must wire DI-registered interceptors so the commit edge is observed");
        (await harness.CountRowsAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_discard_on_commit_work_and_roll_back_rows_when_operation_throws()
    {
        await using var harness = await WiringHarness.CreateAsync();
        await using var scope = harness.Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WiringTestDbContext>();
        var drained = false;

        InvalidOperationException? thrown = null;

        try
        {
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

                    ctx.Set<WiringRow>().Add(new WiringRow { Name = "rolled-back" });
                    await ctx.SaveChangesAsync(ct);

                    throw new InvalidOperationException("boom");
                },
                cancellationToken: AbortToken
            );
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        thrown.Should().NotBeNull("the helper must rethrow the operation's exception");
        thrown!.Message.Should().Be("boom");
        drained.Should().BeFalse("a rolled-back coordinated transaction must discard buffered work");
        (await harness.CountRowsAsync()).Should().Be(0);
    }

    private sealed class WiringHarness : IAsyncDisposable
    {
        // Owned by the harness: the field initializer keeps creation ownership local (CA2000-clean),
        // and DisposeAsync releases it. The in-memory DB lives only while this connection is open.
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private ServiceProvider? _root;

        public ServiceProvider Root => _root!;

        public static async Task<WiringHarness> CreateAsync()
        {
            var harness = new WiringHarness();

            try
            {
                await harness._connection.OpenAsync();

                var services = new ServiceCollection();
                services.AddLogging();
                services.AddSingleton<IClock>(new TestClock { TimeProvider = new FakeTimeProvider() });
                services.AddSingleton<ICurrentTenant>(new TestCurrentTenant { Id = null });
                services.AddSingleton<ICurrentUser>(new TestCurrentUser());
                services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
                services.AddRecordingHeadlessDispatcher();

                // The point under test: commit coordination registers its IInterceptor in DI, and
                // AddHeadlessDbContext is expected to apply it — there is deliberately NO AddInterceptors here.
                services.AddEntityFrameworkCommitCoordination();
                services.AddHeadlessDbContext<WiringTestDbContext>(options => options.UseSqlite(harness._connection));

                harness._root = services.BuildServiceProvider();

                await using (var scope = harness._root.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<WiringTestDbContext>();
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

        public async Task<int> CountRowsAsync()
        {
            await using var scope = _root!.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WiringTestDbContext>();

            return await db.Rows.AsNoTracking().CountAsync();
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
}

public sealed class WiringTestDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<WiringTestDbContext> options
) : HeadlessDbContext(services, options)
{
    public required DbSet<WiringRow> Rows { get; set; }

    public override string DefaultSchema => "";
}

public sealed class WiringRow
{
    public int Id { get; set; }

    public string? Name { get; set; }
}
