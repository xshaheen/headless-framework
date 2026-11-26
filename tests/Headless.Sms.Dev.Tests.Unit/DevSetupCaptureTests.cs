// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sms;
using Headless.Sms.Dev;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class DevSetupCaptureTests : TestBase
{
    [Fact]
    public async Task should_share_one_capture_store_between_the_default_and_named_senders()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
        {
            setup.UseDevelopment();
            setup.AddNamed("otp", static instance => instance.UseDevelopment());
        });
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.GetRequiredService<ISmsSender>().SendAsync(SmsRequests.Single(text: "default"), AbortToken);
        await provider
            .GetRequiredKeyedService<IBulkSmsSender>("otp")
            .SendBulkAsync(SmsRequests.Bulk("named", (20, "1")), AbortToken);

        // then
        var store = provider.GetRequiredService<ISmsCaptureStore>();
        store
            .Query()
            .Select(x => (x.SenderName, x.Text, x.IsBulk))
            .Should()
            .Equal(("otp", "named", true), ("", "default", false));
    }

    [Fact]
    public void should_size_the_shared_store_from_the_largest_capacity()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
        {
            setup.UseDevelopment((options, _) => options.CaptureCapacity = 30);
            setup.AddNamed(
                "otp",
                static instance => instance.UseDevelopment(static options => options.CaptureCapacity = 10)
            );
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISmsCaptureStore>().Capacity.Should().Be(30);
    }

    [Fact]
    public void should_bind_options_from_configuration()
    {
        // given
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["CaptureCapacity"] = "9",
                    ["FilePath"] = "sms.txt",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddHeadlessSms(setup =>
        {
            setup.UseDevelopment(config);
            setup.AddNamed("otp", instance => instance.UseDevelopment(config));
            setup.AddNamed(
                "named-delegate",
                static instance => instance.UseDevelopment((options, _) => options.CaptureCapacity = 1)
            );
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptionsMonitor<DevSmsOptions>>().Get("otp").FilePath.Should().Be("sms.txt");
        provider.GetRequiredService<ISmsCaptureStore>().Capacity.Should().Be(9);
    }

    [Fact]
    public void should_reject_a_zero_capacity()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(static setup => setup.UseDevelopment(static options => options.CaptureCapacity = 0));
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ISmsCaptureStore>();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_register_no_capture_store_for_the_noop_sender()
    {
        var services = new ServiceCollection();
        services.AddHeadlessSms(static setup => setup.UseNoop());
        using var provider = services.BuildServiceProvider();

        provider.GetService<ISmsCaptureStore>().Should().BeNull();
    }
}
