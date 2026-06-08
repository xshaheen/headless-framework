// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.SqlServer;
using Headless.Testing.Tests;

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
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("ConnectionString"));
    }

    [Fact]
    public void should_fail_when_connection_string_is_empty()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = "" };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_fail_when_connection_string_is_whitespace()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = "   " };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_succeed_when_connection_string_is_provided()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = "Server=localhost;Database=test" };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_return_success_result_type()
    {
        // given
        var options = new SqlServerOptions { ConnectionString = "Server=localhost" };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_fail_when_owner_column_max_length_is_too_small_for_node_identity_tags()
    {
        // given
        var options = new SqlServerOptions
        {
            ConnectionString = "Server=localhost",
            OwnerColumnMaxLength = DataStorageConstants.MinimumOwnerColumnMaxLength - 1,
        };

        // when
        var result = _validator.Validate(options);

        // then
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SqlServerOptions.OwnerColumnMaxLength));
    }
}
