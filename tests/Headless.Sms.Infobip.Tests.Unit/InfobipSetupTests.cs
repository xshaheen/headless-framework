// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Infobip;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class InfobipSetupTests
{
    [Fact]
    public void should_register_bulk_sender_through_setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessSms(setup =>
            setup.UseInfobip(_Config(("Sender", "SENDER"), ("ApiKey", "api-key"), ("BasePath", "https://example.test")))
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
