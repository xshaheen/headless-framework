// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Headless.Permissions.Requirements;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Tests.Requirements;

public sealed class AuthorizationPolicyCatalogTests : TestBase
{
    private readonly AuthorizationOptions _authorizationOptions = new();
    private readonly PermissionManagementOptions _managementOptions = new();
    private readonly IPermissionDefinitionManager _definitionManager = Substitute.For<IPermissionDefinitionManager>();

    [Fact]
    public async Task should_list_policies_registered_on_authorization_options()
    {
        // given — this pins the private PolicyMap access; an ASP.NET Core change that breaks it fails here.
        _authorizationOptions.AddPolicy("Administrator", policy => policy.RequireAuthenticatedUser());
        _authorizationOptions.AddPolicy("Charity", policy => policy.RequireAuthenticatedUser());
        var sut = _CreateSut(_PermissionProvider());

        // when
        var names = await sut.GetRegisteredPolicyNamesAsync(AbortToken);

        // then
        names.Should().BeEquivalentTo("Administrator", "Charity");
    }

    [Fact]
    public async Task should_list_defined_permission_names()
    {
        // given
        _definitionManager.GetPermissionsAsync(AbortToken).Returns(_Permissions("Orders.Edit", "Orders.View"));
        var sut = _CreateSut(_PermissionProvider());

        // when
        var names = await sut.GetPermissionNamesAsync(AbortToken);

        // then
        names.Should().BeEquivalentTo("Orders.Edit", "Orders.View");
    }

    [Fact]
    public async Task should_list_permissions_as_policies_when_permission_provider_is_active()
    {
        // given
        _authorizationOptions.AddPolicy("Administrator", policy => policy.RequireAuthenticatedUser());
        _definitionManager.GetPermissionsAsync(AbortToken).Returns(_Permissions("Orders.Edit"));
        var sut = _CreateSut(_PermissionProvider());

        // when
        var names = await sut.GetPolicyNamesAsync(AbortToken);

        // then
        names.Should().BeEquivalentTo("Administrator", "Orders.Edit");
    }

    [Fact]
    public async Task should_list_permission_policies_with_the_configured_prefix()
    {
        // given
        _managementOptions.PolicyNamePrefix = "permission:";
        _definitionManager.GetPermissionsAsync(AbortToken).Returns(_Permissions("Orders.Edit"));
        var sut = _CreateSut(_PermissionProvider());

        // when
        var names = await sut.GetPolicyNamesAsync(AbortToken);

        // then
        names.Should().BeEquivalentTo("permission:Orders.Edit");
    }

    [Fact]
    public async Task should_list_only_registered_policies_when_another_provider_is_active()
    {
        // given — with DisablePermissionNamePolicies() or a host-owned provider, permission names are not policies.
        _authorizationOptions.AddPolicy("Administrator", policy => policy.RequireAuthenticatedUser());
        _definitionManager.GetPermissionsAsync(AbortToken).Returns(_Permissions("Orders.Edit"));
        var sut = _CreateSut(new DefaultAuthorizationPolicyProvider(Options.Create(_authorizationOptions)));

        // when
        var names = await sut.GetPolicyNamesAsync(AbortToken);

        // then
        names.Should().BeEquivalentTo("Administrator");
    }

    private AuthorizationPolicyCatalog _CreateSut(IAuthorizationPolicyProvider policyProvider)
    {
        return new AuthorizationPolicyCatalog(
            Options.Create(_authorizationOptions),
            Options.Create(_managementOptions),
            _definitionManager,
            policyProvider
        );
    }

    private PermissionPolicyProvider _PermissionProvider()
    {
        return new PermissionPolicyProvider(
            Options.Create(_authorizationOptions),
            _definitionManager,
            Options.Create(_managementOptions)
        );
    }

    private static IReadOnlyList<PermissionDefinition> _Permissions(params string[] names)
    {
        var group = new PermissionGroupDefinition("TestGroup");

        return [.. names.Select(name => group.AddChild(name))];
    }
}
