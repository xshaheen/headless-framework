// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Permissions.Models;
using Framework.Testing.Tests;

namespace Tests.Models;

public sealed class MultiplePermissionGrantResultTests : TestBase
{
    [Fact]
    public void should_track_multiple_results()
    {
        // given
        var permissions = new List<string> { "Read", "Write", "Delete" };

        // when
        var result = new MultiplePermissionGrantResult(permissions, isGranted: true);

        // then
        result.Should().HaveCount(3);
        result["Read"].Should().BeTrue();
        result["Write"].Should().BeTrue();
        result["Delete"].Should().BeTrue();
    }

    [Fact]
    public void should_return_all_granted_true_when_all_permissions_granted()
    {
        // given
        var result = new MultiplePermissionGrantResult
        {
            ["Read"] = true,
            ["Write"] = true,
            ["Delete"] = true,
        };

        // then
        result.AllGranted.Should().BeTrue();
        result.AllProhibited.Should().BeFalse();
    }

    [Fact]
    public void should_return_all_prohibited_true_when_all_permissions_denied()
    {
        // given
        var result = new MultiplePermissionGrantResult
        {
            ["Read"] = false,
            ["Write"] = false,
            ["Delete"] = false,
        };

        // then
        result.AllGranted.Should().BeFalse();
        result.AllProhibited.Should().BeTrue();
    }
}
