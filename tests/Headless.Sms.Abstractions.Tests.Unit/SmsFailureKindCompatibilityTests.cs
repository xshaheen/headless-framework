// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;

namespace Tests;

public sealed class SmsFailureKindCompatibilityTests
{
    [Fact]
    public void should_keep_failure_kind_numeric_contract_stable()
    {
        Enum.GetValues<SmsFailureKind>().Select(static value => (int)value).Should().Equal(Enumerable.Range(0, 7));
    }
}
