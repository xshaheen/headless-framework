// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class ErrorDescriptorTests
{
    [Fact]
    public void should_default_to_error_severity_for_every_constructor()
    {
        var descriptor = new ErrorDescriptor("code", "description");
        var descriptorWithParams = new ErrorDescriptor(
            "code",
            "description",
            new Dictionary<string, object?>(StringComparer.Ordinal)
        );

        descriptor.Severity.Should().Be(ValidationSeverity.Error);
        descriptorWithParams.Severity.Should().Be(ValidationSeverity.Error);
    }
}
