// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Customizer;

namespace Tests;

public sealed class ConfigurationTypeCompatibilityTests
{
    [Fact]
    public void should_keep_configuration_type_numeric_contract_stable()
    {
        new[] { (int)ConfigurationType.UseModelCustomizer, (int)ConfigurationType.IgnoreModelCustomizer }
            .Should()
            .Equal(0, 1);
    }
}
