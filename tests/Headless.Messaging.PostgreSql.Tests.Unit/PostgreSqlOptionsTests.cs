// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.PostgreSql;
using Npgsql;

namespace Tests;

public sealed class PostgreSqlOptionsTests : TestBase
{
    [Fact]
    public void should_inherit_from_entity_framework_messaging_options()
    {
        // when
        var options = new PostgreSqlOptions();

        // then
        options.Should().BeAssignableTo<PostgreSqlEntityFrameworkMessagingOptions>();
    }

    [Fact]
    public void should_have_default_schema_from_base()
    {
        // when
        var options = new PostgreSqlOptions();

        // then
        options.Schema.Should().Be(PostgreSqlEntityFrameworkMessagingOptions.DefaultSchema);
    }

    [Fact]
    public void should_allow_setting_connection_string()
    {
        // given
        const string connectionString = "Host=localhost;Database=test";

        // when
        var options = new PostgreSqlOptions { ConnectionString = connectionString };

        // then
        options.ConnectionString.Should().Be(connectionString);
    }

    [Fact]
    public void should_allow_setting_data_source()
    {
        // given
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=test");

        // when
        var options = new PostgreSqlOptions { DataSource = dataSource };

        // then
        options.DataSource.Should().BeSameAs(dataSource);
        dataSource.Dispose();
    }

    [Fact]
    public void should_allow_null_connection_string()
    {
        // when
        var options = new PostgreSqlOptions { ConnectionString = null };

        // then
        options.ConnectionString.Should().BeNull();
    }

    [Fact]
    public void should_allow_null_data_source()
    {
        // when
        var options = new PostgreSqlOptions { DataSource = null };

        // then
        options.DataSource.Should().BeNull();
    }
}
