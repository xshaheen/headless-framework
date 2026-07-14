// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Cequens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class CequensSetupTests
{
    [Fact]
    public void action_overload_configures_options()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
            setup.UseCequens(options =>
            {
                options.ApiKey = "api-key";
                options.UserName = "user";
                options.SenderName = "SENDER";
            })
        );

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<CequensSmsOptions>>().CurrentValue;

        options.ApiKey.Should().Be("api-key");
        options.UserName.Should().Be("user");
        options.SenderName.Should().Be("SENDER");
    }

    [Fact]
    public void should_register_bulk_sender_through_setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddHeadlessSms(setup =>
            setup.UseCequens(
                _Config(
                    ("SingleSmsEndpoint", "https://example.test/sms"),
                    ("TokenEndpoint", "https://example.test/auth"),
                    ("ApiKey", "api-key"),
                    ("UserName", "user"),
                    ("SenderName", "SENDER")
                )
            )
        );

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBulkSmsSender>().Should().BeSameAs(provider.GetRequiredService<ISmsSender>());
    }

    private static IConfiguration _Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(static item => new KeyValuePair<string, string?>(item.Key, item.Value))
            )
            .Build();
}
