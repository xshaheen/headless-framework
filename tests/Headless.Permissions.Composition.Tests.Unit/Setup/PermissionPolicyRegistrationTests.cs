// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.MultiTenancy;
using Headless.Permissions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Headless.Permissions.Requirements;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Setup;

public sealed class PermissionPolicyRegistrationTests : TestBase
{
    [Fact]
    public void should_replace_default_provider_when_authorization_is_registered_first()
    {
        // given
        var services = new ServiceCollection();
        services.AddAuthorizationCore();

        // when
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<RegistrationTestDbContext>());

        // then
        services
            .Where(d => d.ServiceType == typeof(IAuthorizationPolicyProvider))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<ServiceDescriptor>(d =>
                d.ImplementationType == typeof(PermissionPolicyProvider) && d.Lifetime == ServiceLifetime.Singleton
            );
    }

    [Fact]
    public void should_keep_permission_provider_when_authorization_is_registered_after()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<RegistrationTestDbContext>());
        services.AddAuthorizationCore();

        // then
        services
            .Where(d => d.ServiceType == typeof(IAuthorizationPolicyProvider))
            .Should()
            .ContainSingle()
            .Which.ImplementationType.Should()
            .Be<PermissionPolicyProvider>();
    }

    [Fact]
    public void should_keep_host_provider_when_registered_before_permissions()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationPolicyProvider, HostPolicyProvider>();
        services.AddAuthorizationCore();

        // when
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<RegistrationTestDbContext>());

        // then
        services
            .Where(d => d.ServiceType == typeof(IAuthorizationPolicyProvider))
            .Should()
            .ContainSingle()
            .Which.ImplementationType.Should()
            .Be<HostPolicyProvider>();
    }

    [Fact]
    public void should_not_register_permission_provider_when_disabled()
    {
        // given
        var services = new ServiceCollection();
        services.AddAuthorizationCore();

        // when
        services.AddHeadlessPermissions(setup =>
        {
            setup.DisablePermissionNamePolicies();
            setup.UseEntityFramework<RegistrationTestDbContext>();
        });

        // then
        services
            .Where(d => d.ServiceType == typeof(IAuthorizationPolicyProvider))
            .Should()
            .ContainSingle()
            .Which.ImplementationType.Should()
            .Be<DefaultAuthorizationPolicyProvider>();
    }

    [Fact]
    public void should_keep_one_provider_when_permissions_are_added_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<RegistrationTestDbContext>());

        // when
        var act = () => services.AddHeadlessPermissions(setup => setup.UseEntityFramework<RegistrationTestDbContext>());

        // then
        act.Should().Throw<InvalidOperationException>();
        services.Where(d => d.ServiceType == typeof(IAuthorizationPolicyProvider)).Should().ContainSingle();
    }

    [Fact]
    public void should_resolve_policy_provider_without_authorization_registration()
    {
        // given
        var services = _CreateResolvableServices(Substitute.For<IPermissionManager>());

        // when
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        provider.GetRequiredService<IAuthorizationPolicyProvider>().Should().BeOfType<PermissionPolicyProvider>();
    }

    [Fact]
    public void should_resolve_client_config_services_without_authorization_registration()
    {
        // given — a worker-style host: no AddAuthorization, no ICurrentPrincipalAccessor, no ICurrentTenant.
        // AddHeadlessPermissions supplies authorization and a tenant fallback; the principal accessor is optional.
        var services = _CreateResolvableServices(Substitute.For<IPermissionManager>());

        // when
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        provider.GetRequiredService<IGrantedPoliciesReader>().Should().NotBeNull();
        provider.GetRequiredService<IAuthorizationPolicyCatalog>().Should().NotBeNull();
        provider.GetRequiredService<ICurrentTenant>().Should().BeOfType<CurrentTenant>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_authorize_by_permission_name_according_to_grant(bool isGranted)
    {
        // given
        var permissionManager = Substitute.For<IPermissionManager>();
        permissionManager
            .GetAsync("Orders.Edit", Arg.Any<ICurrentUser>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new GrantedPermissionResult("Orders.Edit", isGranted));
        var services = _CreateResolvableServices(permissionManager);
        services.AddAuthorizationCore();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        // when
        var result = await authorization.AuthorizeAsync(_User(), "Orders.Edit");

        // then
        result.Succeeded.Should().Be(isGranted);
    }

    [Fact]
    public async Task should_throw_policy_not_found_when_name_is_not_a_defined_permission()
    {
        // given
        var services = _CreateResolvableServices(Substitute.For<IPermissionManager>());
        services.AddAuthorizationCore();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        // when
        var act = () => authorization.AuthorizeAsync(_User(), "Orders.Nope");

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static ServiceCollection _CreateResolvableServices(IPermissionManager permissionManager)
    {
        // The real definition manager pulls in the dynamic store's repository, cache, lock, and bus; a substitute
        // registered first wins because Core registers the manager with TryAddSingleton.
        var definitionManager = Substitute.For<IPermissionDefinitionManager>();
        definitionManager
            .FindAsync("Orders.Edit", Arg.Any<CancellationToken>())
            .Returns(new PermissionGroupDefinition("Orders").AddChild("Orders.Edit"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(definitionManager);
        services.AddSingleton(permissionManager);
        services.AddHeadlessPermissions(setup => setup.UseEntityFramework<RegistrationTestDbContext>());

        return services;
    }

    private static ClaimsPrincipal _User()
    {
        return new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test"));
    }

    private sealed class HostPolicyProvider : IAuthorizationPolicyProvider
    {
        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
        {
            return Task.FromResult(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        }

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
        {
            return Task.FromResult<AuthorizationPolicy?>(null);
        }
    }

    private sealed class RegistrationTestDbContext(DbContextOptions<RegistrationTestDbContext> options)
        : DbContext(options);
}
