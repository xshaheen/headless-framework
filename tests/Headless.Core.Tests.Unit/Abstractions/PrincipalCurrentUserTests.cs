// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class PrincipalCurrentUserTests
{
    [Fact]
    public void is_authenticated_should_be_true_for_an_authenticated_identity()
    {
        // given — a non-null authentication type makes ClaimsIdentity.IsAuthenticated true
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth"));
        var sut = new PrincipalCurrentUser(principal);

        // then
        sut.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void is_authenticated_should_be_false_for_an_unauthenticated_identity()
    {
        // given — no authentication type => ClaimsIdentity.IsAuthenticated is false
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var sut = new PrincipalCurrentUser(principal);

        // then
        sut.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void is_authenticated_should_be_false_for_a_null_principal()
    {
        // given
        var sut = new PrincipalCurrentUser(null);

        // then
        sut.IsAuthenticated.Should().BeFalse();
    }
}
