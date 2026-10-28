// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Storage.SqlServer;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public async Task should_run_durable_attempts_in_a_transaction_by_default()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
            setup.UseSqlServer("Server=localhost;Database=test;TrustServerCertificate=True")
        );

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        // then
        services
            .Should()
            .ContainSingle(descriptor =>
                descriptor.ServiceType == typeof(IInboxTransactionRunner)
                && descriptor.Lifetime == ServiceLifetime.Scoped
            );
        await using (var scope = provider.CreateAsyncScope())
        {
            scope
                .ServiceProvider.GetRequiredService<IInboxTransactionRunner>()
                .Should()
                .BeOfType<AdoNetInboxTransactionRunner>();
        }

        _StorageGuarantee(provider).Should().Be(InboxGuarantee.Transactional);
    }

    [Fact]
    public async Task should_declare_durable_inbox_when_transactional_inbox_is_disabled()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
            setup.UseSqlServer(options =>
            {
                options.ConnectionString = "Server=localhost;Database=test;TrustServerCertificate=True";
                options.EnableTransactionalInbox = false;
            })
        );

        await using var provider = services.BuildServiceProvider();

        // then
        _StorageGuarantee(provider).Should().Be(InboxGuarantee.Durable);
    }

    [Fact]
    public async Task should_bind_transactional_inbox_switch_from_configuration()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ConnectionString"] = "Server=localhost;Database=test;TrustServerCertificate=True",
                    ["EnableTransactionalInbox"] = "false",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup => setup.UseSqlServer(configuration));

        await using var provider = services.BuildServiceProvider();

        // then
        _StorageGuarantee(provider).Should().Be(InboxGuarantee.Durable);
    }

    private static InboxGuarantee? _StorageGuarantee(IServiceProvider provider)
    {
        return provider
            .GetServices<MessagingProviderCapabilities>()
            .Single(capability => string.Equals(capability.Provider, "SqlServer", StringComparison.Ordinal))
            .InboxGuarantee;
    }

    [Fact]
    public async Task should_use_shared_connection_string_when_using_parameterless_overload()
    {
        const string connectionString = "Server=localhost;Database=shared;TrustServerCertificate=True";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerSql(connectionString);

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSqlServer();
        });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<SqlServerOptions>>().Value.ConnectionString.Should().Be(connectionString);
    }

    [Fact]
    public async Task should_prefer_own_connection_string_over_shared_connection()
    {
        const string ownConnectionString = "Server=localhost;Database=own;TrustServerCertificate=True";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqlServerSql("Server=localhost;Database=shared;TrustServerCertificate=True");

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSqlServer(ownConnectionString);
        });

        await using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<IOptions<SqlServerOptions>>()
            .Value.ConnectionString.Should()
            .Be(ownConnectionString);
    }

    [Fact]
    public async Task should_throw_naming_add_sql_server_sql_when_parameterless_overload_has_no_shared_connection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseSqlServer();
        });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<SqlServerOptions>>().Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddSqlServerSql(connectionString)*");
    }
}
