// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.PostgreSql;

namespace Tests.PostgreSql;

/// <summary>
/// Unit tests for <see cref="NpgsqlConnectionFactory"/>.
/// These are structural tests; integration tests require actual database.
/// </summary>
public sealed class NpgsqlConnectionFactoryTests
{
    [Fact]
    public void should_implement_ISqlConnectionFactory()
    {
        // given
        const string connectionString = "Host=localhost;Database=test";

        // when
        var sut = new NpgsqlConnectionFactory(connectionString);

        // then
        sut.Should().BeAssignableTo<ISqlConnectionFactory>();
    }

    [Fact]
    public void should_store_connection_string()
    {
        // given
        const string connectionString = "Host=localhost;Database=mydb;Port=5432;Username=admin";

        // when
        var sut = new NpgsqlConnectionFactory(connectionString);

        // then - constructor should store the connection string for later retrieval
        sut.GetConnectionString().Should().Be(connectionString);
    }

    [Fact]
    public void should_return_connection_string()
    {
        // given
        const string connectionString = "Host=localhost;Database=test;Port=5432";

        // when
        var sut = new NpgsqlConnectionFactory(connectionString);

        // then
        sut.GetConnectionString().Should().Be(connectionString);
    }

    [Fact]
    public void should_implement_interface_explicitly()
    {
        // given
        const string connectionString = "Host=localhost;Database=test";
        var sut = new NpgsqlConnectionFactory(connectionString);

        // when - cast to interface to verify explicit implementation exists
        ISqlConnectionFactory interfaceRef = sut;

        // then - verify interface method is accessible
        interfaceRef.Should().NotBeNull();
        interfaceRef.GetConnectionString().Should().Be(connectionString);

        // The explicit ISqlConnectionFactory.CreateNewConnectionAsync delegates to the public method
        // Both methods should be available (can't test actual connection without real DB)
        var publicMethod = sut.GetType().GetMethod("CreateNewConnectionAsync", [typeof(CancellationToken)]);
        publicMethod.Should().NotBeNull("public CreateNewConnectionAsync should exist");

        var interfaceMethod = typeof(ISqlConnectionFactory).GetMethod("CreateNewConnectionAsync");
        interfaceMethod.Should().NotBeNull("interface method should exist");
    }
}
