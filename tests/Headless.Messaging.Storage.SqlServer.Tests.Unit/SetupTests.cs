// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Storage.SqlServer;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public async Task should_not_enable_transactional_outbox_on_raw_path()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.UseSqlServer("Server=localhost;Database=test;TrustServerCertificate=True");
        });

        await using var provider = services.BuildServiceProvider();

        // The raw (non-EF) path never enables the transactional inbox runner — that only exists when
        // UseEntityFramework<TContext>() wires an EF-backed transaction boundary.
        provider.GetService<IInboxTransactionRunner>().Should().BeNull();
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
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
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
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
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
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.UseSqlServer();
        });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<SqlServerOptions>>().Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddSqlServerSql(connectionString)*");
    }
}
