// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sitemaps;

namespace Tests;

public sealed class ChangeFrequencyCompatibilityTests
{
    [Fact]
    public void should_keep_change_frequency_numeric_contract_stable()
    {
#pragma warning disable MA0078 // Cast<int>() cannot perform the required enum-to-underlying-value conversion and throws at runtime.
        Enum.GetValues<ChangeFrequency>().Select(static value => (int)value).Should().Equal(Enumerable.Range(0, 7));
#pragma warning restore MA0078
    }
}
