// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Tests;

public sealed class MissedRunPolicyStorageTests : TestBase
{
    [Fact]
    public void named_policy_values_round_trip()
    {
        var converter = new EnumToStringConverter<MissedRunPolicy>();

        foreach (var policy in Enum.GetValues<MissedRunPolicy>())
        {
            var stored = converter.ConvertToProvider(policy);
            converter.ConvertFromProvider(stored).Should().Be(policy);
        }
    }
}
