// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internals;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FcmPushNotificationServiceTests : TestBase
{
    private readonly IFcmMessageSender _sender = Substitute.For<IFcmMessageSender>();

    [Fact]
    public void should_target_fid_for_single_send()
    {
        // when
        var message = FcmMessageSender.BuildMessage(new FcmMessageContent("title", "body", null), "fid-1");

        // then
        message.Fid.Should().Be("fid-1");
    }

    [Fact]
    public void should_target_fids_for_multicast_send()
    {
        // when
        var message = FcmMessageSender.BuildMulticastMessage(
            new FcmMessageContent("title", "body", null),
            ["fid-1", "fid-2"]
        );

        // then
        message.Fids.Should().Equal("fid-1", "fid-2");
    }

    [Fact]
    public void should_set_collapse_key_for_android_and_apns_on_single_send()
    {
        // when
        var message = FcmMessageSender.BuildMessage(
            new FcmMessageContent("title", "body", null, CollapseKey: "order-42"),
            "fid-1"
        );

        // then
        message.Android.CollapseKey.Should().Be("order-42");
        message.Apns.Headers.Should().Contain("apns-collapse-id", "order-42");
    }

    [Fact]
    public void should_set_collapse_key_for_android_and_apns_on_multicast_send()
    {
        // when
        var message = FcmMessageSender.BuildMulticastMessage(
            new FcmMessageContent("title", "body", null, CollapseKey: "order-42"),
            ["fid-1", "fid-2"]
        );

        // then
        message.Android.CollapseKey.Should().Be("order-42");
        message.Apns.Headers.Should().Contain("apns-collapse-id", "order-42");
    }

    [Fact]
    public void should_leave_collapse_key_unset_when_request_has_none()
    {
        // when
        var single = FcmMessageSender.BuildMessage(new FcmMessageContent("title", "body", null), "fid-1");
        var multicast = FcmMessageSender.BuildMulticastMessage(new FcmMessageContent("title", "body", null), ["fid-1"]);

        // then
        single.Android.CollapseKey.Should().BeNull();
        single.Apns.Headers.Should().BeNull();
        multicast.Android.CollapseKey.Should().BeNull();
        multicast.Apns.Headers.Should().BeNull();
    }

    [Fact]
    public async Task should_throw_when_collapse_key_exceeds_64_utf8_bytes()
    {
        // given 33 two-byte characters = 66 UTF-8 bytes, though only 33 chars
        var request = _Request() with
        {
            CollapseKey = new string('é', 33),
        };

        // when
        var action = async () => await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        await action.Should().ThrowAsync<ArgumentException>();
        await _sender
            .DidNotReceive()
            .SendAsync(Arg.Any<FcmMessageContent>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_pass_collapse_key_to_sender()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), "fid", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PushNotificationResponse.Succeeded("fid", "msg-1")));
        var request = _Request() with { CollapseKey = "order-42" };

        // when
        await _CreateService().SendToDeviceAsync("fid", request, AbortToken);

        // then
        await _sender
            .Received(1)
            .SendAsync(
                Arg.Is<FcmMessageContent>(c => c.CollapseKey == "order-42"),
                "fid",
                Arg.Any<CancellationToken>()
            );
    }

    private FcmPushNotificationService _CreateService()
    {
        return new(_sender);
    }

    private static PushNotificationRequest _Request(
        string title = "title",
        string body = "body",
        IReadOnlyDictionary<string, string>? data = null
    )
    {
        return new()
        {
            Title = title,
            Body = body,
            Data = data,
        };
    }

    [Fact]
    public async Task should_return_sender_outcome_for_single_send()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), "fid", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PushNotificationResponse.Succeeded("fid", "msg-1")));

        // when
        var result = await _CreateService().SendToDeviceAsync("fid", _Request(), AbortToken);
        // then
        result.IsSucceeded().Should().BeTrue();
        result.MessageId.Should().Be("msg-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_throw_when_fid_is_blank(string fid)
    {
        // when
        var action = async () => await _CreateService().SendToDeviceAsync(fid, _Request(), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("from")]
    [InlineData("notification")]
    [InlineData("message_type")]
    [InlineData("google.x")]
    [InlineData("gcm.y")]
    public async Task should_throw_when_data_contains_reserved_key(string reservedKey)
    {
        // given
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { [reservedKey] = "value" };

        // when
        var action = async () => await _CreateService().SendToDeviceAsync("fid", _Request(data: data), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_title_exceeds_max_length()
    {
        // when
        var action = async () =>
            await _CreateService().SendToDeviceAsync("fid", _Request(title: new string('a', 101)), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_propagate_cancellation_for_single_send()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PushNotificationResponse>(new OperationCanceledException()));

        // when
        var action = async () => await _CreateService().SendToDeviceAsync("fid", _Request(), AbortToken);
        // then
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task should_split_multicast_into_batches_of_500()
    {
        // given
        _sender
            .SendBatchAsync(
                Arg.Any<FcmMessageContent>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                var fids = ci.Arg<IReadOnlyList<string>>();
                IReadOnlyList<PushNotificationResponse> outcomes =
                [
                    .. fids.Select(fid => PushNotificationResponse.Succeeded(fid, "id")),
                ];
                return Task.FromResult(outcomes);
            });
        var fids = Enumerable.Range(0, 501).Select(i => $"fid-{i}").ToList();

        // when
        var result = await _CreateService().SendMulticastAsync(fids, _Request(), AbortToken);
        // then
        result.SuccessCount.Should().Be(501);
        result.FailureCount.Should().Be(0);
        result.Responses.Should().HaveCount(501);
        await _sender
            .Received(2)
            .SendBatchAsync(
                Arg.Any<FcmMessageContent>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_keep_accumulated_results_when_a_later_batch_fails()
    {
        // given
        var call = 0;
        _sender
            .SendBatchAsync(
                Arg.Any<FcmMessageContent>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                var fids = ci.Arg<IReadOnlyList<string>>();
                var succeed = Interlocked.Increment(ref call) == 1;
                IReadOnlyList<PushNotificationResponse> outcomes =
                [
                    .. fids.Select(fid =>
                        succeed
                            ? PushNotificationResponse.Succeeded(fid, "id")
                            : PushNotificationResponse.Failed(fid, "boom")
                    ),
                ];
                return Task.FromResult(outcomes);
            });
        var fids = Enumerable.Range(0, 501).Select(i => $"fid-{i}").ToList();

        // when
        var result = await _CreateService().SendMulticastAsync(fids, _Request(), AbortToken);
        // then
        result.Responses.Should().HaveCount(501);
        result.SuccessCount.Should().Be(500);
        result.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_when_multicast_fids_empty()
    {
        // when
        var action = async () => await _CreateService().SendMulticastAsync([], _Request(), AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }
}
