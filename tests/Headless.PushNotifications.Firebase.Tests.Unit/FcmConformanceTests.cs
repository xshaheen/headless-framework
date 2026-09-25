// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internals;

namespace Tests;

/// <summary>
/// Runs the cross-provider <see cref="PushNotificationServiceConformanceTests"/> contract against the Firebase
/// service. Firebase-specific behavior (payload limits, reserved data keys, field mapping, 500-FID batching)
/// lives in <see cref="FcmPushNotificationServiceTests"/>.
/// </summary>
public sealed class FcmConformanceTests : PushNotificationServiceConformanceTests
{
    protected override IPushNotificationService CreateAcceptingService()
    {
        return _CreateService(unregisteredFid: null);
    }

    protected override IPushNotificationService CreateServiceReportingUnregistered(string unregisteredClientIdentifier)
    {
        return _CreateService(unregisteredClientIdentifier);
    }

    private static FcmPushNotificationService _CreateService(string? unregisteredFid)
    {
        var sender = Substitute.For<IFcmMessageSender>();

        // The service forwards the token without inspecting it, so the double stands in for the Firebase client and
        // cancels the way that client would.
        sender
            .SendAsync(Arg.Any<FcmMessageContent>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                ci.ArgAt<CancellationToken>(2).ThrowIfCancellationRequested();

                return Task.FromResult(_Outcome(ci.ArgAt<string>(1), unregisteredFid));
            });

        sender
            .SendBatchAsync(
                Arg.Any<FcmMessageContent>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                ci.ArgAt<CancellationToken>(2).ThrowIfCancellationRequested();
                IReadOnlyList<PushNotificationResponse> outcomes =
                [
                    .. ci.ArgAt<IReadOnlyList<string>>(1).Select(fid => _Outcome(fid, unregisteredFid)),
                ];

                return Task.FromResult(outcomes);
            });

        return new FcmPushNotificationService(sender);
    }

    private static PushNotificationResponse _Outcome(string fid, string? unregisteredFid)
    {
        return string.Equals(fid, unregisteredFid, StringComparison.Ordinal)
            ? PushNotificationResponse.Unregistered(fid)
            : PushNotificationResponse.Succeeded(fid, $"projects/test/messages/{fid}");
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
