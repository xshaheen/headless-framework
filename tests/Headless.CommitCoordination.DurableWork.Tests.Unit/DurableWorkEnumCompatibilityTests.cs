// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination.DurableWork;

namespace Tests;

public sealed class DurableWorkEnumCompatibilityTests
{
    [Fact]
    public void should_keep_provider_mismatch_policy_numeric_contract_stable()
    {
        new[] { (int)DurableWorkProviderMismatchPolicy.Throw, (int)DurableWorkProviderMismatchPolicy.Warn }
            .Should()
            .Equal(0, 1);
    }
}
