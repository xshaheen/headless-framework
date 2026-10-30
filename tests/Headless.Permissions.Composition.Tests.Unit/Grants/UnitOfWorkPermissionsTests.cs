// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Testing.Tests;
using Headless.UnitOfWork;

namespace Tests.Grants;

public sealed class UnitOfWorkPermissionsTests : TestBase
{
    private readonly IUnitOfWorkPermissions _feature = Substitute.For<IUnitOfWorkPermissions>();
    private readonly IUnitOfWork _unit = Substitute.For<IUnitOfWork>();

    public UnitOfWorkPermissionsTests()
    {
        _unit
            .GetOrAdd(
                Arg.Any<IUnitOfWorkPermissions>(),
                Arg.Any<Func<IUnitOfWork, IUnitOfWorkPermissions, UnitOfWorkPermissions>>()
            )
            .Returns(call =>
                call.Arg<Func<IUnitOfWork, IUnitOfWorkPermissions, UnitOfWorkPermissions>>()(
                    _unit,
                    call.Arg<IUnitOfWorkPermissions>()
                )
            );
    }

    [Fact]
    public async Task should_bind_every_write_to_the_unit_it_was_read_from()
    {
        // given
        _unit.GetFeature<IUnitOfWorkPermissions>().Returns(_feature);
        var permissions = _unit.Permissions;
        string[] names = ["Users.Create", "Users.Delete"];

        // when
        await permissions.SetAsync("Users.Update", "Role", "admin", true, AbortToken);
        await permissions.SetAsync(names, "Role", "admin", false, AbortToken);
        await permissions.DeleteAsync("Role", "admin", AbortToken);
        await permissions.GrantToUserAsync("Users.Create", "u1", AbortToken);
        await permissions.RevokeFromUserAsync("Users.Create", "u1", AbortToken);
        await permissions.GrantToRoleAsync("Users.Create", "admin", AbortToken);
        await permissions.RevokeFromRoleAsync("Users.Create", "admin", AbortToken);

        // then
        await _feature.Received(1).SetAsync(_unit, "Users.Update", "Role", "admin", true, AbortToken);
        await _feature.Received(1).SetAsync(_unit, names, "Role", "admin", false, AbortToken);
        await _feature.Received(1).DeleteAsync(_unit, "Role", "admin", AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, "Users.Create", PermissionGrantProviderNames.User, "u1", true, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, "Users.Create", PermissionGrantProviderNames.User, "u1", false, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, "Users.Create", PermissionGrantProviderNames.Role, "admin", true, AbortToken);
        await _feature
            .Received(1)
            .SetAsync(_unit, "Users.Create", PermissionGrantProviderNames.Role, "admin", false, AbortToken);
    }

    [Fact]
    public void should_name_the_registration_when_permissions_are_not_registered()
    {
        // given
        _unit.GetFeature<IUnitOfWorkPermissions>().Returns((IUnitOfWorkPermissions?)null);

        // when
        var act = () => _unit.Permissions;

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessPermissions*");
    }
}
