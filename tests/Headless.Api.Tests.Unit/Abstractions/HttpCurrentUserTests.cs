// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Constants;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests.Abstractions;

public sealed class HttpCurrentUserTests : TestBase
{
    [Fact]
    public void should_return_principal_from_accessor()
    {
        // given
        var principal = new ClaimsPrincipal(new ClaimsIdentity());
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.Principal;

        // then
        result.Should().BeSameAs(principal);
    }

    [Fact]
    public void should_return_is_authenticated_true_when_user_id_present()
    {
        // given
        var claims = new[] { new Claim(UserClaimTypes.UserId, "user-123") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.IsAuthenticated;

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_is_authenticated_false_when_no_user_id()
    {
        // given
        var identity = new ClaimsIdentity([], "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.IsAuthenticated;

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_when_principal_null()
    {
        // given
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns((ClaimsPrincipal?)null);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.IsAuthenticated;

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_user_id_from_claims()
    {
        // given
        var claims = new[] { new Claim(UserClaimTypes.UserId, "user-456") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.UserId;

        // then
        result.Should().NotBeNull();
        result.Should().Be(new UserId("user-456"));
    }

    [Fact]
    public void should_return_null_user_id_when_missing()
    {
        // given
        var identity = new ClaimsIdentity([], "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.UserId;

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_account_type_from_claims()
    {
        // given
        var claims = new[] { new Claim(UserClaimTypes.AccountType, "premium") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.AccountType;

        // then
        result.Should().Be("premium");
    }

    [Fact]
    public void should_return_account_id_from_claims()
    {
        // given
        var claims = new[] { new Claim(UserClaimTypes.AccountId, "acc-789") };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.AccountId;

        // then
        result.Should().NotBeNull();
        result.Should().Be(new AccountId("acc-789"));
    }

    [Fact]
    public void should_return_roles_from_claims()
    {
        // given
        var claims = new[]
        {
            new Claim(UserClaimTypes.Roles, "admin"),
            new Claim(UserClaimTypes.Roles, "editor"),
            new Claim(UserClaimTypes.Roles, "viewer"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.Roles;

        // then
        result.Should().BeEquivalentTo(["admin", "editor", "viewer"]);
    }

    [Fact]
    public void should_return_empty_roles_when_none()
    {
        // given
        var identity = new ClaimsIdentity([], "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns(principal);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.Roles;

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public void should_return_empty_roles_when_principal_null()
    {
        // given
        var accessor = Substitute.For<ICurrentPrincipalAccessor>();
        accessor.Principal.Returns((ClaimsPrincipal?)null);
        var sut = new HttpCurrentUser(accessor);

        // when
        var result = sut.Roles;

        // then
        result.Should().BeEmpty();
    }
}
