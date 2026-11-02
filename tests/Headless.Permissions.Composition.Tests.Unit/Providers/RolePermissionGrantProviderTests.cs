// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Context;
using Headless.Permissions;
using Headless.Testing.Tests;

namespace Tests.Providers;

public sealed class RolePermissionGrantProviderTests : TestBase
{
    private readonly IPermissionGrantStore _grantStore;
    private readonly RolePermissionGrantProvider _sut;

    public RolePermissionGrantProviderTests()
    {
        _grantStore = Substitute.For<IPermissionGrantStore>();
        _sut = new RolePermissionGrantProvider(_grantStore);
    }

    [Fact]
    public async Task should_return_undefined_when_user_has_no_roles()
    {
        // given
        var permission = _CreatePermission("Users.Create");
        var currentUser = _CreateCurrentUser();

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result.Statuses.Should().ContainKey("Users.Create");
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Undefined);
        await _grantStore
            .DidNotReceive()
            .IsGrantedAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_read_every_roles_grants_in_one_store_call()
    {
        // given
        var permission = _CreatePermission("Users.Create");
        var currentUser = _CreateCurrentUser(["Admin", "Manager", "User"]);

        _grantStore
            .IsGrantedAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                PermissionGrantProviderNames.Role,
                Arg.Any<IReadOnlyCollection<string>>(),
                AbortToken
            )
            .Returns(
                new Dictionary<string, Dictionary<string, PermissionGrantStatus>>(StringComparer.Ordinal)
                {
                    ["Admin"] = new(StringComparer.Ordinal) { ["Users.Create"] = PermissionGrantStatus.Undefined },
                    ["Manager"] = new(StringComparer.Ordinal) { ["Users.Create"] = PermissionGrantStatus.Granted },
                    ["User"] = new(StringComparer.Ordinal) { ["Users.Create"] = PermissionGrantStatus.Undefined },
                }
            );

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then — one batched call carries every role, instead of one call per role
        await _grantStore
            .Received(1)
            .IsGrantedAsync(
                Arg.Is<IReadOnlyCollection<string>>(static names => names.SequenceEqual(new[] { "Users.Create" })),
                PermissionGrantProviderNames.Role,
                Arg.Is<IReadOnlyCollection<string>>(static keys =>
                    keys.Order(StringComparer.Ordinal).SequenceEqual(new[] { "Admin", "Manager", "User" })
                ),
                AbortToken
            );
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Granted);
        result["Users.Create"].ProviderKeys.Should().Contain("Manager");
    }

    [Fact]
    public async Task should_return_granted_from_first_matching_role()
    {
        // given — two roles grant the permission; the user's role order decides which one is attributed
        var permission = _CreatePermission("Users.Create");
        var currentUser = _CreateCurrentUser(["Admin", "Manager"]);

        _grantStore
            .IsGrantedAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                PermissionGrantProviderNames.Role,
                Arg.Any<IReadOnlyCollection<string>>(),
                AbortToken
            )
            .Returns(
                new Dictionary<string, Dictionary<string, PermissionGrantStatus>>(StringComparer.Ordinal)
                {
                    ["Admin"] = new(StringComparer.Ordinal) { ["Users.Create"] = PermissionGrantStatus.Granted },
                    ["Manager"] = new(StringComparer.Ordinal) { ["Users.Create"] = PermissionGrantStatus.Granted },
                }
            );

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result["Users.Create"].Status.Should().Be(PermissionGrantStatus.Granted);
        result["Users.Create"].ProviderKeys.Should().Equal("Admin");
    }

    [Fact]
    public async Task should_return_prohibited_when_permission_denied()
    {
        // given
        var permission = _CreatePermission("Users.Delete");
        var currentUser = _CreateCurrentUser(["User"]);

        _grantStore
            .IsGrantedAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                PermissionGrantProviderNames.Role,
                Arg.Any<IReadOnlyCollection<string>>(),
                AbortToken
            )
            .Returns(
                new Dictionary<string, Dictionary<string, PermissionGrantStatus>>(StringComparer.Ordinal)
                {
                    ["User"] = new(StringComparer.Ordinal) { ["Users.Delete"] = PermissionGrantStatus.Prohibited },
                }
            );

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result["Users.Delete"].Status.Should().Be(PermissionGrantStatus.Prohibited);
        result["Users.Delete"].ProviderKeys.Should().Contain("User");
    }

    [Fact]
    public async Task should_keep_an_earlier_roles_decision_over_a_later_one()
    {
        // given — Admin prohibits, Manager grants; the earlier role's decision stands
        var permission = _CreatePermission("Users.Delete");
        var currentUser = _CreateCurrentUser(["Admin", "Manager"]);

        _grantStore
            .IsGrantedAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                PermissionGrantProviderNames.Role,
                Arg.Any<IReadOnlyCollection<string>>(),
                AbortToken
            )
            .Returns(
                new Dictionary<string, Dictionary<string, PermissionGrantStatus>>(StringComparer.Ordinal)
                {
                    ["Admin"] = new(StringComparer.Ordinal) { ["Users.Delete"] = PermissionGrantStatus.Prohibited },
                    ["Manager"] = new(StringComparer.Ordinal) { ["Users.Delete"] = PermissionGrantStatus.Granted },
                }
            );

        // when
        var result = await _sut.CheckAsync([permission], currentUser, AbortToken);

        // then
        result["Users.Delete"].Status.Should().Be(PermissionGrantStatus.Prohibited);
        result["Users.Delete"].ProviderKeys.Should().Equal("Admin");
    }

    [Fact]
    public async Task should_track_which_role_granted_each_permission()
    {
        // given
        var permission1 = _CreatePermission("Users.Create");
        var permission2 = _CreatePermission("Users.Delete");
        var currentUser = _CreateCurrentUser(["Admin", "Manager"]);

        _grantStore
            .IsGrantedAsync(
                Arg.Any<IReadOnlyCollection<string>>(),
                PermissionGrantProviderNames.Role,
                Arg.Any<IReadOnlyCollection<string>>(),
                AbortToken
            )
            .Returns(
                new Dictionary<string, Dictionary<string, PermissionGrantStatus>>(StringComparer.Ordinal)
                {
                    ["Admin"] = new(StringComparer.Ordinal)
                    {
                        ["Users.Create"] = PermissionGrantStatus.Granted,
                        ["Users.Delete"] = PermissionGrantStatus.Undefined,
                    },
                    ["Manager"] = new(StringComparer.Ordinal) { ["Users.Delete"] = PermissionGrantStatus.Granted },
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
        var currentUser = _CreateCurrentUser(["Admin"]);

        // when
        var act = () => _sut.CheckAsync([], currentUser, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static PermissionDefinition _CreatePermission(string name)
    {
        var group = new PermissionGroupDefinition("TestGroup");
        return group.AddChild(name);
    }

    private static ICurrentUser _CreateCurrentUser(HashSet<string>? roles = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Roles.Returns(roles ?? []);
        return user;
    }
}
