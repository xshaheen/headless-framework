// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions;
using Headless.Testing.Tests;

namespace Tests.Requirements;

public sealed class PermissionsRequirementTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_reject_an_empty_permission_list(bool requiresAll)
    {
        // An empty all-of set is vacuously satisfied, so it would authorize every caller.
        var act = () => new PermissionsRequirement([], requiresAll);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_reject_a_null_permission_list()
    {
        var act = () => new PermissionsRequirement(null!, requiresAll: true);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_drop_duplicate_permission_names_keeping_first_order()
    {
        var requirement = new PermissionsRequirement(
            ["Orders.Edit", "Orders.Create", "Orders.Edit"],
            requiresAll: true
        );

        requirement.PermissionNames.Should().Equal("Orders.Edit", "Orders.Create");
        requirement.RequiresAll.Should().BeTrue();
    }
}
