// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;

namespace Tests;

[Collection<HeadlessDbContextTestFixture>]
public sealed class HeadlessDbContextFactoryTests(HeadlessDbContextTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_resolve_dbcontext_factory_from_root_provider_when_registered_via_add_headless_dbcontext()
    {
        // given — clean container registered via AddHeadlessDbContext (the combined call)
        await using var sp = _BuildProvider();

        // when — resolve the factory and create a context outside any explicit scope
        var factory = sp.GetRequiredService<IDbContextFactory<FactoryTestDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync(AbortToken);

        // then — context is usable and disposes cleanly
        ctx.Should().NotBeNull();
        (await ctx.Database.CanConnectAsync(AbortToken)).Should().BeTrue();
    }

    [Fact]
    public async Task should_dispose_owned_scope_when_factory_created_context_is_disposed()
    {
        // given — register a scoped tracker so we can observe scope disposal
        await using var sp = _BuildProvider(services => services.AddScoped<ScopeProbe>());

        var factory = sp.GetRequiredService<IDbContextFactory<FactoryTestDbContext>>();

        // when — create + dispose a context; the factory's per-call scope must also dispose
        ScopeProbe probe;
        await using (var ctx = await factory.CreateDbContextAsync(AbortToken))
        {
            var scoped = (IHeadlessDbContextScopeOwner)ctx;
            scoped.OwnedScope.Should().NotBeNull("factory-created contexts carry their own scope");
            probe = scoped.OwnedScope!.ServiceProvider.GetRequiredService<ScopeProbe>();
            probe.IsDisposed.Should().BeFalse("scope is alive while the context is alive");
        }

        // then
        probe.IsDisposed.Should().BeTrue("scope should dispose when the context disposes");
    }

    [Fact]
    public async Task should_yield_independent_contexts_with_independent_scopes_when_factory_called_multiple_times()
    {
        // given
        await using var sp = _BuildProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<FactoryTestDbContext>>();

        // when
        await using var a = await factory.CreateDbContextAsync(AbortToken);
        await using var b = await factory.CreateDbContextAsync(AbortToken);

        // then — different instances, different scopes
        a.Should().NotBeSameAs(b);
        ((IHeadlessDbContextScopeOwner)a).OwnedScope.Should().NotBeSameAs(((IHeadlessDbContextScopeOwner)b).OwnedScope);
    }

    [Fact]
    public void should_use_provider_keyed_guid_generator_for_guid_entity_ids()
    {
        // given
        var providerGuid = Guid.Parse("019b055c-8f3e-7a6d-a71d-94536be55948");
        var unkeyedGuid = Guid.Parse("019b055c-8f3e-7a6d-a71d-94536be55949");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGuidGenerator>(new FixedGuidGenerator(unkeyedGuid));
        services.AddKeyedSingleton<IGuidGenerator>(SequentialGuidType.Version7, new FixedGuidGenerator(providerGuid));
        services.AddHeadlessDbContext<KeyedGuidDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=headless;Username=headless;Password=headless")
        );

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        using var ctx = scope.ServiceProvider.GetRequiredService<KeyedGuidDbContext>();

        var entity = new KeyedGuidEntity { Name = "postgres" };

        // when
        ctx.Entities.Add(entity);

        // then
        entity.Id.Should().Be(providerGuid);
        entity.Id.Should().NotBe(unkeyedGuid);
    }

    [Fact]
    public async Task should_dispose_owned_scope_when_dbcontext_constructor_throws()
    {
        // given — a scoped probe whose disposal we observe, and a context whose ctor throws.
        // The factory must dispose the per-call scope on the failure path so the probe (and any
        // other scoped state) is released; otherwise we leak a scope per failed CreateDbContext.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fixture.Clock);
        services.AddSingleton<ICurrentTenant>(fixture.CurrentTenant);
        services.AddSingleton<ICurrentUser>(fixture.CurrentUser);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddScoped<ScopeProbe>();
        services.AddHeadlessDbContext<ThrowingDbContext>(options => options.UseNpgsql(fixture.SqlConnectionString));

        await using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<ThrowingDbContext>>();

        ScopeProbe? probeFromFailedScope = null;
        ThrowingDbContext.OnConstructed = probe => probeFromFailedScope = probe;

        try
        {
            // when — factory creates a scope, resolves ThrowingDbContext (whose ctor injects the
            // probe and then throws), and must dispose the per-call scope on the catch path
            var act = () => factory.CreateDbContextAsync(AbortToken);
            await act.Should().ThrowAsync<InvalidOperationException>();

            // then — the probe was created inside the per-call scope, and disposing that scope
            // must have disposed the probe
            probeFromFailedScope.Should().NotBeNull("ctor ran far enough to capture the scoped probe");
            probeFromFailedScope!.IsDisposed.Should().BeTrue("factory must dispose the scope on the failure path");
        }
        finally
        {
            ThrowingDbContext.OnConstructed = null;
        }
    }

    private ServiceProvider _BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fixture.Clock);
        services.AddSingleton<ICurrentTenant>(fixture.CurrentTenant);
        services.AddSingleton<ICurrentUser>(fixture.CurrentUser);
        services.AddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
        services.AddHeadlessDbContext<FactoryTestDbContext>(options => options.UseNpgsql(fixture.SqlConnectionString));
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    public sealed class ScopeProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}

file sealed class FixedGuidGenerator(Guid value) : IGuidGenerator
{
    public Guid Create()
    {
        return value;
    }
}

file sealed class KeyedGuidDbContext(HeadlessDbContextServices services, DbContextOptions<KeyedGuidDbContext> options)
    : HeadlessDbContext(services, options)
{
    public DbSet<KeyedGuidEntity> Entities => Set<KeyedGuidEntity>();

    public override string DefaultSchema => "";
}

file sealed class KeyedGuidEntity : IEntity<Guid>
{
    public Guid Id { get; private init; }

    public required string Name { get; init; }

    public IReadOnlyList<object> GetKeys()
    {
        return [Id];
    }
}

/// <summary>
/// Minimal HeadlessDbContext used by the factory tests. Exposes the owned scope's service provider
/// (test-only) so the tests can observe scope disposal without reflection.
/// </summary>
public sealed class FactoryTestDbContext(
    HeadlessDbContextServices services,
    DbContextOptions<FactoryTestDbContext> options
) : HeadlessDbContext(services, options)
{
    public override string DefaultSchema => "";
}

/// <summary>
/// Context whose constructor captures an injected per-scope probe and then throws. Used to verify
/// the factory disposes the per-call scope on the failure path (the scoped probe should observe
/// disposal even though the context was never returned).
/// </summary>
public sealed class ThrowingDbContext : HeadlessDbContext
{
    public static Action<HeadlessDbContextFactoryTests.ScopeProbe>? OnConstructed { get; set; }

    public ThrowingDbContext(
        HeadlessDbContextServices services,
        DbContextOptions<ThrowingDbContext> options,
        HeadlessDbContextFactoryTests.ScopeProbe probe
    )
        : base(services, options)
    {
        OnConstructed?.Invoke(probe);

        throw new InvalidOperationException("intentional ctor failure to exercise scope-disposal path");
    }

    public override string DefaultSchema => "";
}
