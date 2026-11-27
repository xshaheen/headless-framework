// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Context;
using Headless.PushNotifications;
using Headless.PushNotifications.Dev;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests;

public sealed class DevPushNotificationServiceTests : TestBase
{
    private readonly PushNotificationCaptureStore _store = new(500, new FakeTimeProvider());

    [Fact]
    public async Task should_capture_a_single_device_notification_with_every_option()
    {
        // given
        var service = new DevPushNotificationService(_store, "ios");
        var request = new PushNotificationRequest
        {
            Title = "Order shipped",
            Body = "Your order is on its way",
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["orderId"] = "42" },
            CollapseKey = "orders",
            Badge = 3,
            Sound = "default",
            Priority = PushNotificationPriority.High,
            TimeToLive = TimeSpan.FromHours(1),
        };

        // when
        var response = await service.SendToDeviceAsync("device-1", request, AbortToken);

        // then
        var captured = _store.Query(new()).Should().ContainSingle().Subject;
        response.IsSucceeded().Should().BeTrue();
        response.ClientIdentifier.Should().Be("device-1");
        response.MessageId.Should().Be(captured.Id.ToString());
        captured
            .Should()
            .BeEquivalentTo(
                new
                {
                    SenderName = "ios",
                    ClientIdentifiers = new[] { "device-1" },
                    IsMulticast = false,
                    Title = "Order shipped",
                    Body = "Your order is on its way",
                    IsDataOnly = false,
                    CollapseKey = "orders",
                    Badge = 3,
                    Sound = "default",
                    Priority = PushNotificationPriority.High,
                    TimeToLive = TimeSpan.FromHours(1),
                    FailureError = (string?)null,
                }
            );
        captured.Data.Should().ContainKey("orderId").WhoseValue.Should().Be("42");
        captured.PayloadSizeBytes.Should().BePositive();
        captured.ExceedsPayloadLimit.Should().BeFalse();
        captured.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_capture_a_multicast_once_and_answer_every_identifier()
    {
        // given
        var service = new DevPushNotificationService(_store);
        var request = new PushNotificationRequest
        {
            Data = new Dictionary<string, string>(StringComparer.Ordinal) { ["sync"] = "1" },
        };

        // when
        var response = await service.SendMulticastAsync(["a", "b"], request, AbortToken);

        // then
        response.SuccessCount.Should().Be(2);
        response.FailureCount.Should().Be(0);
        response.Responses.Select(r => r.ClientIdentifier).Should().Equal("a", "b");
        var captured = _store.Query(new()).Should().ContainSingle().Subject;
        captured.IsMulticast.Should().BeTrue();
        captured.IsDataOnly.Should().BeTrue();
        captured.ClientIdentifiers.Should().Equal("a", "b");
    }

    [Fact]
    public async Task should_flag_a_payload_estimated_over_4096_bytes_and_still_succeed()
    {
        // given
        var service = new DevPushNotificationService(_store);
        var request = new PushNotificationRequest { Title = "Big", Body = new string('x', 5000) };

        // when
        var response = await service.SendMulticastAsync(["a", "b"], request, AbortToken);

        // then
        response.SuccessCount.Should().Be(2);
        var captured = _store.Query(new()).Single();
        captured.ExceedsPayloadLimit.Should().BeTrue();
        captured.PayloadSizeBytes.Should().BeGreaterThan(CapturedPushNotification.MaxPayloadBytes);
        captured.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_an_empty_multicast_without_capturing_it()
    {
        var service = new DevPushNotificationService(_store);

        var act = async () =>
            await service.SendMulticastAsync([], new PushNotificationRequest { Title = "t", Body = "b" }, AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
        _store.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_measure_non_ascii_text_as_utf8_bytes()
    {
        var service = new DevPushNotificationService(_store);

        await service.SendToDeviceAsync("a", new PushNotificationRequest { Title = "ط", Body = "ب" }, AbortToken);

        // {"notification":{"title":"ط","body":"ب"}} is 39 ASCII bytes plus two 2-byte letters.
        _store.Query(new()).Single().PayloadSizeBytes.Should().Be(43);
    }

    [Fact]
    public async Task should_capture_a_malformed_request_as_failed_and_still_throw()
    {
        // given: a title without a body is neither a notification nor a data-only message
        var service = new DevPushNotificationService(_store);

        // when
        var act = async () =>
            await service.SendToDeviceAsync("a", new PushNotificationRequest { Title = "t" }, AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        _store.Query(new()).Should().ContainSingle().Which.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_a_blank_client_identifier()
    {
        var service = new DevPushNotificationService(_store);

        var act = async () =>
            await service.SendMulticastAsync(
                ["a", " "],
                new PushNotificationRequest { Title = "t", Body = "b" },
                AbortToken
            );

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_keep_the_capture_unchanged_when_the_caller_mutates_its_request()
    {
        // given
        var service = new DevPushNotificationService(_store);
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "1" };
        var ids = new List<string> { "device-1" };

        // when
        await service.SendMulticastAsync(ids, new PushNotificationRequest { Data = data }, AbortToken);
        data["b"] = "2";
        ids.Add("device-2");

        // then
        var captured = _store.Query(new()).Single();
        captured.Data.Should().ContainSingle();
        captured.ClientIdentifiers.Should().ContainSingle();
    }

    [Fact]
    public async Task should_record_the_current_trace_id_and_correlation_id()
    {
        // given
        var correlation = Substitute.For<ICorrelationIdProvider>();
        correlation.CorrelationId.Returns("corr-1");
        var service = new DevPushNotificationService(_store, correlationIdProvider: correlation);
        using var activity = new Activity("send");
        activity.SetIdFormat(ActivityIdFormat.W3C).Start();

        // when
        await service.SendToDeviceAsync("a", new PushNotificationRequest { Title = "t", Body = "b" }, AbortToken);

        // then
        var captured = _store.Query(new()).Single();
        captured.TraceId.Should().Be(activity.TraceId.ToString());
        captured.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task should_honor_cancellation()
    {
        var service = new DevPushNotificationService(_store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () =>
            await service.SendToDeviceAsync("a", new PushNotificationRequest { Title = "t", Body = "b" }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Count.Should().Be(0);
    }
}
