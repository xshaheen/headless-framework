// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.CommitCoordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.PostgreSql;
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
            setup.UseInMemory();
            setup.UsePostgreSql("Host=localhost;Database=test");
        });

        await using var provider = services.BuildServiceProvider();

        provider
            .GetRequiredService<ICurrentCommitCoordinator>()
            .GetType()
            .Name.Should()
            .Be("MessagingNullCommitCoordinator");
    }

    [Fact]
    public void should_throw_when_postgresql_configure_delegate_is_null()
    {
        var setup = new MessagingSetupBuilder(new ServiceCollection(), new MessagingOptions(), new ConsumerRegistry());

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
