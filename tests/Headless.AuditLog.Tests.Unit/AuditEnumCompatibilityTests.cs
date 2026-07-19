// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;

namespace Tests;

public sealed class AuditEnumCompatibilityTests
{
    [Fact]
    public void should_keep_audit_enum_numeric_contracts_stable()
    {
        new[] { (int)AuditChangeType.Created, (int)AuditChangeType.Updated, (int)AuditChangeType.Deleted }
            .Should()
            .Equal(0, 1, 2);

        new[]
        {
            (int)SensitiveDataStrategy.Redact,
            (int)SensitiveDataStrategy.Exclude,
            (int)SensitiveDataStrategy.Transform,
        }
            .Should()
            .Equal(0, 1, 2);
    }
}
