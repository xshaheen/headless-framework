// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupEntityFrameworkUnitOfWorkTests
{
    [Fact]
    public void should_register_the_singleton_factory_and_be_idempotent()
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();

        services.Count(d => d.ServiceType == typeof(IUnitOfWorkFactory)).Should().Be(1);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IUnitOfWorkFactory>().Should().NotBeNull();
    }

    [Fact]
    public void should_resolve_the_factory_from_the_root_under_scope_validation()
    {
        // A singleton with no per-scope state: a hosted service or a singleton takes it directly.
        var services = new ServiceCollection();
        services.AddEntityFrameworkUnitOfWork();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var act = () => provider.GetRequiredService<IUnitOfWorkFactory>();

        act.Should().NotThrow();
    }
}
