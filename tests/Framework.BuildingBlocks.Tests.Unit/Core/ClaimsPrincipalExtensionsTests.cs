// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.Constants;
using Framework.Primitives;
using Framework.Testing.Tests;

namespace Tests.Core;

public sealed class ClaimsPrincipalExtensionsTests : TestBase
{
    [Fact]
    public void should_extract_claim_value()
    {
        // given
        UserId userId = "user-123";
        var claims = new[] { new Claim(UserClaimTypes.UserId, (string)userId) };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // when
        var result = principal.GetUserId();

        // then
        result.Should().Be(userId);
    }

    [Fact]
    public void should_return_null_when_claim_not_found()
    {
        // given
        var claims = new[] { new Claim("other_claim", "value") };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // when
        var result = principal.GetUserId();

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_null_for_null_principal()
    {
        // given
        ClaimsPrincipal? principal = null;

        // when
        var result = principal.GetUserId();

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_extract_first_claim_when_multiple_exist()
    {
        // given
        UserId firstUserId = "user-first";
        UserId secondUserId = "user-second";
        var claims = new[]
        {
            new Claim(UserClaimTypes.UserId, (string)firstUserId),
            new Claim(UserClaimTypes.UserId, (string)secondUserId),
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        // when
        var result = principal.GetUserId();

        // then
        result.Should().Be(firstUserId);
    }
}
