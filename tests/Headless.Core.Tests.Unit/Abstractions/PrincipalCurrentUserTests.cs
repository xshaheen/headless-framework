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

    [Fact]
    public void find_claims_should_return_all_matching_claims()
    {
        // given
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("role", "admin"), new Claim("role", "editor"), new Claim("other", "x")],
                "TestAuth"
            )
        );
        ICurrentUser sut = new PrincipalCurrentUser(principal);

        // when
        var claims = sut.FindClaims("role");

        // then
        claims.Should().HaveCount(2);
        claims.Select(c => c.Value).Should().BeEquivalentTo(["admin", "editor"]);
    }

    [Fact]
    public void find_claims_should_return_empty_for_a_null_principal()
    {
        // given
        ICurrentUser sut = new PrincipalCurrentUser(null);

        // then
        sut.FindClaims("role").Should().BeEmpty();
    }
}
