// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.PostgreSql;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupTests : TestBase
{
    [Fact]
    public async Task should_register_postgresql_services_and_copy_version_when_using_connection_string()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.Version = "v7";
            setup.UseInMemoryMessageQueue();
            setup.UsePostgreSql("Host=localhost;Database=test");
        });

        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<MessageStorageMarkerService>().Name.Should().Be("PostgreSql");
        provider.GetRequiredService<IStorageInitializer>().Should().BeOfType<PostgreSqlStorageInitializer>();
        provider.GetRequiredService<IDataStorage>().Should().BeOfType<PostgreSqlDataStorage>();
        provider.GetRequiredService<IOutboxTransaction>().Should().BeOfType<PostgreSqlOutboxTransaction>();

        var options = provider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value;
        options.ConnectionString.Should().Be("Host=localhost;Database=test");
        _GetInternalString(options, "Version").Should().Be("v7");
    }

    [Fact]
    public async Task should_extract_dbcontext_configuration_when_using_entity_framework()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestMessagingDbContext>(builder =>
            builder.UseNpgsql("Host=localhost;Database=entity;Username=postgres;Password=postgres")
        );

        // when
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.Version = "v9";
            setup.UseInMemoryMessageQueue();
            setup.UseEntityFramework<TestMessagingDbContext>(postgreSql => postgreSql.Schema = "custom_schema");
        });

        await using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<PostgreSqlOptions>>().Value;
        options.ConnectionString.Should().Contain("Host=localhost");
        options.ConnectionString.Should().Contain("Database=entity");
        options.Schema.Should().Be("custom_schema");
        _GetInternalType(options, "DbContextType").Should().Be(typeof(TestMessagingDbContext));
        _GetInternalString(options, "Version").Should().Be("v9");
    }

    [Fact]
    public void should_throw_when_postgresql_configure_delegate_is_null()
    {
        // given
        var setup = _CreateSetup();

        // when
        var act = () => setup.UsePostgreSql((Action<PostgreSqlOptions>)null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_throw_when_entity_framework_configure_delegate_is_null()
    {
        // given
        var setup = _CreateSetup();

        // when
        var act = () => setup.UseEntityFramework<TestMessagingDbContext>(null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    private static MessagingSetupBuilder _CreateSetup()
    {
        return new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions(), new ConsumerRegistry());
    }

    private static string _GetInternalString(object instance, string propertyName)
    {
        return (string)
            instance
                .GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(instance)!;
    }

    private static Type? _GetInternalType(object instance, string propertyName)
    {
        return (Type?)
            instance
                .GetType()
                .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(instance);
    }

    private sealed class TestMessagingDbContext(DbContextOptions<TestMessagingDbContext> options) : DbContext(options);
}
