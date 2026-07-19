// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;

namespace Tests;

public sealed class TenancyEnumCompatibilityTests
{
    [Fact]
    public void should_keep_diagnostic_severity_numeric_contract_stable()
    {
        new[]
        {
            (int)HeadlessTenancyDiagnosticSeverity.Information,
            (int)HeadlessTenancyDiagnosticSeverity.Warning,
            (int)HeadlessTenancyDiagnosticSeverity.Error,
        }
            .Should()
            .Equal(0, 1, 2);
    }
}
