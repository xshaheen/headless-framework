// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Constants;
using Headless.Permissions;
using Headless.Permissions.Definitions;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tests.TestSetup;

namespace Tests;

public sealed class PermissionPolicyProviderTests(PermissionsTestFixture fixture) : PermissionsTestBase(fixture)
{
    private const string _PermissionName = "Orders.Edit";

    [Fact]
    public async Task should_authorize_user_by_permission_name_when_user_is_granted()
    {
        // given
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        await permissionManager.GrantToUserAsync(_PermissionName, "1001", AbortToken);

        // when
        var granted = await authorization.AuthorizeAsync(_User("1001"), _PermissionName);
        var notGranted = await authorization.AuthorizeAsync(_User("1002"), _PermissionName);

        // then
        granted.Succeeded.Should().BeTrue();
        notGranted.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_authorize_user_by_permission_name_when_role_is_granted()
    {
        // given
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        await permissionManager.GrantToRoleAsync(_PermissionName, "editor", AbortToken);

        // when
        var result = await authorization.AuthorizeAsync(_User("1003", role: "editor"), _PermissionName);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_deny_granted_user_when_permission_is_disabled()
    {
        // given — the grant store refuses disabled permissions, so grant first and disable the definition after.
        await Fixture.ResetAsync();
        using var host = _CreateHost();
        await using var scope = host.Services.CreateAsyncScope();
        var permissionManager = scope.ServiceProvider.GetRequiredService<IPermissionManager>();
        var definitionManager = scope.ServiceProvider.GetRequiredService<IPermissionDefinitionManager>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        await permissionManager.GrantToUserAsync(_PermissionName, "1004", AbortToken);
        var definition = await definitionManager.FindAsync(_PermissionName, AbortToken);
        definition!.IsEnabled = false;

        // when
        var result = await authorization.AuthorizeAsync(_User("1004"), _PermissionName);

        // then
        result.Succeeded.Should().BeFalse();
    }

    private IHost _CreateHost()
    {
        return CreateHost(builder =>
        {
            builder.Services.AddPermissionDefinitionProvider<OrdersPermissionDefinitionProvider>();
            builder.Services.AddAuthorizationCore();
        });
    }

    private static ClaimsPrincipal _User(string userId, string? role = null)
    {
        List<Claim> claims = [new(UserClaimTypes.UserId, userId)];

        if (role is not null)
        {
            claims.Add(new Claim(UserClaimTypes.Roles, role));
        }

        // An authenticated identity is required: the current-user adapter reports no id or roles without one.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    private sealed class OrdersPermissionDefinitionProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("Orders").AddChild(_PermissionName);
        }
    }
}
