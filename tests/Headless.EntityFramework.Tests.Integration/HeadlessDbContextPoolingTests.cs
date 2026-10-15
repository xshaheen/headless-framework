// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Pooling behavior that only a reused instance or an unbound context can show: the change-tracker handlers come
/// back on every lease, a private scope opens only when needed, and a context with several constructors still
/// registers.
/// </summary>
public sealed class HeadlessDbContextPoolingTests : TestBase
{
    [Fact]
    public async Task should_stamp_the_tenant_on_add_after_the_instance_returns_to_the_pool()
    {
        // given — EF clears the change-tracker handlers when an instance returns to the pool and restores only those
        // its first-lease snapshot captured. The tenant stamp runs in the Tracking handler, at Add, before any save.
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessDbContextPool<PooledTenantContext>(options => options.UseSqlite(connection));
        builder.AddHeadlessTenancy(tenancy => tenancy.EntityFramework(ef => ef.GuardTenantWrites()));
        using var host = builder.Build();
        var factory = host.Services.GetRequiredService<IDbContextFactory<PooledTenantContext>>();
        var currentTenant = host.Services.GetRequiredService<ICurrentTenant>();

        PooledTenantContext first;
        await using (first = await factory.CreateDbContextAsync(AbortToken)) { }

        await using var second = await factory.CreateDbContextAsync(AbortToken);
        second.Should().BeSameAs(first, "the pool hands the returned instance back");

        // when / then — with a tenant, Add stamps it; without one, Add throws. Both happen only through the handler.
        using (currentTenant.Change("tenant-b"))
        {
            var row = new TenantRow { Id = Guid.NewGuid() };
            second.Add(row);
            row.TenantId.Should().Be("tenant-b");
        }

        var act = () => second.Add(new TenantRow { Id = Guid.NewGuid() });
        act.Should().Throw<MissingTenantContextException>();
    }

    [Fact]
    public async Task should_open_a_private_scope_only_when_a_scoped_collaborator_is_needed()
    {
        // given — a context created outside any scope, over options that carry the root provider
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContextServices();
        await using var root = services.BuildServiceProvider(validateScopes: true);
        var counting = new ScopeCountingServiceProvider(root);
        var options = new DbContextOptionsBuilder<FactoryTestDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .UseApplicationServiceProvider(counting)
            .Options;
        var context = new FactoryTestDbContext(options);

        // when — reading the tenant and tracking an entity need only singletons
        _ = context.TenantId;
        _ = context.ChangeTracker.Entries().ToList();

        // then
        counting.ScopesCreated.Should().Be(0);
        var scoped = ((IHeadlessDbContext)context).ServiceProvider;
        counting.ScopesCreated.Should().Be(1);
        ((IHeadlessDbContext)context).ServiceProvider.Should().BeSameAs(scoped);
        await context.DisposeAsync();
        var act = () => scoped.GetRequiredService<IServiceScopeFactory>();
        act.Should().Throw<ObjectDisposedException>("the private scope is disposed with the context");
    }

    [Fact]
    public async Task should_not_carry_a_forgotten_unit_of_work_into_the_next_lease()
    {
        // given — a pooled context whose caller enlisted a unit of work and returned the context without completing it
        await using var keeper = new SqliteConnection("Data Source=pool-unit-binding;Mode=Memory;Cache=Shared");
        await keeper.OpenAsync(AbortToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContextPool<FactoryTestDbContext>(
            options => options.UseSqlite(keeper.ConnectionString),
            poolSize: 1
        );
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var factory = provider.GetRequiredService<IDbContextFactory<FactoryTestDbContext>>();
        var unitOfWorkFactory = provider.GetRequiredService<IUnitOfWorkFactory>();

        var first = await factory.CreateDbContextAsync(AbortToken);
        await using var forgottenTransaction = await first.Database.BeginTransactionAsync(AbortToken);
#pragma warning disable CA2000 // Not a leak to fix: the forgotten handle is the scenario under test.
        unitOfWorkFactory.Enlist(first, forgottenTransaction);
#pragma warning restore CA2000
        await first.DisposeAsync();

        // when
        await using var second = await factory.CreateDbContextAsync(AbortToken);

        // then — the instance comes back from the pool without the unit its previous caller forgot
        second.Should().BeSameAs(first, "a pool of one hands back the same instance");
        second.UnitOfWork().Should().BeNull();
        await using var transaction = await second.Database.BeginTransactionAsync(AbortToken);
        await using var unit = unitOfWorkFactory.Enlist(second, transaction);
        second.UnitOfWork().Should().BeSameAs(unit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_register_a_context_with_several_public_constructors(bool viaFactory)
    {
        // given — DI picks the longest constructor it can satisfy, as it does for a plain EF Core registration
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<TwoConstructorContext>(options =>
            options.UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
        );
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        // when
        TwoConstructorContext context;
        if (viaFactory)
        {
            context = await provider
                .GetRequiredService<IDbContextFactory<TwoConstructorContext>>()
                .CreateDbContextAsync(AbortToken);
        }
        else
        {
            await using var scope = provider.CreateAsyncScope();
            context = scope.ServiceProvider.GetRequiredService<TwoConstructorContext>();
        }

        // then
        context.CurrentUser.Should().NotBeNull();
        await context.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_bind_a_context_whose_constructor_reads_its_service_provider(bool pooled)
    {
        // given — reading ServiceProvider in the constructor resolves a fallback scope before the registration binds
        var services = new ServiceCollection();
        services.AddLogging();
        const string connectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

        if (pooled)
        {
            services.AddHeadlessDbContextPool<EagerServicesContext>(options => options.UseNpgsql(connectionString));
        }
        else
        {
            services.AddHeadlessDbContext<EagerServicesContext>(options => options.UseNpgsql(connectionString));
        }

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        // when
        var context = scope.ServiceProvider.GetRequiredService<EagerServicesContext>();

        // then — the registration's binding replaces the fallback
        ((IHeadlessDbContext)context)
            .ServiceProvider.Should()
            .BeSameAs(scope.ServiceProvider);
    }

    private sealed class PooledTenantContext(DbContextOptions<PooledTenantContext> options) : HeadlessDbContext(options)
    {
        public override string? DefaultSchema => null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TenantRow>().HasKey(x => x.Id);
        }
    }

    private sealed class TenantRow : IMultiTenant
    {
        public Guid Id { get; set; }

        public string? TenantId { get; set; }
    }

    private sealed class TwoConstructorContext : HeadlessDbContext
    {
        public TwoConstructorContext(DbContextOptions<TwoConstructorContext> options)
            : base(options) { }

        public TwoConstructorContext(DbContextOptions<TwoConstructorContext> options, ICurrentUser currentUser)
            : base(options)
        {
            CurrentUser = currentUser;
        }

        public ICurrentUser? CurrentUser { get; }

        public override string? DefaultSchema => null;
    }

    private sealed class EagerServicesContext : HeadlessDbContext
    {
        public EagerServicesContext(DbContextOptions<EagerServicesContext> options)
            : base(options)
        {
            _ = ((IHeadlessDbContext)this).ServiceProvider;
        }

        public override string? DefaultSchema => null;
    }

    // Counts the scopes the context opens through its application provider; everything else passes through.
    private sealed class ScopeCountingServiceProvider(IServiceProvider inner) : IServiceProvider, IServiceScopeFactory
    {
        public int ScopesCreated { get; private set; }

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? this : inner.GetService(serviceType);

        public IServiceScope CreateScope()
        {
            ScopesCreated++;

            return inner.GetRequiredService<IServiceScopeFactory>().CreateScope();
        }
    }
}
