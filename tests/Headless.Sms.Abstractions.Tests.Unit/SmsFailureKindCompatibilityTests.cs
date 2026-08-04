// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;

namespace Tests;

public sealed class SmsFailureKindCompatibilityTests
{
    [Fact]
    public void should_keep_failure_kind_numeric_contract_stable()
    {
#pragma warning disable MA0078 // Cast<int>() cannot perform the required enum-to-underlying-value conversion and throws at runtime.
        Enum.GetValues<SmsFailureKind>().Select(static value => (int)value).Should().Equal(Enumerable.Range(0, 7));
#pragma warning restore MA0078
    }
}
