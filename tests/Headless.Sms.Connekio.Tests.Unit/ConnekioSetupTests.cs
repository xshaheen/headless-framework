// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Connekio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class ConnekioSetupTests
{
    [Fact]
    public void should_register_bulk_sender_through_setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessSms(setup =>
            setup.UseConnekio(
                _Config(
                    ("SingleSmsEndpoint", "https://example.test/sms/single"),
                    ("BatchSmsEndpoint", "https://example.test/sms/batch"),
                    ("Sender", "SENDER"),
                    ("AccountId", "account"),
                    ("UserName", "user"),
                    ("Password", "password")
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
