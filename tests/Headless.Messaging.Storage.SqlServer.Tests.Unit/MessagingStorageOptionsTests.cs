// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// The messaging schema is feature-owned: it is configured once through <c>ConfigureStorage</c> and the
/// SQL Server provider validates it against SQL Server identifier rules.
/// </summary>
public sealed class MessagingStorageOptionsTests : TestBase
{
    private const string _ConnectionString = "Server=localhost;Database=test;Integrated Security=true";

    [Fact]
    public async Task should_default_the_schema_to_the_feature_name()
    {
        // given
        var services = _BuildServices(setup => setup.UseSqlServer(_ConnectionString));

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<MessagingStorageOptions>>().Value.Schema.Should().Be("messaging");
    }

    [Fact]
    public async Task should_use_the_schema_configured_on_the_feature_options()
    {
        // given
        var services = _BuildServices(setup =>
        {
            setup.ConfigureStorage(storage => storage.Schema = "msg_custom");
            setup.UseSqlServer(_ConnectionString);
        });

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<MessagingStorageOptions>>().Value.Schema.Should().Be("msg_custom");
    }

    [Fact]
    public async Task should_bind_the_schema_from_the_messaging_storage_configuration_section()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ConnectionString"] = _ConnectionString,
                    ["Headless:Messaging:Storage:Schema"] = "msg_from_configuration",
                }
            )
            .Build();

        var services = _BuildServices(setup => setup.UseSqlServer(configuration));

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<IOptions<MessagingStorageOptions>>()
            .Value.Schema.Should()
            .Be("msg_from_configuration");
    }

    [Fact]
    public async Task should_prefer_the_configured_schema_over_the_bound_configuration_value()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["ConnectionString"] = _ConnectionString,
                    ["Headless:Messaging:Storage:Schema"] = "msg_from_configuration",
                }
            )
            .Build();

        var services = _BuildServices(setup =>
        {
            setup.ConfigureStorage(storage => storage.Schema = "msg_from_code");
            setup.UseSqlServer(configuration);
        });

        // when
        await using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<MessagingStorageOptions>>().Value.Schema.Should().Be("msg_from_code");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1_leading_digit")]
    [InlineData("has space")]
    [InlineData("drop;table")]
    public async Task should_reject_a_schema_that_is_not_a_sql_server_identifier(string schema)
    {
        // given
        var services = _BuildServices(setup =>
        {
            setup.ConfigureStorage(storage => storage.Schema = schema);
            setup.UseSqlServer(_ConnectionString);
        });

        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<MessagingStorageOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    private static ServiceCollection _BuildServices(Action<MessagingSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(configure);

        return services;
    }
}
