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
    private readonly IPermissionDefinitionManager _definitionManager = Substitute.For<IPermissionDefinitionManager>();
    private readonly AuthorizationPolicyCatalog _sut;

    public AuthorizationPolicyCatalogTests()
    {
        _sut = new AuthorizationPolicyCatalog(Options.Create(_authorizationOptions), _definitionManager);
    }

    [Fact]
    public async Task should_list_policies_registered_on_authorization_options()
    {
        // given — this pins the private PolicyMap access; an ASP.NET Core change that breaks it fails here.
        _authorizationOptions.AddPolicy("Administrator", policy => policy.RequireAuthenticatedUser());
        _authorizationOptions.AddPolicy("Charity", policy => policy.RequireAuthenticatedUser());

        // when
        var names = await _sut.GetRegisteredPolicyNamesAsync(AbortToken);

        // then
        names.Should().BeEquivalentTo("Administrator", "Charity");
    }

    [Fact]
    public async Task should_list_defined_permission_names()
    {
        // given
        _definitionManager.GetPermissionsAsync(AbortToken).Returns(_Permissions("Orders.Edit", "Orders.View"));

        // when
        var names = await _sut.GetPermissionNamesAsync(AbortToken);

        // then
        names.Should().BeEquivalentTo("Orders.Edit", "Orders.View");
    }

    [Fact]
    public async Task should_list_the_union_of_registered_policies_and_permissions()
    {
        // given
        _authorizationOptions.AddPolicy("Administrator", policy => policy.RequireAuthenticatedUser());
        _definitionManager.GetPermissionsAsync(AbortToken).Returns(_Permissions("Orders.Edit"));

        // when
        var names = await _sut.GetPolicyNamesAsync(AbortToken);

        // then
        names.Should().BeEquivalentTo("Administrator", "Orders.Edit");
    }

    private static IReadOnlyList<PermissionDefinition> _Permissions(params string[] names)
    {
        var group = new PermissionGroupDefinition("TestGroup");

        return [.. names.Select(name => group.AddChild(name))];
    }
}
