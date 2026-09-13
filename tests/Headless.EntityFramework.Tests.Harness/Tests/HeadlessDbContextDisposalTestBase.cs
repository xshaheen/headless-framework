// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Fixtures;

namespace Tests.Tests;

public abstract class HeadlessDbContextDisposalTestBase<TContext> : TestBase
    where TContext : DbContext, IHeadlessDbContext
{
    protected abstract void ConfigureContext(IServiceCollection services);

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
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<RecordingHeadlessMessageDispatcher>();
        services.AddScoped<DisposalProbe>();
        ConfigureContext(services);

        // DI disposes in reverse resolution order. Resolve the probe before the context so
        // re-entry into context disposal cannot silently skip the remaining scoped services.
        DisposalProbe? probe = null;
        services.ConfigureDbContext<TContext>((provider, _) => probe = provider.GetRequiredService<DisposalProbe>());
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<TContext>>();
#pragma warning disable VSTHRD103 // Exercise synchronous factory creation and disposal as separate contracts.
        var context = asynchronousFactory ? await factory.CreateDbContextAsync(AbortToken) : factory.CreateDbContext();
        probe.Should().NotBeNull();
        probe!.DisposeCount.Should().Be(0);

        if (asynchronousDisposal)
        {
            await context.DisposeAsync();
        }
        else
        {
            context.Dispose();
        }

        probe.DisposeCount.Should().Be(1);
        await context.DisposeAsync();
        context.Dispose();
#pragma warning restore VSTHRD103
        probe.DisposeCount.Should().Be(1);
    }

    private sealed class DisposalProbe : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
