// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Permissions.GrantProviders;
using Headless.Permissions.Grants;
using Headless.Permissions.Models;
using Headless.Primitives;
using Headless.Testing.Tests;
using NSubstitute;

namespace Tests.Providers;

public sealed class UserPermissionGrantProviderTests : TestBase
{
    private readonly IPermissionGrantStore _grantStore;
    private readonly ICurrentTenant _currentTenant;
    private readonly UserPermissionGrantProvider _sut;

    public UserPermissionGrantProviderTests()
    {
        _grantStore = Substitute.For<IPermissionGrantStore>();
        _currentTenant = Substitute.For<ICurrentTenant>();
        _sut = new UserPermissionGrantProvider(_grantStore, _currentTenant);
    }

    [Fact]
    public async Task should_check_permission_by_user_id()
    {
        // given
        var userId = new UserId("test-user-123");
        var permission = CreatePermission("Users.Create");
        var currentUser = CreateCurrentUser(userId);

        _grantStore
            .IsGrantedAsync(
                Arg.Any<IReadOnlyList<string>>(),
                PermissionGrantProviderNames.User,
                userId.ToString(),
                AbortToken
            )
            .Returns(
                new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal)
                {
                    ["Users.Create"] = PermissionGrantStatus.Granted,
                }
            );

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Granted);
        result["Users.Create"].ProviderKeys.Should().Contain(userId.ToString());
    }

    [Fact]
    public async Task should_return_undefined_for_unauthenticated_user()
    {
        // given
        var permission = CreatePermission("Users.Create");
        var currentUser = CreateCurrentUser(userId: null);

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_return_grant_status_from_store()
    {
        // given
        var userId = new UserId("test-user-123");
        var permission1 = CreatePermission("Users.Create");
        var permission2 = CreatePermission("Users.Delete");
        var currentUser = CreateCurrentUser(userId);

        _grantStore
            .IsGrantedAsync(
                Arg.Any<IReadOnlyList<string>>(),
                PermissionGrantProviderNames.User,
                userId.ToString(),
                AbortToken
            )
            .Returns(
                new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal)
                {
                    ["Users.Create"] = PermissionGrantStatus.Granted,
                    ["Users.Delete"] = PermissionGrantStatus.Prohibited,
                }
            );

        // when
        var result = await _sut.CheckAsync([permission1, permission2], currentUser, AbortToken);

        // then
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Granted);
        result["Users.Delete"].Status.Should().Be(PermissionGrantStatus.Prohibited);
    }

    [Fact]
    public async Task should_return_undefined_when_permission_not_in_store()
    {
        // given
        var userId = new UserId("test-user-123");
        var permission = CreatePermission("Users.Create");
        var currentUser = CreateCurrentUser(userId);

        _grantStore
            .IsGrantedAsync(
                Arg.Any<IReadOnlyList<string>>(),
                PermissionGrantProviderNames.User,
                userId.ToString(),
                AbortToken
            )
            .Returns(new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal));

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Undefined);
    }

    private static PermissionDefinition CreatePermission(string name) => new(name);

    private static ICurrentUser CreateCurrentUser(UserId? userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.Roles.Returns(new HashSet<string>());
        return user;
    }
}
