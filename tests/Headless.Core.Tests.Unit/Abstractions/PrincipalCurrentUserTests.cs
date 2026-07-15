// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Constants;
using UserId = Headless.Primitives.UserId;

namespace Tests.Abstractions;

public sealed class PrincipalCurrentUserTests
{
    [Fact]
    public void should_be_true_for_an_authenticated_identity_when_is_authenticated()
    {
        // given — a non-null authentication type makes ClaimsIdentity.IsAuthenticated true
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth"));
        var sut = new PrincipalCurrentUser(principal);

        // then
        sut.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void should_be_false_for_an_unauthenticated_identity_when_is_authenticated()
    {
        // given — no authentication type => ClaimsIdentity.IsAuthenticated is false
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var sut = new PrincipalCurrentUser(principal);

        // then
        sut.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void should_be_false_for_a_null_principal_when_is_authenticated()
    {
        // given
        var sut = new PrincipalCurrentUser(null);

        // then
        sut.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void should_not_expose_user_id_or_roles_when_unauthenticated_principal()
    {
        // given
        UserId userId = "user-123";

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(UserClaimTypes.UserId, userId), new Claim(UserClaimTypes.Roles, "admin")])
        );

        var sut = new PrincipalCurrentUser(principal);

        // then
        sut.IsAuthenticated.Should().BeFalse();
        sut.UserId.Should().BeNull();
        sut.Roles.Should().BeEmpty();
    }

    [Fact]
    public void should_return_all_matching_claims_when_find_claims()
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
    public void should_return_empty_for_a_null_principal_when_find_claims()
    {
        // given
        ICurrentUser sut = new PrincipalCurrentUser(null);

        // then
        sut.FindClaims("role").Should().BeEmpty();
    }
}
