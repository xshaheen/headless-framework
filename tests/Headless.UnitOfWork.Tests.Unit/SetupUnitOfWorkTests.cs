// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupUnitOfWorkTests
{
    [Fact]
    public void should_register_a_single_singleton_factory_and_be_idempotent()
    {
        var services = new ServiceCollection();

        services.AddUnitOfWork();
        services.AddUnitOfWork();

        services.Count(d => d.ServiceType == typeof(IUnitOfWorkFactory)).Should().Be(1);
        services
            .Single(d => d.ServiceType == typeof(IUnitOfWorkFactory))
            .Lifetime.Should()
            .Be(ServiceLifetime.Singleton);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IUnitOfWorkFactory>().Should().BeOfType<UnitOfWorkFactory>();
    }

    [Fact]
    public void should_resolve_one_factory_from_the_root_and_from_every_scope()
    {
        // No per-scope state: a singleton or hosted service takes the factory directly, and every scope sees the
        // same instance. Scope validation has nothing to report.
        var services = new ServiceCollection();
        services.AddUnitOfWork();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var root = provider.GetRequiredService<IUnitOfWorkFactory>();

        root.Should().BeSameAs(scope1.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>());
        root.Should().BeSameAs(scope2.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>());
    }
}
