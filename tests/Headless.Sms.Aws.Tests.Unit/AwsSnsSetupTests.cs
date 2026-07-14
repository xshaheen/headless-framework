// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Aws;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AwsSnsSetupTests
{
    [Fact]
    public void action_overload_configures_options()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
            setup.UseAwsSns(options =>
            {
                options.SenderId = "SENDER";
                options.MaxPrice = 0.5m;
            })
        );

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<AwsSnsSmsOptions>>().CurrentValue;

        options.SenderId.Should().Be("SENDER");
        options.MaxPrice.Should().Be(0.5m);
    }

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
