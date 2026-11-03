// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Context;
using Headless.Permissions;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Requirements;

public sealed class PermissionRequirementHandlerTests : TestBase
{
    private const string _Read = "Orders.Read";
    private const string _Delete = "Orders.Delete";
    private const string _Export = "Orders.Export";

    private readonly IPermissionManager _permissionManager = Substitute.For<IPermissionManager>();

    [Fact]
    public async Task should_read_every_permission_of_a_combined_policy_once()
    {
        // given - a group-level, an endpoint-level, and a multi-permission requirement in one policy
        _Grant(_Read, _Export);
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(_Read))
            .AddRequirements(new PermissionRequirement(_Read))
            .AddRequirements(new PermissionsRequirement([_Delete, _Export], requiresAll: false))
            .Build();

        // when
        var result = await _AuthorizeAsync(policy);

        // then
        result.Succeeded.Should().BeTrue();
        await _permissionManager
            .Received(1)
            .GetAllAsync(
                Arg.Is<IReadOnlyCollection<string>>(names =>
                    names.Count == 3 && names.Contains(_Read) && names.Contains(_Delete) && names.Contains(_Export)
                ),
                Arg.Any<ICurrentUser>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await _permissionManager.DidNotReceiveWithAnyArgs().GetAsync(default!, default!, default, AbortToken);
    }

    [Fact]
    public async Task should_leave_only_the_ungranted_requirements_pending()
    {
        // given
        _Grant(_Read, _Export);
        var single = new PermissionRequirement(_Delete);
        var all = new PermissionsRequirement([_Read, _Delete], requiresAll: true);
        var any = new PermissionsRequirement([_Delete, _Export], requiresAll: false);
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionRequirement(_Read), single, all, any)
            .Build();

        // when
        var result = await _AuthorizeAsync(policy);

        // then - a denied permission leaves its requirement pending without failing the evaluation
        result.Succeeded.Should().BeFalse();
        result.Failure!.FailCalled.Should().BeFalse();
        result.Failure.FailedRequirements.Should().BeEquivalentTo(new IAuthorizationRequirement[] { single, all });
    }

    [Fact]
    public async Task should_succeed_a_requires_all_requirement_when_every_permission_is_granted()
    {
        // given
        _Grant(_Read, _Delete);
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionsRequirement([_Read, _Delete], requiresAll: true))
            .Build();

        // when
        var result = await _AuthorizeAsync(policy);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_read_nothing_when_the_policy_has_no_permission_requirement()
    {
        // given
        var policy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();

        // when
        var result = await _AuthorizeAsync(policy);

        // then
        result.Succeeded.Should().BeTrue();
        await _permissionManager.DidNotReceiveWithAnyArgs().GetAllAsync(default!, default!, default, AbortToken);
    }

    private void _Grant(params string[] granted)
    {
        _permissionManager
            .GetAllAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<ICurrentUser>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                call.ArgAt<IReadOnlyCollection<string>>(0)
                    .Select(name => new GrantedPermissionResult(name, granted.Contains(name, StringComparer.Ordinal)))
                    .ToList()
            );
    }

    private async Task<AuthorizationResult> _AuthorizeAsync(AuthorizationPolicy policy)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore();
        services.AddSingleton(_permissionManager);
        services.AddSingleton<IAuthorizationHandler, PermissionRequirementHandler>();

        await using var provider = services.BuildServiceProvider();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "test"));

        return await provider
            .GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, resource: null, policy);
    }
}
