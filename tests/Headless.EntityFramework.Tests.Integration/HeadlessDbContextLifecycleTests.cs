// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tests.Tests;

namespace Tests;

public sealed class HeadlessDbContextLifecycleTests : HeadlessDbContextLifecycleTestBase<FactoryTestDbContext>
{
    private const string _ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    protected override void ConfigureContext(IServiceCollection services, bool pooled)
    {
        if (pooled)
        {
            services.AddHeadlessDbContextPool<FactoryTestDbContext>(options => options.UseNpgsql(_ConnectionString));
        }
        else
        {
            services.AddHeadlessDbContext<FactoryTestDbContext>(options => options.UseNpgsql(_ConnectionString));
        }
    }

    [Fact]
    public async Task should_bind_a_context_registered_with_plain_ef_core_to_the_resolving_scope()
    {
        // A plain AddDbContext builds per-scope options in the resolving scope, so the context adopts that scope
        // instead of opening a private one: scoped collaborators such as the current user come from the request.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContextServices();
        services.AddDbContext<FactoryTestDbContext>(options => options.UseNpgsql(_ConnectionString));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<FactoryTestDbContext>();

        ((IHeadlessDbContext)context).ServiceProvider.Should().BeSameAs(scope.ServiceProvider);
    }
}
