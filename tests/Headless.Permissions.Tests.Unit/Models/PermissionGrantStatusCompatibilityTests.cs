// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Models;

namespace Tests.Models;

public sealed class PermissionGrantStatusCompatibilityTests
{
    [Fact]
    public void should_keep_permission_grant_status_numeric_contract_stable()
    {
        new[]
        {
            (int)PermissionGrantStatus.Undefined,
            (int)PermissionGrantStatus.Granted,
            (int)PermissionGrantStatus.Prohibited,
        }
            .Should()
            .Equal(0, 1, 2);
    }
}
