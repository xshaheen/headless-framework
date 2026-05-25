// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Tests;

[Collection<PostgreSqlAuditLogFixture>]
public sealed class PostgreSqlAuditLogFailureModesTests(PostgreSqlAuditLogFixture fixture)
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // given — port 1 is reserved and won't accept connections; short timeout to fail fast
        const string unreachable = "Host=127.0.0.1;Port=1;Database=missing;Username=postgres;Password=postgres;Timeout=2";
        using var host = _CreateHost(unreachable);

        // when / then
        await FluentActions
            .Awaiting(() => host.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>();

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();

        await FluentActions
            .Awaiting(() => initializer.WaitForInitializationAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>();
    }

    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_authentication_fails()
    {
        // given — point at the real fixture but with a wrong password
        var badAuth = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Password = "wrong-password" }.ToString();
        using var host = _CreateHost(badAuth);

        // when / then
        var startThrew = await FluentActions
            .Awaiting(() => host.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>();
        startThrew.Which.Should().Match<Exception>(e => e is PostgresException || e.InnerException is PostgresException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();
    }

    private static IHost _CreateHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = "audit_log_pg_failure");
            setup.UsePostgreSql(connectionString);
        });

        return builder.Build();
    }
}
