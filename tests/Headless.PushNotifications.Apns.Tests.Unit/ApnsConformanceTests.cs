// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="PushNotificationServiceConformanceTests"/> contract against the APNs service
/// talking HTTP/2 to <see cref="FakeApnsServer"/>. APNs-specific behavior (headers, payload limits, status mapping,
/// retries, provider-token expiry) lives in <see cref="ApnsPushNotificationServiceTests"/>.
/// </summary>
public sealed class ApnsConformanceTests : PushNotificationServiceConformanceTests
{
    private readonly List<ServiceProvider> _providers = [];
    private FakeApnsServer _server = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _server = await FakeApnsServer.StartAsync(AbortToken);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        await _server.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    protected override IPushNotificationService CreateAcceptingService()
    {
        _server.Responder = static _ => FakeApnsReply.Ok;

        return _CreateService();
    }

    protected override IPushNotificationService CreateServiceReportingUnregistered(string unregisteredClientIdentifier)
    {
        _server.Responder = r =>
            string.Equals(r.DeviceToken, unregisteredClientIdentifier, StringComparison.Ordinal)
                ? new FakeApnsReply(410, "Unregistered")
                : FakeApnsReply.Ok;

        return _CreateService();
    }

    private IPushNotificationService _CreateService()
    {
        var provider = _server.CreateProvider();
        _providers.Add(provider);

        return provider.GetRequiredService<IPushNotificationService>();
    }

    [Fact]
    public override Task should_reject_a_null_client_identifier()
    {
        return base.should_reject_a_null_client_identifier();
    }

    [Fact]
    public override Task should_reject_an_empty_client_identifier()
    {
        return base.should_reject_an_empty_client_identifier();
    }

    [Fact]
    public override Task should_reject_a_whitespace_client_identifier()
    {
        return base.should_reject_a_whitespace_client_identifier();
    }

    [Fact]
    public override Task should_reject_a_null_request()
    {
        return base.should_reject_a_null_request();
    }

    [Fact]
    public override Task should_reject_a_blank_title()
    {
        return base.should_reject_a_blank_title();
    }

    [Fact]
    public override Task should_reject_a_blank_body()
    {
        return base.should_reject_a_blank_body();
    }

    [Fact]
    public override Task should_reject_a_title_without_a_body()
    {
        return base.should_reject_a_title_without_a_body();
    }

    [Fact]
    public override Task should_reject_a_body_without_a_title()
    {
        return base.should_reject_a_body_without_a_title();
    }

    [Fact]
    public override Task should_reject_a_request_without_title_body_or_data()
    {
        return base.should_reject_a_request_without_title_body_or_data();
    }

    [Fact]
    public override Task should_reject_a_data_only_request_with_a_badge()
    {
        return base.should_reject_a_data_only_request_with_a_badge();
    }

    [Fact]
    public override Task should_reject_a_data_only_request_with_a_sound()
    {
        return base.should_reject_a_data_only_request_with_a_sound();
    }

    [Fact]
    public override Task should_reject_a_negative_badge()
    {
        return base.should_reject_a_negative_badge();
    }

    [Fact]
    public override Task should_reject_a_negative_time_to_live()
    {
        return base.should_reject_a_negative_time_to_live();
    }

    [Fact]
    public override Task should_succeed_for_a_data_only_request()
    {
        return base.should_succeed_for_a_data_only_request();
    }

    [Fact]
    public override Task should_reject_an_empty_multicast_list()
    {
        return base.should_reject_an_empty_multicast_list();
    }

    [Fact]
    public override Task should_succeed_for_a_single_device()
    {
        return base.should_succeed_for_a_single_device();
    }

    [Fact]
    public override Task should_succeed_for_every_device_of_a_multicast_in_input_order()
    {
        return base.should_succeed_for_every_device_of_a_multicast_in_input_order();
    }

    [Fact]
    public override Task should_report_an_unregistered_device_of_a_multicast_in_input_order()
    {
        return base.should_report_an_unregistered_device_of_a_multicast_in_input_order();
    }

    [Fact]
    public override Task should_propagate_cancellation_of_a_single_send()
    {
        return base.should_propagate_cancellation_of_a_single_send();
    }
}
