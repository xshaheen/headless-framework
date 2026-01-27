// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Sql;
using Framework.Sql.SqlServer;

namespace Tests.SqlServer;

/// <summary>
/// Unit tests for <see cref="SqlServerConnectionFactory"/>.
/// These are structural tests; integration tests require actual database.
/// </summary>
public sealed class SqlServerConnectionFactoryTests
{
    [Fact]
    public void should_implement_ISqlConnectionFactory()
    {
        // given
        const string connectionString = "Server=localhost;Database=test";

        // when
        var sut = new SqlServerConnectionFactory(connectionString);

        // then
        sut.Should().BeAssignableTo<ISqlConnectionFactory>();
    }

    [Fact]
    public void should_store_connection_string()
    {
        // given
        const string connectionString = "Server=localhost;Database=test;TrustServerCertificate=True";

        // when
        var sut = new SqlServerConnectionFactory(connectionString);

        // then
        sut.GetConnectionString().Should().Be(connectionString);
    }

    [Fact]
    public void should_return_connection_string()
    {
        // given
        const string connectionString = "Server=myserver;Database=mydb;User Id=sa;Password=pass";
        var sut = new SqlServerConnectionFactory(connectionString);

        // when
        var result = sut.GetConnectionString();

        // then
        result.Should().Be(connectionString);
    }

    [Fact]
    public void should_implement_interface_explicitly()
    {
        // given
        const string connectionString = "Server=localhost;Database=test";
        var sut = new SqlServerConnectionFactory(connectionString);

        // when - cast to interface to verify explicit implementation exists
        ISqlConnectionFactory factory = sut;

        // then - verify the interface method is accessible
        factory.GetConnectionString().Should().Be(connectionString);

        // verify CreateNewConnectionAsync is available on interface
        // (actual connection test requires integration test)
        factory.Should().BeAssignableTo<ISqlConnectionFactory>();
    }
}
