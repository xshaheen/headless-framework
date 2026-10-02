// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;

namespace Tests;

public sealed class ChangeFrequencyCompatibilityTests
{
    [Fact]
    public void should_keep_change_frequency_numeric_contract_stable()
    {
        Enum.GetValues<ChangeFrequency>().Select(static value => (int)value).Should().Equal(Enumerable.Range(0, 7));
    }
}
