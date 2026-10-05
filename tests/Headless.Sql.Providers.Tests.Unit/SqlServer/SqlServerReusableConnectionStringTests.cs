// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security;
using Headless.Testing.Tests;
using Microsoft.Data.SqlClient;

namespace Tests.SqlServer;

public sealed class SqlServerReusableConnectionStringTests : TestBase
{
    [Theory]
    [InlineData("Server=localhost;Database=app;User ID=sa;Password=secret;TrustServerCertificate=True")]
    [InlineData("Server=localhost;Database=app;Integrated Security=True")]
    [InlineData("Server=localhost;Database=app;Authentication=Active Directory Default")]
    [InlineData("Server=localhost;Database=app;User ID=client;Authentication=Active Directory Managed Identity")]
    [InlineData("")]
    public void should_return_the_connection_string_when_it_carries_the_authentication(string connectionString)
    {
        // given
        using var connection = new SqlConnection(connectionString);

        // when
        var result = connection.GetReusableConnectionString();

        // then
        result.Should().Be(connection.ConnectionString);
    }

    [Fact]
    public void should_throw_when_the_connection_authenticates_with_an_access_token()
    {
        // given
        using var connection = new SqlConnection("Server=localhost;Database=app") { AccessToken = "token" };

        // when
        var act = () => connection.GetReusableConnectionString();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*access token*");
    }

    [Fact]
    public void should_throw_when_the_connection_authenticates_with_an_access_token_callback()
    {
        // given
        using var connection = new SqlConnection("Server=localhost;Database=app")
        {
            AccessTokenCallback = (_, _) =>
                Task.FromResult(new SqlAuthenticationToken("token", DateTimeOffset.MaxValue)),
        };

        // when
        var act = () => connection.GetReusableConnectionString();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*access token*");
    }

    [Fact]
    public void should_throw_when_the_connection_authenticates_with_a_sql_credential()
    {
        // given
        using var password = new SecureString();
        password.AppendChar('x');
        password.MakeReadOnly();
        using var connection = new SqlConnection(
            "Server=localhost;Database=app",
            new SqlCredential("app_user", password)
        );

        // when
        var act = () => connection.GetReusableConnectionString();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*SqlCredential*");
    }

    [Fact]
    public void should_throw_when_the_connection_string_names_a_login_without_a_password()
    {
        // given: the shape SqlClient leaves on an opened connection unless Persist Security Info is set
        using var connection = new SqlConnection("Server=localhost;Database=app;User ID=sa");

        // when
        var act = () => connection.GetReusableConnectionString();

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*no password*");
    }

    [Fact]
    public void should_return_the_configured_connection_string_when_the_connection_lost_its_password()
    {
        // given: the configured string keeps the password the connection's own string lost when it opened
        const string configured = "Server=localhost;Database=app;User ID=sa;Password=secret";
        using var connection = new SqlConnection("Server=localhost;Database=app;User ID=sa");

        // when
        var result = connection.GetReusableConnectionString(configured);

        // then
        result.Should().Be(configured);
    }

    [Fact]
    public void should_throw_when_the_connection_is_null()
    {
        // given
        SqlConnection connection = null!;

        // when
        var act = () => connection.GetReusableConnectionString();

        // then
        act.Should().Throw<ArgumentNullException>();
    }
}
