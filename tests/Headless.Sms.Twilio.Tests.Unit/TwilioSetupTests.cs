// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Twilio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class TwilioSetupTests
{
    [Fact]
    public void action_overload_configures_options()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
            setup.UseTwilio(options =>
            {
                options.Sid = "AC0000000000000000000000000000000";
                options.AuthToken = "token";
                options.PhoneNumber = "+15551234567";
            })
        );

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<TwilioSmsOptions>>().CurrentValue;

        options.Sid.Should().Be("AC0000000000000000000000000000000");
        options.AuthToken.Should().Be("token");
        options.PhoneNumber.Should().Be("+15551234567");
    }

    [Fact]
    public void should_not_register_bulk_sender_through_setup()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
            setup.UseTwilio(
                _Config(
                    ("Sid", "AC0000000000000000000000000000000"),
                    ("AuthToken", "token"),
                    ("PhoneNumber", "+15551234567")
                )
            )
        );

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
