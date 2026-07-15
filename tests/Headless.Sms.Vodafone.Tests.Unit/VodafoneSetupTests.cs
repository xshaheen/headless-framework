// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Vodafone;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class VodafoneSetupTests
{
    [Fact]
    public void action_overload_configures_options()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
            setup.UseVodafone(options =>
            {
                options.Sender = "SENDER";
                options.AccountId = "account";
                options.Password = "password";
                options.SecureHash = "secure-hash";
            })
        );

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<VodafoneSmsOptions>>().CurrentValue;

        options.Sender.Should().Be("SENDER");
        options.AccountId.Should().Be("account");
        options.Password.Should().Be("password");
        options.SecureHash.Should().Be("secure-hash");
    }

    [Fact]
    public void should_register_bulk_sender_through_setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessSms(setup =>
            setup.UseVodafone(
                _Config(
                    ("SendSmsEndpoint", "https://example.test/sms"),
                    ("Sender", "SENDER"),
                    ("AccountId", "account"),
                    ("Password", "password"),
                    ("SecureHash", "secure-hash")
                )
            )
        );

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBulkSmsSender>().Should().BeSameAs(provider.GetRequiredService<ISmsSender>());
    }

    private static IConfiguration _Config(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(static item => new KeyValuePair<string, string?>(item.Key, item.Value))
            )
            .Build();
    }
}
