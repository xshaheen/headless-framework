// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixtures;

namespace Tests.Tests;

/// <summary>
/// The scope-binding and disposal contract every Headless context base must keep in both registration modes: a
/// context resolved from a scope serves that scope, a factory context owns the scope it uses, and a pooled instance
/// carries nothing from one lease into the next.
/// </summary>
public abstract class HeadlessDbContextLifecycleTestBase<TContext> : TestBase
    where TContext : DbContext, IHeadlessDbContext
{
    protected abstract void ConfigureContext(IServiceCollection services, bool pooled);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task should_dispose_dependencies_created_before_the_context_once(
        bool asynchronousFactory,
        bool asynchronousDisposal
    )
    {
        await using var provider = _BuildProvider(pooled: false, out var probes);
        var factory = provider.GetRequiredService<IDbContextFactory<TContext>>();
        var context = asynchronousFactory ? await factory.CreateDbContextAsync(AbortToken) : factory.CreateDbContext();
        var probe = probes.Should().ContainSingle().Subject;
        probe.DisposeCount.Should().Be(0);

        await _DisposeAsync(context, asynchronousDisposal);

        probe.DisposeCount.Should().Be(1);
        await context.DisposeAsync();
        context.Dispose();
        probe.DisposeCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_bind_the_resolved_context_to_the_resolving_scope(bool pooled)
    {
        await using var provider = _BuildProvider(pooled, out _);
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();

        ((IHeadlessDbContext)context).ServiceProvider.Should().BeSameAs(scope.ServiceProvider);
    }

    [Fact]
    public async Task should_rebind_a_pooled_instance_to_each_leasing_scope()
    {
        await using var provider = _BuildProvider(pooled: true, out _);
        TContext first;

        await using (var scope = provider.CreateAsyncScope())
        {
            first = scope.ServiceProvider.GetRequiredService<TContext>();
        }

        await using var nextScope = provider.CreateAsyncScope();
        var second = nextScope.ServiceProvider.GetRequiredService<TContext>();

        // The pool hands the same instance back, bound to the new scope rather than the disposed one.
        second.Should().BeSameAs(first);
        ((IHeadlessDbContext)second).ServiceProvider.Should().BeSameAs(nextScope.ServiceProvider);
    }

    [Fact]
    public async Task should_return_a_scope_leased_pooled_context_once_when_the_consumer_disposes_it_early()
    {
        await using var provider = _BuildProvider(pooled: true, out _);
        var factory = provider.GetRequiredService<IDbContextFactory<TContext>>();
        TContext scoped;
        TContext other;

        await using (var scope = provider.CreateAsyncScope())
        {
            scoped = scope.ServiceProvider.GetRequiredService<TContext>();

            // A consumer disposing the scope's context early must not hand it back to the pool: the scope still
            // holds it, and the scope's own dispose would otherwise return it a second time.
            await scoped.DisposeAsync();
            scoped.Dispose();

            ((IHeadlessDbContext)scoped).ServiceProvider.Should().BeSameAs(scope.ServiceProvider);
            other = await factory.CreateDbContextAsync(AbortToken);
            other.Should().NotBeSameAs(scoped);
            await other.DisposeAsync();
        }

        // Returned exactly once: three leases are three distinct instances, the two pooled ones among them.
        await using var first = await factory.CreateDbContextAsync(AbortToken);
        await using var second = await factory.CreateDbContextAsync(AbortToken);
        await using var third = await factory.CreateDbContextAsync(AbortToken);
        new[] { first, second, third }.Should().OnlyHaveUniqueItems().And.Contain(scoped).And.Contain(other);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_give_a_pooled_factory_context_a_private_scope_disposed_with_it(bool asynchronousDisposal)
    {
        await using var provider = _BuildProvider(pooled: true, out _);
        var factory = provider.GetRequiredService<IDbContextFactory<TContext>>();
        var context = await factory.CreateDbContextAsync(AbortToken);

        // A factory context is unbound, so asking for its services opens a private scope.
        var services = ((IHeadlessDbContext)context).ServiceProvider;
        services.Should().NotBeSameAs(provider);
        var probe = services.GetRequiredService<DisposalProbe>();
        ((IHeadlessDbContext)context).ServiceProvider.Should().BeSameAs(services);

        await _DisposeAsync(context, asynchronousDisposal);

        probe.DisposeCount.Should().Be(1);

        // The next lease of the same instance opens a fresh scope rather than reusing the disposed one.
        await using var next = await factory.CreateDbContextAsync(AbortToken);
        next.Should().BeSameAs(context);
        ((IHeadlessDbContext)next).ServiceProvider.Should().NotBeSameAs(services);
    }

    private ServiceProvider _BuildProvider(bool pooled, out List<DisposalProbe> probes)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RecordingHeadlessMessageDispatcher>();
        services.AddScoped<DisposalProbe>();
        ConfigureContext(services, pooled);

        var created = new List<DisposalProbe>();
        probes = created;

        if (!pooled)
        {
            // Per-scope options resolve in the context's scope. DI disposes in reverse resolution order, so resolving
            // the probe before the context proves re-entry into context disposal skips no remaining scoped service.
            services.ConfigureDbContext<TContext>(
                (provider, _) => created.Add(provider.GetRequiredService<DisposalProbe>())
            );
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async ValueTask _DisposeAsync(TContext context, bool asynchronous)
    {
        if (asynchronous)
        {
            await context.DisposeAsync();
        }
        else
        {
            context.Dispose();
        }
    }

    private sealed class DisposalProbe : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
