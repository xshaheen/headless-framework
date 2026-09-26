// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using NSubstitute.ExceptionExtensions;

namespace Tests.Grants;

public sealed class GrantedPoliciesReaderTests : TestBase
{
    private readonly IPermissionManager _permissionManager = Substitute.For<IPermissionManager>();
    private readonly IAuthorizationService _authorizationService = Substitute.For<IAuthorizationService>();
    private readonly ThreadCurrentPrincipalAccessor _principalAccessor = new();
    private readonly TestCurrentTenant _currentTenant = new();
    private readonly GrantedPoliciesReader _sut;

    public GrantedPoliciesReaderTests()
    {
        _sut = new GrantedPoliciesReader(_permissionManager, _authorizationService, _currentTenant, _principalAccessor);
        _permissionManager
            .GetAllAsync(Arg.Any<ICurrentUser>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    [Fact]
    public async Task should_return_only_granted_permissions_and_satisfied_policies()
    {
        // given
        var context = _Context("tenant-1");
        _authorizationService
            .AuthorizeAsync(context.Principal, null, "Administrator")
            .Returns(AuthorizationResult.Success());
        _authorizationService.AuthorizeAsync(context.Principal, null, "Charity").Returns(AuthorizationResult.Failed());
        _permissionManager
            .GetAllAsync(Arg.Any<ICurrentUser>(), Arg.Any<string?>(), AbortToken)
            .Returns([
                new GrantedPermissionResult("Orders.Edit", true),
                new GrantedPermissionResult("Orders.Delete", false),
            ]);

        // when
        var granted = await _sut.GetAsync(context, ["Administrator", "Charity"], AbortToken);

        // then
        granted.Should().BeEquivalentTo("Administrator", "Orders.Edit");
    }

    [Fact]
    public async Task should_check_permissions_for_the_context_principal_in_one_call()
    {
        // given
        var context = _Context("tenant-1");

        // when
        await _sut.GetAsync(context, [], AbortToken);

        // then
        await _permissionManager
            .Received(1)
            .GetAllAsync(
                Arg.Is<ICurrentUser>(user => user.Principal == context.Principal),
                Arg.Any<string?>(),
                AbortToken
            );
    }

    [Fact]
    public async Task should_evaluate_a_repeated_policy_name_once_even_when_it_fails()
    {
        // given
        var context = _Context("tenant-1");
        _authorizationService.AuthorizeAsync(context.Principal, null, "Charity").Returns(AuthorizationResult.Failed());

        // when
        await _sut.GetAsync(context, ["Charity", "Charity"], AbortToken);

        // then
        await _authorizationService.Received(1).AuthorizeAsync(context.Principal, null, "Charity");
    }

    [Fact]
    public async Task should_propagate_when_a_listed_policy_is_unknown()
    {
        // given
        var context = _Context("tenant-1");
        _authorizationService
            .AuthorizeAsync(context.Principal, null, "Typo")
            .ThrowsAsync(new InvalidOperationException("No policy found: Typo."));

        // when
        var act = () => _sut.GetAsync(context, ["Typo"], AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task should_evaluate_under_context_identity_and_restore_ambient_identity_afterwards()
    {
        // given
        var ambient = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "ambient")], "test"));
        var issued = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "issued")], "test"));
        using var ambientPrincipal = _principalAccessor.Change(ambient);
        using var ambientTenant = _currentTenant.Change("ambient-tenant");

        ClaimsPrincipal? principalDuringCheck = null;
        string? tenantDuringCheck = null;
        _authorizationService
            .AuthorizeAsync(issued, null, "Administrator")
            .Returns(_ =>
            {
                principalDuringCheck = _principalAccessor.Principal;
                tenantDuringCheck = _currentTenant.Id;
                return AuthorizationResult.Success();
            });

        // when
        await _sut.GetAsync(new PrincipalContext(issued, "issued-tenant"), ["Administrator"], AbortToken);

        // then
        principalDuringCheck.Should().BeSameAs(issued);
        tenantDuringCheck.Should().Be("issued-tenant");
        _principalAccessor.Principal.Should().BeSameAs(ambient);
        _currentTenant.Id.Should().Be("ambient-tenant");
    }

    private static PrincipalContext _Context(string? tenantId)
    {
        return new PrincipalContext(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "user-1")], "test")),
            tenantId
        );
    }
}
