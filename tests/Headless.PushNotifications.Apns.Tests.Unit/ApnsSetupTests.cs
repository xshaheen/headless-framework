// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class ApnsSetupTests : TestBase
{
    private FakeApnsServer _server = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _server = await FakeApnsServer.StartAsync(AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await _server.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_resolve_default_service_bound_to_configuration_when_using_configuration_overload()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Apns:KeyId"] = FakeApnsServer.KeyId,
                    ["Apns:TeamId"] = FakeApnsServer.TeamId,
                    ["Apns:PrivateKey"] = _server.PrivateKeyPem,
                    ["Apns:BundleId"] = "com.example.configured",
                    ["Apns:Priority"] = "PowerPrioritized",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
            setup.UseApns(configuration.GetSection("Apns"), configureClient: c => c.BaseAddress = _server.BaseAddress)
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var service = provider.GetRequiredService<IPushNotificationService>();
        var response = await service.SendToDeviceAsync("device-1", PushNotificationRequests.Valid(), AbortToken);

        // then
        service.Should().BeOfType<ApnsPushNotificationService>();
        response.IsSucceeded().Should().BeTrue();
        var request = _server.Requests.Should().ContainSingle().Subject;
        request.Headers["apns-topic"].Should().Be("com.example.configured");
        request.Headers["apns-priority"].Should().Be("1");
    }

    [Fact]
    public async Task should_bind_each_named_instance_to_its_own_options()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.AddNamed(
                "a",
                i => i.UseApns(o => _Configure(o, "com.example.a"), c => c.BaseAddress = _server.BaseAddress)
            );
            setup.AddNamed(
                "b",
                i => i.UseApns(o => _Configure(o, "com.example.b"), c => c.BaseAddress = _server.BaseAddress)
            );
        });
        await using var provider = services.BuildServiceProvider();

        // when
        var a = provider.GetRequiredKeyedService<IPushNotificationService>("a");
        var b = provider.GetRequiredKeyedService<IPushNotificationService>("b");
        await a.SendToDeviceAsync("device-a", PushNotificationRequests.Valid(), AbortToken);
        await b.SendToDeviceAsync("device-b", PushNotificationRequests.Valid(), AbortToken);

        // then
        provider.GetService<IPushNotificationService>().Should().BeNull();
        provider.GetRequiredService<IPushNotificationServiceProvider>().GetService("a").Should().BeSameAs(a);
        _server.Requests.Single(r => r.DeviceToken == "device-a").Headers["apns-topic"].Should().Be("com.example.a");
        _server.Requests.Single(r => r.DeviceToken == "device-b").Headers["apns-topic"].Should().Be("com.example.b");
    }

    [Fact]
    public async Task should_share_one_provider_token_when_default_and_named_instances_use_the_same_key()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseApns(o => _Configure(o, "com.example.app"), c => c.BaseAddress = _server.BaseAddress);
            setup.AddNamed(
                "calls",
                i =>
                    i.UseApns(
                        o =>
                        {
                            _Configure(o, "com.example.app");
                            o.PushType = ApnsPushType.Voip;
                        },
                        c => c.BaseAddress = _server.BaseAddress
                    )
            );
        });
        await using var provider = services.BuildServiceProvider();

        // when
        await provider
            .GetRequiredService<IPushNotificationService>()
            .SendToDeviceAsync("device-1", PushNotificationRequests.Valid(), AbortToken);
        await provider
            .GetRequiredKeyedService<IPushNotificationService>("calls")
            .SendToDeviceAsync("device-2", PushNotificationRequests.Valid(), AbortToken);

        // then
        services.Count(d => d.ServiceType == typeof(ApnsTokenSource)).Should().Be(1);
        _server.Requests.Should().HaveCount(2);
        _server.DistinctBearers().Should().ContainSingle();
        _server.Requests.Select(r => r.Headers["apns-topic"]).Should().Equal("com.example.app", "com.example.app.voip");
    }

    [Theory]
    [InlineData(ApnsEnvironment.Production, "https://api.push.apple.com/")]
    [InlineData(ApnsEnvironment.Sandbox, "https://api.sandbox.push.apple.com/")]
    public async Task should_target_the_environment_host_when_no_client_override_is_given(
        ApnsEnvironment environment,
        string expected
    )
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseApns(o =>
            {
                _Configure(o, "com.example.app");
                o.Environment = environment;
            });
            setup.AddNamed(
                "named",
                i =>
                    i.UseApns(o =>
                    {
                        _Configure(o, "com.example.app");
                        o.Environment = environment;
                    })
            );
        });
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // when
        using var defaultClient = factory.CreateClient("Headless:Apns");
        using var namedClient = factory.CreateClient("Headless:Apns:named");

        // then
        defaultClient.BaseAddress.Should().Be(new Uri(expected));
        namedClient.BaseAddress.Should().Be(new Uri(expected));
    }

    [Fact]
    public async Task should_copy_every_option_when_using_a_prebuilt_options_instance()
    {
        // given
        var options = new ApnsOptions
        {
            KeyId = FakeApnsServer.KeyId,
            TeamId = FakeApnsServer.TeamId,
            PrivateKey = _server.PrivateKeyPem,
            BundleId = "com.example.prebuilt",
            Environment = ApnsEnvironment.Sandbox,
            PushType = ApnsPushType.Voip,
            Priority = ApnsPriority.PowerConsiderate,
            TreatBadDeviceTokenAsUnregistered = true,
            MaxConcurrency = 7,
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessPushNotifications(setup =>
        {
            setup.UseApns(options);
            setup.AddNamed("named", i => i.UseApns(options));
        });
        await using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<ApnsOptions>>();

        // when
        var resolved = new[] { monitor.Get(Options.DefaultName), monitor.Get("named") };

        // then
        resolved.Should().AllSatisfy(r => r.Should().BeEquivalentTo(options));
    }

    [Fact]
    public async Task should_fail_host_start_when_options_are_missing()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessPushNotifications(setup => setup.UseApns(static _ => { }));
        using var host = builder.Build();

        // when
        var act = async () => await host.StartAsync(AbortToken);

        // then
        var exception = await act.Should().ThrowAsync<OptionsValidationException>();
        exception.Which.Failures.Should().Contain(f => f.Contains("KeyId", StringComparison.Ordinal));
    }

    private void _Configure(ApnsOptions options, string bundleId)
    {
        _server.ConfigureOptions(options);
        options.BundleId = bundleId;
    }
}
