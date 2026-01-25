// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Framework.Permissions.GrantProviders;
using Framework.Permissions.Grants;
using Framework.Permissions.Models;
using Framework.Testing.Tests;
using NSubstitute;

namespace Tests.Providers;

public sealed class RolePermissionGrantProviderTests : TestBase
{
    private readonly IPermissionGrantStore _grantStore;
    private readonly ICurrentTenant _currentTenant;
    private readonly RolePermissionGrantProvider _sut;

    public RolePermissionGrantProviderTests()
    {
        _grantStore = Substitute.For<IPermissionGrantStore>();
        _currentTenant = Substitute.For<ICurrentTenant>();
        _sut = new RolePermissionGrantProvider(_grantStore, _currentTenant);
    }

    [Fact]
    public async Task should_return_undefined_when_user_has_no_roles()
    {
        // given
        var permission = CreatePermission("Users.Create");
        var currentUser = CreateCurrentUser();

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result.Should().ContainKey("Users.Create");
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Undefined);
    }

    [Fact]
    public async Task should_check_all_roles_for_permission()
    {
        // given
        var permission = CreatePermission("Users.Create");
        var currentUser = CreateCurrentUser(["Admin", "Manager", "User"]);

        _grantStore
            .IsGrantedAsync(Arg.Any<IReadOnlyList<string>>(), PermissionGrantProviderNames.Role, "Admin", AbortToken)
            .Returns(
                new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal)
                {
                    ["Users.Create"] = PermissionGrantStatus.Undefined,
                }
            );

        _grantStore
            .IsGrantedAsync(Arg.Any<IReadOnlyList<string>>(), PermissionGrantProviderNames.Role, "Manager", AbortToken)
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
        result["Users.Create"].ProviderKeys.Should().Contain("Manager");
    }

    [Fact]
    public async Task should_return_granted_from_first_matching_role()
    {
        // given
        var permission = CreatePermission("Users.Create");
        var currentUser = CreateCurrentUser(["Admin", "Manager"]);

        _grantStore
            .IsGrantedAsync(Arg.Any<IReadOnlyList<string>>(), PermissionGrantProviderNames.Role, "Admin", AbortToken)
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
        result["Users.Create"].ProviderKeys.Should().Contain("Admin");
    }

    [Fact]
    public async Task should_return_prohibited_when_permission_denied()
    {
        // given
        var permission = CreatePermission("Users.Delete");
        var currentUser = CreateCurrentUser(["User"]);

        _grantStore
            .IsGrantedAsync(Arg.Any<IReadOnlyList<string>>(), PermissionGrantProviderNames.Role, "User", AbortToken)
            .Returns(
                new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal)
                {
                    ["Users.Delete"] = PermissionGrantStatus.Prohibited,
                }
            );

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result["Users.Delete"].Status.Should().Be(PermissionGrantStatus.Prohibited);
        result["Users.Delete"].ProviderKeys.Should().Contain("User");
    }

    [Fact]
    public async Task should_stop_checking_roles_when_all_permissions_resolved()
    {
        // given
        var permission1 = CreatePermission("Users.Create");
        var permission2 = CreatePermission("Users.Read");
        var currentUser = CreateCurrentUser(["Admin", "Manager", "User"]);

        _grantStore
            .IsGrantedAsync(Arg.Any<IReadOnlyList<string>>(), PermissionGrantProviderNames.Role, "Admin", AbortToken)
            .Returns(
                new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal)
                {
                    ["Users.Create"] = PermissionGrantStatus.Granted,
                    ["Users.Read"] = PermissionGrantStatus.Granted,
                }
            );

        // when
        var result = await _sut.CheckAsync([permission1, permission2], currentUser, AbortToken);

        // then
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Granted);
        result["Users.Read"].Status.Should().Be(PermissionGrantStatus.Granted);

        // Should not check Manager or User roles since all permissions are resolved
        await _grantStore
            .DidNotReceive()
            .IsGrantedAsync(Arg.Any<IReadOnlyList<string>>(), PermissionGrantProviderNames.Role, "Manager", AbortToken);
    }

    [Fact]
    public async Task should_track_which_role_granted_each_permission()
    {
        // given
        var permission1 = CreatePermission("Users.Create");
        var permission2 = CreatePermission("Users.Delete");
        var currentUser = CreateCurrentUser(["Admin", "Manager"]);

        _grantStore
            .IsGrantedAsync(Arg.Any<IReadOnlyList<string>>(), PermissionGrantProviderNames.Role, "Admin", AbortToken)
            .Returns(
                new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal)
                {
                    ["Users.Create"] = PermissionGrantStatus.Granted,
                    ["Users.Delete"] = PermissionGrantStatus.Undefined,
                }
            );

        _grantStore
            .IsGrantedAsync(Arg.Any<IReadOnlyList<string>>(), PermissionGrantProviderNames.Role, "Manager", AbortToken)
            .Returns(
                new Dictionary<string, PermissionGrantStatus>(StringComparer.Ordinal)
                {
                    ["Users.Delete"] = PermissionGrantStatus.Granted,
                }
            );

        // when
        var result = await _sut.CheckAsync([permission1, permission2], currentUser, AbortToken);

        // then
        result["Users.Create"].ProviderKeys.Should().Contain("Admin");
        result["Users.Delete"].ProviderKeys.Should().Contain("Manager");
    }

    [Fact]
    public async Task should_throw_when_permissions_collection_is_empty()
    {
        // given
        var currentUser = CreateCurrentUser(["Admin"]);

        // when
        var act = () => _sut.CheckAsync([], currentUser, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static PermissionDefinition CreatePermission(string name)
    {
        return (PermissionDefinition)
            Activator.CreateInstance(
                typeof(PermissionDefinition),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                [name, null, true],
                null
            )!;
    }

    private static ICurrentUser CreateCurrentUser(HashSet<string>? roles = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Roles.Returns(roles ?? []);
        return user;
    }
}
