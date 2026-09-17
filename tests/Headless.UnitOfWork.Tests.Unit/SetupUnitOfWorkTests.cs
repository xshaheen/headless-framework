// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupUnitOfWorkTests
{
    [Fact]
    public void should_register_a_single_scoped_manager_and_be_idempotent()
    {
        var services = new ServiceCollection();

        services.AddUnitOfWork();
        services.AddUnitOfWork();

        services.Count(d => d.ServiceType == typeof(IUnitOfWorkManager)).Should().Be(1);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IUnitOfWorkManager>().Should().BeOfType<UnitOfWorkManager>();
    }

    [Fact]
    public void should_resolve_the_same_manager_within_a_scope_and_different_ones_across_scopes()
    {
        var services = new ServiceCollection();

        services.AddUnitOfWork();

        using var provider = services.BuildServiceProvider();

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var manager1 = scope1.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        manager1.Should().BeSameAs(scope1.ServiceProvider.GetRequiredService<IUnitOfWorkManager>());
        manager1.Should().NotBeSameAs(scope2.ServiceProvider.GetRequiredService<IUnitOfWorkManager>());
    }

    [Fact]
    public void should_throw_when_resolving_the_scoped_manager_from_the_root_under_scope_validation()
    {
        var services = new ServiceCollection();

        services.AddUnitOfWork();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var act = () => provider.GetRequiredService<IUnitOfWorkManager>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot resolve scoped service*");
    }
}
