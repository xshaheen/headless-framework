// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Cross-provider conformance contract that every real <see cref="IPushNotificationService"/> implementation must
/// satisfy.
/// </summary>
/// <remarks>
/// <para>
/// Concrete provider test classes derive from this, wire the provider's backend double through
/// <see cref="CreateAcceptingService"/> and <see cref="CreateServiceReportingUnregistered"/>, and re-expose each
/// scenario as an xUnit <c>[Fact]</c> override (<c>[Fact] public override Task should_…() =&gt; base.should_…();</c>).
/// Only the portable contract belongs here: input validation before any backend call, per-device outcomes in input
/// order, unregistered-identifier reporting, and cancellation. Provider limits, payload mapping, batching, retries,
/// and transport-fault handling stay in provider-specific test classes, because the interface leaves whole-call
/// transport failure implementation-specific.
/// </para>
/// <para>
/// Both seams must return a backend double that honors the <see cref="CancellationToken"/> it receives and throws
/// <see cref="OperationCanceledException"/> when that token is already cancelled. A service that only forwards the
/// token without inspecting it passes the cancellation scenario only through that double, the same way the real
/// backend client would cancel.
/// </para>
/// <para>
/// The no-op development provider does not belong here: it never validates input and never fails, by design.
/// </para>
/// </remarks>
public abstract class PushNotificationServiceConformanceTests : TestBase
{
    /// <summary>
    /// Creates a service whose backend accepts every client identifier and answers each with a success carrying a
    /// non-empty provider message id.
    /// </summary>
    protected abstract IPushNotificationService CreateAcceptingService();

    /// <summary>
    /// Creates a service whose backend reports <paramref name="unregisteredClientIdentifier"/> as no longer
    /// registered and accepts every other client identifier with a non-empty provider message id.
    /// </summary>
    /// <param name="unregisteredClientIdentifier">The identifier the backend must report as unregistered.</param>
    protected abstract IPushNotificationService CreateServiceReportingUnregistered(string unregisteredClientIdentifier);

    public virtual async Task should_reject_a_null_client_identifier()
    {
        var service = CreateAcceptingService();

        var act = async () => await service.SendToDeviceAsync(null!, PushNotificationRequests.Valid(), AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_reject_an_empty_client_identifier()
    {
        var service = CreateAcceptingService();

        var act = async () => await service.SendToDeviceAsync("", PushNotificationRequests.Valid(), AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_reject_a_whitespace_client_identifier()
    {
        var service = CreateAcceptingService();

        var act = async () => await service.SendToDeviceAsync("   ", PushNotificationRequests.Valid(), AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_reject_a_null_request()
    {
        var service = CreateAcceptingService();

        var act = async () => await service.SendToDeviceAsync("device-1", null!, AbortToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    public virtual async Task should_reject_a_blank_title()
    {
        var service = CreateAcceptingService();
        var request = PushNotificationRequests.Valid(title: "   ");

        var act = async () => await service.SendToDeviceAsync("device-1", request, AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_reject_a_blank_body()
    {
        var service = CreateAcceptingService();
        var request = PushNotificationRequests.Valid(body: "   ");

        var act = async () => await service.SendToDeviceAsync("device-1", request, AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual Task should_reject_a_title_without_a_body()
    {
        return _ShouldReject(new PushNotificationRequest { Title = "Order shipped" });
    }

    public virtual Task should_reject_a_body_without_a_title()
    {
        return _ShouldReject(new PushNotificationRequest { Body = "Your order is on its way." });
    }

    public virtual Task should_reject_a_request_without_title_body_or_data()
    {
        return _ShouldReject(new PushNotificationRequest());
    }

    public virtual Task should_reject_a_data_only_request_with_a_badge()
    {
        return _ShouldReject(PushNotificationRequests.DataOnly() with { Badge = 1 });
    }

    public virtual Task should_reject_a_data_only_request_with_a_sound()
    {
        return _ShouldReject(PushNotificationRequests.DataOnly() with { Sound = "default" });
    }

    public virtual Task should_reject_a_negative_badge()
    {
        return _ShouldReject(PushNotificationRequests.Valid() with { Badge = -1 });
    }

    public virtual Task should_reject_a_negative_time_to_live()
    {
        return _ShouldReject(PushNotificationRequests.Valid() with { TimeToLive = TimeSpan.FromSeconds(-1) });
    }

    public virtual async Task should_reject_a_time_to_live_over_28_days()
    {
        var service = CreateAcceptingService();
        var request = PushNotificationRequests.Valid() with
        {
            TimeToLive = TimeSpan.FromDays(28) + TimeSpan.FromTicks(1),
        };

        var single = async () => await service.SendToDeviceAsync("device-1", request, AbortToken);
        var multicast = async () => await service.SendMulticastAsync(["device-1"], request, AbortToken);

        await single.Should().ThrowAsync<ArgumentOutOfRangeException>().WithMessage("*28 days*");
        await multicast.Should().ThrowAsync<ArgumentOutOfRangeException>().WithMessage("*28 days*");
    }

    public virtual async Task should_accept_a_time_to_live_of_exactly_28_days()
    {
        var service = CreateAcceptingService();
        var request = PushNotificationRequests.Valid() with { TimeToLive = TimeSpan.FromDays(28) };

        var response = await service.SendToDeviceAsync("device-1", request, AbortToken);

        response.IsSucceeded().Should().BeTrue();
    }

    public virtual async Task should_succeed_for_a_data_only_request()
    {
        var service = CreateAcceptingService();

        var response = await service.SendToDeviceAsync("device-1", PushNotificationRequests.DataOnly(), AbortToken);

        response.IsSucceeded().Should().BeTrue();
        response.ClientIdentifier.Should().Be("device-1");
    }

    public virtual async Task should_reject_an_empty_multicast_list()
    {
        var service = CreateAcceptingService();

        var act = async () => await service.SendMulticastAsync([], PushNotificationRequests.Valid(), AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    public virtual async Task should_succeed_for_a_single_device()
    {
        var service = CreateAcceptingService();

        var response = await service.SendToDeviceAsync("device-1", PushNotificationRequests.Valid(), AbortToken);

        response.IsSucceeded().Should().BeTrue();
        response.ClientIdentifier.Should().Be("device-1");
        response.MessageId.Should().NotBeNullOrWhiteSpace();
    }

    public virtual async Task should_succeed_for_every_device_of_a_multicast_in_input_order()
    {
        var service = CreateAcceptingService();
        string[] devices = ["device-1", "device-2", "device-3"];

        var result = await service.SendMulticastAsync(devices, PushNotificationRequests.Valid(), AbortToken);

        result.SuccessCount.Should().Be(3);
        result.FailureCount.Should().Be(0);
        result.Responses.Select(r => r.ClientIdentifier).Should().Equal(devices);
        result.Responses.Should().AllSatisfy(r => r.IsSucceeded().Should().BeTrue());
    }

    public virtual async Task should_report_an_unregistered_device_of_a_multicast_in_input_order()
    {
        var service = CreateServiceReportingUnregistered("device-2");
        string[] devices = ["device-1", "device-2", "device-3"];

        var result = await service.SendMulticastAsync(devices, PushNotificationRequests.Valid(), AbortToken);

        result.SuccessCount.Should().Be(2);
        result.FailureCount.Should().Be(1);
        result.Responses.Select(r => r.ClientIdentifier).Should().Equal(devices);
        result.Responses[0].IsSucceeded().Should().BeTrue();
        result.Responses[1].IsUnregistered().Should().BeTrue();
        result.Responses[2].IsSucceeded().Should().BeTrue();
    }

    public virtual async Task should_propagate_cancellation_of_a_single_send()
    {
        var service = CreateAcceptingService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await service.SendToDeviceAsync("device-1", PushNotificationRequests.Valid(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private async Task _ShouldReject(PushNotificationRequest request)
    {
        var service = CreateAcceptingService();

        var single = async () => await service.SendToDeviceAsync("device-1", request, AbortToken);
        var multicast = async () => await service.SendMulticastAsync(["device-1"], request, AbortToken);

        await single.Should().ThrowAsync<ArgumentException>();
        await multicast.Should().ThrowAsync<ArgumentException>();
    }
}
