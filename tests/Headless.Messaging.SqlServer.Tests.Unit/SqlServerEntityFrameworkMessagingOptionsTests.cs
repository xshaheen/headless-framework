// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.SqlServer;

namespace Tests;

public sealed class SqlServerEntityFrameworkMessagingOptionsTests : TestBase
{
    [Fact]
    public void should_have_default_schema_set()
    {
        // when
        var options = new SqlServerEntityFrameworkMessagingOptions();

        // then
        options.Schema.Should().Be(SqlServerEntityFrameworkMessagingOptions.DefaultSchema);
    }

    [Fact]
    public void should_accept_valid_schema_name()
    {
        // given
        var options = new SqlServerEntityFrameworkMessagingOptions();
        const string validSchema = "my_custom_schema";

        // when
        options.Schema = validSchema;

        // then
        options.Schema.Should().Be(validSchema);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_throw_when_schema_is_null_or_whitespace(string? schema)
    {
        // given
        var options = new SqlServerEntityFrameworkMessagingOptions();

        // when
        var act = () => options.Schema = schema!;

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_schema_exceeds_max_length()
    {
        // given
        var options = new SqlServerEntityFrameworkMessagingOptions();
        var longSchema = new string('a', SqlServerEntityFrameworkMessagingOptions.MaxSchemaLength + 1);

        // when
        var act = () => options.Schema = longSchema;

        // then
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage($"*{SqlServerEntityFrameworkMessagingOptions.MaxSchemaLength}*");
    }

    [Fact]
    public void should_accept_schema_at_max_length()
    {
        // given
        var options = new SqlServerEntityFrameworkMessagingOptions();
        var maxLengthSchema = new string('a', SqlServerEntityFrameworkMessagingOptions.MaxSchemaLength);

        // when
        options.Schema = maxLengthSchema;

        // then
        options.Schema.Should().Be(maxLengthSchema);
    }

    [Fact]
    public void should_have_max_schema_length_of_128()
    {
        // SQL Server identifier limit
        SqlServerEntityFrameworkMessagingOptions.MaxSchemaLength.Should().Be(128);
    }

    [Theory]
    [InlineData("_valid_schema")]
    [InlineData("Valid123")]
    [InlineData("_")]
    [InlineData("a")]
    [InlineData("schema_with_underscores_123")]
    [InlineData("@temp_schema")] // SQL Server allows @ prefix
    [InlineData("#local_temp")] // SQL Server allows # prefix
    [InlineData("dbo")]
    [InlineData("MySchema$Test")] // SQL Server allows $ in identifiers
    public void should_accept_valid_identifier_patterns(string schema)
    {
        // given
        var options = new SqlServerEntityFrameworkMessagingOptions();

        // when
        options.Schema = schema;

        // then
        options.Schema.Should().Be(schema);
    }

    [Theory]
    [InlineData("1starts_with_digit")]
    [InlineData("has-hyphen")]
    [InlineData("has.dot")]
    [InlineData("has space")]
    [InlineData("has\"quote")]
    [InlineData("has;semicolon")]
    [InlineData("dbo]; DROP TABLE users; --")] // SQL injection attempt
    [InlineData("schema';\n DROP TABLE users; --")] // SQL injection attempt
    [InlineData("[dbo]")] // Brackets not allowed (they're for quoting)
    [InlineData("schema\r\nGO\r\nDROP TABLE")] // SQL batch separator injection
    [InlineData("schema/*comment*/")] // SQL comment injection
    [InlineData("test\0null")] // Null byte injection
    public void should_throw_for_invalid_identifier_patterns(string schema)
    {
        // given
        var options = new SqlServerEntityFrameworkMessagingOptions();

        // when
        var act = () => options.Schema = schema;

        // then
        act.Should().Throw<ArgumentException>().WithMessage("*start with a letter*");
    }

    [Theory]
    [InlineData("dbo]; DROP TABLE--")]
    [InlineData("'; DELETE FROM users WHERE '1'='1")]
    [InlineData("test'); EXEC xp_cmdshell('dir'); --")]
    [InlineData("schema; SHUTDOWN WITH NOWAIT; --")]
    [InlineData("test' OR '1'='1")]
    [InlineData("x'; EXEC sp_configure 'xp_cmdshell', 1; --")]
    public void should_reject_sql_injection_patterns(string schema)
    {
        // given
        var options = new SqlServerEntityFrameworkMessagingOptions();

        // when
        var act = () => options.Schema = schema;

        // then
        act.Should().Throw<ArgumentException>();
    }
}
