// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Hosting.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

[Collection<SqlServerAuditLogFixture>]
public sealed class SqlServerAuditLogFailureModesTests(SqlServerAuditLogFixture fixture)
{
    [Fact]
    public async Task should_throw_and_keep_initializer_unmarked_when_database_unreachable()
    {
        // given — port 1 is reserved and won't accept connections; short timeout to fail fast
        const string unreachable = "Server=127.0.0.1,1;Database=missing;User Id=sa;Password=Headless!Pass1;Connect Timeout=2;TrustServerCertificate=true";
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
        var badAuth = new SqlConnectionStringBuilder(fixture.ConnectionString) { Password = "wrong-password" }.ToString();
        using var host = _CreateHost(badAuth);

        // when / then
        var startThrew = await FluentActions
            .Awaiting(() => host.StartAsync(TestContext.Current.CancellationToken))
            .Should()
            .ThrowAsync<Exception>();
        startThrew.Which.Should().Match<Exception>(e => e is SqlException || e.InnerException is SqlException);

        var initializer = host.Services.GetRequiredService<IEnumerable<IInitializer>>().Single();
        initializer.IsInitialized.Should().BeFalse();
    }

    private static IHost _CreateHost(string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessAuditLog();
        builder.Services.AddHeadlessAuditLog(setup =>
        {
            setup.ConfigureStorage(options => options.Schema = "audit_log_sql_failure");
            setup.UseSqlServer(connectionString);
        });

        return builder.Build();
    }
}
