// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.Abstractions;
using Framework.Permissions.Testing;
using Framework.Testing.Tests;
using Microsoft.AspNetCore.Authorization;

namespace Tests.Testing;

public sealed class AlwaysAllowAuthorizationServiceTests : TestBase
{
    private readonly ICurrentPrincipalAccessor _principalAccessor = Substitute.For<ICurrentPrincipalAccessor>();
    private readonly AlwaysAllowAuthorizationService _sut;

    public AlwaysAllowAuthorizationServiceTests()
    {
        _sut = new AlwaysAllowAuthorizationService(_principalAccessor);
    }

    [Fact]
    public async Task should_always_authorize()
    {
        // given
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "TestUser")]));
        var resource = new object();
        const string policyName = "AdminPolicy";

        // when
        var result = await _sut.AuthorizeAsync(user, resource, policyName);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_authorize_for_any_requirement()
    {
        // given
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "TestUser")]));
        var resource = new object();
        var requirements = new IAuthorizationRequirement[] { new TestRequirement(), new AnotherTestRequirement() };

        // when
        var result = await _sut.AuthorizeAsync(user, resource, requirements);

        // then
        result.Succeeded.Should().BeTrue();
    }

    private sealed class TestRequirement : IAuthorizationRequirement;

    private sealed class AnotherTestRequirement : IAuthorizationRequirement;
}
