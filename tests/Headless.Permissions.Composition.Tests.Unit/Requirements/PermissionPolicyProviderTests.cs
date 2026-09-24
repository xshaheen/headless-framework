// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Headless.Permissions.Requirements;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Tests.Requirements;

public sealed class PermissionPolicyProviderTests : TestBase
{
    private readonly IPermissionDefinitionManager _definitionManager = Substitute.For<IPermissionDefinitionManager>();
    private readonly AuthorizationOptions _authorizationOptions = new();
    private readonly PermissionManagementOptions _managementOptions = new();

    private PermissionPolicyProvider _CreateSut()
    {
        return new PermissionPolicyProvider(
            Options.Create(_authorizationOptions),
            _definitionManager,
            Options.Create(_managementOptions)
        );
    }

    private void _Define(string name, bool isEnabled = true)
    {
        var permission = new PermissionGroupDefinition("TestGroup").AddChild(name);
        permission.IsEnabled = isEnabled;
        _definitionManager.FindAsync(name, Arg.Any<CancellationToken>()).Returns(permission);
    }

    [Fact]
    public async Task should_return_permission_policy_when_name_is_defined_permission()
    {
        // given
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync("Orders.Edit");

        // then
        policy.Should().NotBeNull();
        policy!
            .Requirements.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<PermissionRequirement>()
            .Which.PermissionName.Should()
            .Be("Orders.Edit");
    }

    [Fact]
    public async Task should_return_host_policy_without_lookup_when_name_is_registered_policy()
    {
        // given
        _authorizationOptions.AddPolicy("Orders.Edit", p => p.RequireRole("admin"));
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync("Orders.Edit");

        // then
        policy!.Requirements.Should().NotContain(r => r is PermissionRequirement);
        await _definitionManager.DidNotReceive().FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_return_host_policy_when_registered_policy_differs_only_by_case()
    {
        // given
        _authorizationOptions.AddPolicy("orders.edit", p => p.RequireRole("admin"));
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync("Orders.Edit");

        // then
        policy!.Requirements.Should().NotContain(r => r is PermissionRequirement);
    }

    [Fact]
    public async Task should_return_null_when_name_differs_from_permission_only_by_case()
    {
        // given
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync("orders.edit");

        // then
        policy.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_when_name_is_not_a_defined_permission()
    {
        // given
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync("Orders.Nope");

        // then
        policy.Should().BeNull();
    }

    [Fact]
    public async Task should_return_policy_when_permission_is_disabled()
    {
        // given
        _Define("Orders.Edit", isEnabled: false);
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync("Orders.Edit");

        // then
        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task should_look_up_resolved_name_once_when_requested_repeatedly()
    {
        // given
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var first = await sut.GetPolicyAsync("Orders.Edit");
        var second = await sut.GetPolicyAsync("Orders.Edit");

        // then
        second.Should().BeSameAs(first);
        await _definitionManager.Received(1).FindAsync("Orders.Edit", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_resolve_name_when_permission_is_defined_after_a_miss()
    {
        // given
        var sut = _CreateSut();
        (await sut.GetPolicyAsync("Orders.Edit")).Should().BeNull();
        _Define("Orders.Edit");

        // when
        var policy = await sut.GetPolicyAsync("Orders.Edit");

        // then
        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task should_propagate_lookup_failure_and_retry_on_next_call()
    {
        // given
        var failure = new InvalidOperationException("store unavailable");
        _definitionManager.FindAsync("Orders.Edit", Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        var sut = _CreateSut();

        // when
        var act = () => sut.GetPolicyAsync("Orders.Edit");

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Should()
            .BeSameAs(failure);
        _Define("Orders.Edit");
        (await sut.GetPolicyAsync("Orders.Edit")).Should().NotBeNull();
        await _definitionManager.Received(2).FindAsync("Orders.Edit", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_resolve_prefixed_name_when_prefix_is_configured()
    {
        // given
        _managementOptions.PolicyNamePrefix = "permission:";
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync("permission:Orders.Edit");

        // then
        policy!
            .Requirements.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<PermissionRequirement>()
            .Which.PermissionName.Should()
            .Be("Orders.Edit");
    }

    [Theory]
    [InlineData("Orders.Edit")]
    [InlineData("Permission:Orders.Edit")]
    [InlineData("permission:")]
    public async Task should_return_null_when_name_lacks_configured_prefix_or_is_only_prefix(string policyName)
    {
        // given
        _managementOptions.PolicyNamePrefix = "permission:";
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync(policyName);

        // then
        policy.Should().BeNull();
    }

    [Fact]
    public async Task should_return_host_policy_when_it_carries_the_prefix()
    {
        // given
        _managementOptions.PolicyNamePrefix = "permission:";
        _authorizationOptions.AddPolicy("permission:Orders.Edit", p => p.RequireRole("admin"));
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var policy = await sut.GetPolicyAsync("permission:Orders.Edit");

        // then
        policy!.Requirements.Should().NotContain(r => r is PermissionRequirement);
    }

    [Fact]
    public async Task should_delegate_default_and_fallback_policies_to_authorization_options()
    {
        // given
        var fallback = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        _authorizationOptions.FallbackPolicy = fallback;
        var sut = _CreateSut();

        // when
        var defaultPolicy = await sut.GetDefaultPolicyAsync();
        var fallbackPolicy = await sut.GetFallbackPolicyAsync();

        // then
        defaultPolicy.Should().BeSameAs(_authorizationOptions.DefaultPolicy);
        fallbackPolicy.Should().BeSameAs(fallback);
        sut.AllowsCachingPolicies.Should().BeTrue();
    }

    [Fact]
    public async Task should_return_equivalent_policy_when_requested_concurrently()
    {
        // given
        _Define("Orders.Edit");
        var sut = _CreateSut();

        // when
        var policies = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => sut.GetPolicyAsync("Orders.Edit")));

        // then
        policies
            .Should()
            .AllSatisfy(p =>
                p!
                    .Requirements.Should()
                    .ContainSingle()
                    .Which.Should()
                    .BeOfType<PermissionRequirement>()
                    .Which.PermissionName.Should()
                    .Be("Orders.Edit")
            );
    }
}
