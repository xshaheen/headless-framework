// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Headless.Sms;
using Headless.Sms.Aws;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AwsSnsNamedSetupTests
{
    // Region + explicit (fake) credentials so the SNS client constructs deterministically without any
    // ambient AWS configuration or network access.
    private static AWSOptions _AwsOptions(RegionEndpoint? region = null)
    {
        return new()
        {
            Region = region ?? RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("fake-access-key", "fake-secret-key"),
        };
    }

    private static IConfiguration _Config(string senderId)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["SenderId"] = senderId })
            .Build();
    }

    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return services;
    }

    [Fact]
    public void should_resolve_via_factory_and_keyed_di_with_default_unaffected_when_named_aws()
    {
        // given
        var services = _Services();

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseAwsSns(_Config("DEFAULT-SENDER"), _AwsOptions());
            setup.AddNamed("otp", instance => instance.UseAwsSns(_Config("OTP-SENDER"), _AwsOptions()));
        });
        using var provider = services.BuildServiceProvider();

        // then - named options bind per name and the sender resolves through both surfaces.
        var monitor = provider.GetRequiredService<IOptionsMonitor<AwsSnsSmsOptions>>();
        monitor.CurrentValue.SenderId.Should().Be("DEFAULT-SENDER");
        monitor.Get("otp").SenderId.Should().Be("OTP-SENDER");

        var named = provider.GetRequiredService<ISmsSenderProvider>().GetSender("otp");
        named.Should().BeOfType<AwsSnsSmsSender>();
        provider.GetRequiredKeyedService<ISmsSender>("otp").Should().BeSameAs(named);
        provider.GetRequiredService<ISmsSender>().Should().NotBeSameAs(named);
    }

    [Fact]
    public void should_isolate_sns_clients_across_two_named_instances()
    {
        // given - distinct regions per instance so the test proves each keyed client is built from its own
        // options, not merely that the two client instances differ.
        var services = _Services();

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseAwsSns(_Config("DEFAULT-SENDER"), _AwsOptions());
            setup.AddNamed(
                "east",
                instance => instance.UseAwsSns(_Config("EAST-SENDER"), _AwsOptions(RegionEndpoint.USEast1))
            );
            setup.AddNamed(
                "west",
                instance => instance.UseAwsSns(_Config("WEST-SENDER"), _AwsOptions(RegionEndpoint.EUWest1))
            );
        });
        using var provider = services.BuildServiceProvider();

        // then
        var eastClient = provider.GetRequiredKeyedService<IAmazonSimpleNotificationService>("east");
        var westClient = provider.GetRequiredKeyedService<IAmazonSimpleNotificationService>("west");
        eastClient.Should().NotBeSameAs(westClient);
        eastClient.Config.RegionEndpoint.SystemName.Should().Be("us-east-1");
        westClient.Config.RegionEndpoint.SystemName.Should().Be("eu-west-1");
    }

    [Fact]
    public void should_win_over_ambient_options_when_explicit_aws_options()
    {
        // given - an ambient AWSOptions in DI pointing at us-east-1, and an explicit override at eu-west-1.
        var services = _Services();
        services.AddDefaultAWSOptions(_AwsOptions(RegionEndpoint.USEast1));

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseAwsSns(_Config("DEFAULT-SENDER"), _AwsOptions());
            setup.AddNamed(
                "otp",
                instance => instance.UseAwsSns(_Config("OTP-SENDER"), _AwsOptions(RegionEndpoint.EUWest1))
            );
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredKeyedService<IAmazonSimpleNotificationService>("otp")
            .Config.RegionEndpoint.SystemName.Should()
            .Be("eu-west-1");
    }

    [Fact]
    public void should_fall_back_to_ambient_aws_options_when_named_aws_with_null_options()
    {
        // given - parity with the default path: null options falls back to the ambient AWSOptions in DI.
        var services = _Services();
        services.AddDefaultAWSOptions(_AwsOptions(RegionEndpoint.EUWest1));

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseAwsSns(_Config("DEFAULT-SENDER"), _AwsOptions());
            setup.AddNamed("otp", instance => instance.UseAwsSns(_Config("OTP-SENDER")));
        });
        using var provider = services.BuildServiceProvider();

        // then - the keyed client resolves without throwing and picks up the ambient region.
        var client = provider.GetRequiredKeyedService<IAmazonSimpleNotificationService>("otp");
        client.Config.RegionEndpoint.SystemName.Should().Be("eu-west-1");
        provider.GetRequiredKeyedService<ISmsSender>("otp").Should().BeOfType<AwsSnsSmsSender>();
    }

    [Fact]
    public void should_not_expose_bulk_capability_when_named_aws_sender()
    {
        // given - AWS SNS sends one recipient per API call; the capability probe must be false.
        var services = _Services();

        // when
        services.AddHeadlessSms(setup =>
        {
            setup.UseAwsSns(_Config("DEFAULT-SENDER"), _AwsOptions());
            setup.AddNamed("otp", instance => instance.UseAwsSns(_Config("OTP-SENDER"), _AwsOptions()));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<ISmsSenderProvider>()
            .GetSender("otp")
            .Should()
            .NotBeAssignableTo<IBulkSmsSender>();
        provider.GetKeyedService<IBulkSmsSender>("otp").Should().BeNull();
    }
}
