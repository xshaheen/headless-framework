// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
using Headless.Sql;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public async Task should_register_postgresql_services_and_copy_version_when_using_connection_string()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.Version = "v7";
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.UseInMemory();
            setup.UsePostgreSql("Host=localhost;Database=test");
        });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<MessageStorageMarkerService>().Name.Should().Be("PostgreSql");
        provider.GetRequiredService<IStorageInitializer>().Should().BeOfType<PostgreSqlStorageInitializer>();
        provider.GetRequiredService<IDataStorage>().Should().BeOfType<PostgreSqlDataStorage>();

        var options = provider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value;
        options.ConnectionString.Should().Be("Host=localhost;Database=test");
        _GetInternalString(options, "Version").Should().Be("v7");
    }

    [Fact]
    public async Task should_not_enable_transactional_outbox_on_raw_path()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.UseInMemory();
            setup.UsePostgreSql("Host=localhost;Database=test");
        });

        await using var provider = services.BuildServiceProvider();

        // The raw (non-EF) path never enables the transactional inbox runner — that only exists when
        // UseEntityFramework<TContext>() wires an EF-backed transaction boundary.
        provider.GetService<IInboxTransactionRunner>().Should().BeNull();
    }

    [Fact]
    public async Task should_use_shared_connection_string_when_using_parameterless_overload()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql("Host=localhost;Database=shared");

        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.UseInMemory();
            setup.UsePostgreSql();
        });

        await using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<IOptions<PostgreSqlOptions>>()
            .Value.ConnectionString.Should()
            .Be("Host=localhost;Database=shared");
    }

    [Fact]
    public async Task should_prefer_own_connection_string_over_shared_connection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgreSqlSql("Host=localhost;Database=shared");

        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.UseInMemory();
            setup.UsePostgreSql("Host=localhost;Database=own");
        });

        await using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<IOptions<PostgreSqlOptions>>()
            .Value.ConnectionString.Should()
            .Be("Host=localhost;Database=own");
    }

    [Fact]
    public async Task should_throw_naming_add_postgresql_sql_when_parameterless_overload_has_no_shared_connection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
            setup.UseInMemory();
            setup.UsePostgreSql();
        });

        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value;

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddPostgreSqlSql(connectionString)*");
    }

    [Fact]
    public void should_throw_when_postgresql_configure_delegate_is_null()
    {
        var setup = new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions());

        var act = () => setup.UsePostgreSql((Action<PostgreSqlOptions>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static string _GetInternalString(object instance, string propertyName)
    {
        return (string)
            instance
                .GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(instance)!;
    }
}
