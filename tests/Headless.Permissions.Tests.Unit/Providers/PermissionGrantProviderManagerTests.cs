// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.GrantProviders;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Tests.Providers;

public sealed class PermissionGrantProviderManagerTests : TestBase
{
    [Fact]
    public void should_register_and_resolve_all_providers()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IPermissionGrantStore>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton<RolePermissionGrantProvider>();
        services.AddSingleton<UserPermissionGrantProvider>();

        var options = Options.Create(new PermissionManagementProvidersOptions());
        options.Value.GrantProviders.Add<RolePermissionGrantProvider>();
        options.Value.GrantProviders.Add<UserPermissionGrantProvider>();

        var serviceProvider = services.BuildServiceProvider();
        var manager = new PermissionGrantProviderManager(serviceProvider, options);

        // when
        var providers = manager.ValueProviders;

        // then
        providers.Should().HaveCount(2);
        providers.Should().Contain(p => p.Name == PermissionGrantProviderNames.Role);
        providers.Should().Contain(p => p.Name == PermissionGrantProviderNames.User);
    }

    [Fact]
    public void should_return_providers_in_registration_order()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IPermissionGrantStore>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton<UserPermissionGrantProvider>();
        services.AddSingleton<RolePermissionGrantProvider>();

        var options = Options.Create(new PermissionManagementProvidersOptions());
        // Register User first, then Role
        options.Value.GrantProviders.Add<UserPermissionGrantProvider>();
        options.Value.GrantProviders.Add<RolePermissionGrantProvider>();

        var serviceProvider = services.BuildServiceProvider();
        var manager = new PermissionGrantProviderManager(serviceProvider, options);

        // when
        var providers = manager.ValueProviders;

        // then
        providers[0].Name.Should().Be(PermissionGrantProviderNames.User);
        providers[1].Name.Should().Be(PermissionGrantProviderNames.Role);
    }

    [Fact]
    public void should_throw_when_duplicate_provider_names_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IPermissionGrantStore>());
        services.AddSingleton(Substitute.For<ICurrentTenant>());
        services.AddSingleton<RolePermissionGrantProvider>();
        services.AddSingleton<DuplicateRoleProvider>();

        var options = Options.Create(new PermissionManagementProvidersOptions());
        options.Value.GrantProviders.Add<RolePermissionGrantProvider>();
        options.Value.GrantProviders.Add<DuplicateRoleProvider>();

        var serviceProvider = services.BuildServiceProvider();
        var manager = new PermissionGrantProviderManager(serviceProvider, options);

        // when
        var act = () => _ = manager.ValueProviders;

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Duplicate permission value provider name detected*");
    }

    /// <summary>A provider with duplicate name for testing.</summary>
    private sealed class DuplicateRoleProvider(IPermissionGrantStore grantStore, ICurrentTenant currentTenant)
        : StorePermissionGrantProvider(grantStore, currentTenant)
    {
        public override string Name => PermissionGrantProviderNames.Role;

        public override Task<MultiplePermissionGrantStatusResult> CheckAsync(
            IReadOnlyCollection<PermissionDefinition> permissions,
            ICurrentUser currentUser,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(new MultiplePermissionGrantStatusResult());
        }
    }
}
