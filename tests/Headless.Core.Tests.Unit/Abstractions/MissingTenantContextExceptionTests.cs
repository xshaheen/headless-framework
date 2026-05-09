// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class MissingTenantContextExceptionTests
{
    [Fact]
    public void should_expose_default_failure_code()
    {
        // when
        var exception = new MissingTenantContextException();

        // then
        exception.FailureCode.Should().Be(MissingTenantContextException.DefaultFailureCode);
        exception.FailureCode.Should().Be("MissingTenantContext");
    }

    [Fact]
    public void should_accept_custom_failure_code()
    {
        // when
        var exception = new MissingTenantContextException { FailureCode = "CustomTenantFailure" };

        // then
        exception.FailureCode.Should().Be("CustomTenantFailure");
    }
}
