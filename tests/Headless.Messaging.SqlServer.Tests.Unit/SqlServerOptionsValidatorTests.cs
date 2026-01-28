// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.SqlServer;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SqlServerOptionsValidatorTests : TestBase
{
    private readonly SqlServerOptionsValidator _validator = new();

    [Fact]
    public void should_fail_when_connection_string_and_db_context_are_both_missing()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = null! };

        // when
        var result = _validator.Validate(null, options);

        // then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ConnectionString");
    }

    [Fact]
    public void should_fail_when_connection_string_is_empty()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = "" };

        // when
        var result = _validator.Validate(null, options);

        // then
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_connection_string_is_whitespace()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = "   " };

        // when
        var result = _validator.Validate(null, options);

        // then
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void should_succeed_when_connection_string_is_provided()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = "Server=localhost;Database=test" };

        // when
        var result = _validator.Validate(null, options);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void should_return_success_result_type()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = "Server=localhost" };

        // when
        var result = _validator.Validate("TestOptions", options);

        // then
        result.Should().Be(ValidateOptionsResult.Success);
    }
}
