// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.PostgreSql;
using Npgsql;

namespace Tests;

public sealed class PostgreSqlOptionsValidatorTests : TestBase
{
    private readonly PostgreSqlOptionsValidator _sut = new();

    [Fact]
    public void should_succeed_when_connection_string_is_provided()
    {
        // given
        var options = new PostgreSqlOptions { ConnectionString = "Host=localhost;Database=test" };

        // when
        var result = _sut.Validate(null, options);

        // then
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void should_succeed_when_data_source_is_provided()
    {
        // given
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test");
        var options = new PostgreSqlOptions { DataSource = dataSource };

        // when
        var result = _sut.Validate(null, options);

        // then
        result.Succeeded.Should().BeTrue();
        dataSource.Dispose();
    }

    [Fact]
    public void should_succeed_when_both_connection_string_and_data_source_are_provided()
    {
        // given
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test");
        var options = new PostgreSqlOptions
        {
            ConnectionString = "Host=localhost;Database=other",
            DataSource = dataSource,
        };

        // when
        var result = _sut.Validate(null, options);

        // then
        result.Succeeded.Should().BeTrue();
        dataSource.Dispose();
    }

    [Fact]
    public void should_fail_when_neither_connection_string_nor_data_source_is_provided()
    {
        // given
        var options = new PostgreSqlOptions();

        // when
        var result = _sut.Validate(null, options);

        // then
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DataSource");
        result.FailureMessage.Should().Contain("ConnectionString");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_fail_when_connection_string_is_null_or_whitespace_and_data_source_is_null(
        string? connectionString
    )
    {
        // given
        var options = new PostgreSqlOptions { ConnectionString = connectionString };

        // when
        var result = _sut.Validate(null, options);

        // then
        result.Failed.Should().BeTrue();
    }

    [Fact]
    public void should_pass_named_options()
    {
        // given
        var options = new PostgreSqlOptions { ConnectionString = "Host=localhost;Database=test" };

        // when
        var result = _sut.Validate("named", options);

        // then
        result.Succeeded.Should().BeTrue();
    }
}
