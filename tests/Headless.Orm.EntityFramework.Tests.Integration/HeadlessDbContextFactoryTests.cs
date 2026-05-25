// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixture;
using Tests.Fixtures;

namespace Tests;

[Collection<HeadlessDbContextTestFixture>]
public sealed class HeadlessDbContextFactoryTests(HeadlessDbContextTestFixture fixture)
{
    [Fact]
    public async Task AddHeadlessDbContext_should_register_IDbContextFactory_resolvable_from_root_provider()
    {
        // given — clean container registered via AddHeadlessDbContext (the combined call)
        await using var sp = _BuildProvider();

        // when — resolve the factory and create a context outside any explicit scope
        var factory = sp.GetRequiredService<IDbContextFactory<FactoryTestDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        // then — context is usable and disposes cleanly
        ctx.Should().NotBeNull();
        (await ctx.Database.CanConnectAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task Factory_created_context_should_dispose_its_owned_scope_when_disposed()
    {
        // given — register a scoped tracker so we can observe scope disposal
        await using var sp = _BuildProvider(services => services.AddScoped<ScopeProbe>());

        var factory = sp.GetRequiredService<IDbContextFactory<FactoryTestDbContext>>();

        // when — create + dispose a context; the factory's per-call scope must also dispose
        ScopeProbe probe;
        await using (var ctx = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            ctx.OwnedScope.Should().NotBeNull("factory-created contexts carry their own scope");
            probe = ctx.OwnedScope!.ServiceProvider.GetRequiredService<ScopeProbe>();
            probe.IsDisposed.Should().BeFalse("scope is alive while the context is alive");
        }

        // then
        probe.IsDisposed.Should().BeTrue("scope should dispose when the context disposes");
    }

    [Fact]
    public async Task Multiple_factory_calls_should_yield_independent_contexts_with_independent_scopes()
    {
        // given
        await using var sp = _BuildProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<FactoryTestDbContext>>();

        // when
        await using var a = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await using var b = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        // then — different instances, different scopes
        a.Should().NotBeSameAs(b);
        a.OwnedScope.Should().NotBeSameAs(b.OwnedScope);
    }

    private ServiceProvider _BuildProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Headless.Abstractions.IClock>(fixture.Clock);
        services.AddSingleton<Headless.Abstractions.ICurrentTenant>(fixture.CurrentTenant);
        services.AddSingleton<Headless.Abstractions.ICurrentUser>(fixture.CurrentUser);
        services.AddSingleton<Headless.Abstractions.IGuidGenerator, Headless.Abstractions.SequentialAsStringGuidGenerator>();
        services.AddHeadlessDbContext<FactoryTestDbContext>(options => options.UseNpgsql(fixture.SqlConnectionString));
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private sealed class ScopeProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
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
