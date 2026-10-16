// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A context attaches the tenant-stamp change-tracker handlers only when they can stamp something: a tenant-owned
/// model with the write guard on, or a context whose guard setting cannot be read.
/// </summary>
public sealed class TrackerHandlerAttachmentTests : TestBase
{
    [Fact]
    public async Task should_attach_and_stamp_ambient_tenant_when_guarded_tenant_model()
    {
        // given
        await using var harness = await Harness.CreateAsync(guardWrites: true);
        var db = harness.CreateContext<TenantDbContext>();
        using var _ = harness.ChangeTenant("tenant-1");
        var row = new TenantRow { Id = Guid.CreateVersion7() };

        // when
        db.Add(row);

        // then
        _TrackingHandlerCount(db).Should().BeGreaterThan(0);
        row.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task should_not_attach_when_write_guard_is_off()
    {
        // given
        await using var harness = await Harness.CreateAsync(guardWrites: false);

        // when
        var db = harness.CreateContext<TenantDbContext>();

        // then
        _TrackingHandlerCount(db).Should().Be(0);
    }

    [Fact]
    public async Task should_not_attach_when_model_has_no_tenant_owned_type()
    {
        // given
        await using var harness = await Harness.CreateAsync(guardWrites: true);

        // when
        var db = harness.CreateContext<PlainDbContext>();

        // then
        _TrackingHandlerCount(db).Should().Be(0);
    }

    [Fact]
    public async Task should_attach_when_context_has_no_application_provider()
    {
        // given
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(connection).Options;

        // when
        await using var db = new TenantDbContext(options);

        // then
        _TrackingHandlerCount(db).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task should_keep_the_decision_across_pool_leases()
    {
        // given
        await using var harness = await Harness.CreateAsync(guardWrites: true, pooled: true);
        using var _ = harness.ChangeTenant("tenant-1");

        for (var lease = 0; lease < 3; lease++)
        {
            await using var scope = harness.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var row = new TenantRow { Id = Guid.CreateVersion7() };

            // when
            db.Add(row);

            // then
            row.TenantId.Should().Be("tenant-1");
        }
    }

#pragma warning disable EF1001 // Internal EF Core API: the attached handlers are only visible on the state manager.
    private static int _TrackingHandlerCount(DbContext db)
    {
        var stateManager = ((IInfrastructure<IServiceProvider>)db).GetService<IStateManager>();
        var field = stateManager
            .GetType()
            .GetField(nameof(IStateManager.Tracking), BindingFlags.Instance | BindingFlags.NonPublic);

        return (field!.GetValue(stateManager) as Delegate)?.GetInvocationList().Length ?? 0;
    }
#pragma warning restore EF1001

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly List<AsyncServiceScope> _scopes = [];
        private ServiceProvider _provider = null!;

        public static async Task<Harness> CreateAsync(bool guardWrites, bool pooled = false)
        {
            var harness = new Harness();
            await harness._connection.OpenAsync(AbortToken);
            var services = new ServiceCollection();
            services.AddLogging();

            if (pooled)
            {
                services.AddHeadlessDbContextPool<TenantDbContext>(options => options.UseSqlite(harness._connection));
            }
            else
            {
                services.AddHeadlessDbContext<TenantDbContext>(options => options.UseSqlite(harness._connection));
            }

            services.AddHeadlessDbContext<PlainDbContext>(options => options.UseSqlite(harness._connection));

            if (guardWrites)
            {
                services.PostConfigure<TenantGuardOptions>(options => options.GuardWrites = true);
            }

            harness._provider = services.BuildServiceProvider();

            return harness;
        }

        public AsyncServiceScope CreateScope()
        {
            var scope = _provider.CreateAsyncScope();
            _scopes.Add(scope);

            return scope;
        }

        public TContext CreateContext<TContext>()
            where TContext : DbContext => CreateScope().ServiceProvider.GetRequiredService<TContext>();

        public IDisposable ChangeTenant(string tenantId) =>
            _provider.GetRequiredService<ICurrentTenant>().Change(tenantId);

        public async ValueTask DisposeAsync()
        {
            foreach (var scope in _scopes)
            {
                await scope.DisposeAsync();
            }

            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    public sealed class TenantRow : Entity<Guid>, IMultiTenant
    {
        public string? TenantId { get; private set; }
    }

    public sealed class PlainRow : Entity<Guid>;

    public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<TenantRow> Rows => Set<TenantRow>();

        public override string DefaultSchema => "";
    }

    public sealed class PlainDbContext(DbContextOptions<PlainDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<PlainRow> Rows => Set<PlainRow>();

        public override string DefaultSchema => "";
    }
}
