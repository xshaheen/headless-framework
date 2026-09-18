// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupEntityFrameworkUnitOfWorkTests
{
    [Fact]
    public void should_register_the_scoped_manager_and_be_idempotent()
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();

        services.Count(d => d.ServiceType == typeof(IUnitOfWorkManager)).Should().Be(1);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IUnitOfWorkManager>().Should().NotBeNull();
    }

    [Fact]
    public void should_throw_when_resolving_the_manager_from_the_root_under_scope_validation()
    {
        var services = new ServiceCollection();

        services.AddEntityFrameworkUnitOfWork();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var act = () => provider.GetRequiredService<IUnitOfWorkManager>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot resolve scoped service*");
    }
}
