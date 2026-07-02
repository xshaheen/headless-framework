// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Aws;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class AwsSnsSetupTests
{
    [Fact]
    public void should_not_register_bulk_sender_through_setup()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup => setup.UseAwsSns(_Config(("SenderId", "SENDER"))));

        using var provider = services.BuildServiceProvider();

        provider.GetService<IBulkSmsSender>().Should().BeNull();
    }

    private static IConfiguration _Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(static item => new KeyValuePair<string, string?>(item.Key, item.Value))
            )
            .Build();
}
