// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.PushNotifications;
using Headless.PushNotifications.Firebase;
using Headless.PushNotifications.Firebase.Internals;
using Headless.Testing.Tests;

namespace Tests;

public sealed class FcmPushNotificationServiceTests : TestBase
{
    private readonly IFcmMessageSender _sender = Substitute.For<IFcmMessageSender>();

    private FcmPushNotificationService _CreateService() => new(_sender);

    [Fact]
    public async Task should_return_sender_outcome_for_single_send()
    {
        // given
        _sender
            .SendAsync(Arg.Any<FcmMessageContent>(), "token", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PushNotificationResponse.Succeeded("token", "msg-1")));

        // when
        var result = await _CreateService().SendToDeviceAsync("token", "title", "body", cancellationToken: AbortToken);
        // then
        result.IsSucceeded().Should().BeTrue();
        result.MessageId.Should().Be("msg-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_throw_when_token_is_blank(string token)
    {
        // when
        var action = async () =>
            await _CreateService().SendToDeviceAsync(token, "title", "body", cancellationToken: AbortToken);
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
        var action = async () => await _CreateService().SendToDeviceAsync("token", "title", "body", data, AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_when_title_exceeds_max_length()
    {
        // when
        var action = async () =>
            await _CreateService()
                .SendToDeviceAsync("token", new string('a', 101), "body", cancellationToken: AbortToken);
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
        var action = async () =>
            await _CreateService().SendToDeviceAsync("token", "title", "body", cancellationToken: AbortToken);
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
                var tokens = ci.Arg<IReadOnlyList<string>>();
                IReadOnlyList<PushNotificationResponse> outcomes =
                [
                    .. tokens.Select(t => PushNotificationResponse.Succeeded(t, "id")),
                ];
                return Task.FromResult(outcomes);
            });
        var tokens = Enumerable.Range(0, 501).Select(i => $"t{i}").ToList();

        // when
        var result = await _CreateService().SendMulticastAsync(tokens, "title", "body", cancellationToken: AbortToken);
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
                var tokens = ci.Arg<IReadOnlyList<string>>();
                var succeed = Interlocked.Increment(ref call) == 1;
                IReadOnlyList<PushNotificationResponse> outcomes =
                [
                    .. tokens.Select(t =>
                        succeed
                            ? PushNotificationResponse.Succeeded(t, "id")
                            : PushNotificationResponse.Failed(t, "boom")
                    ),
                ];
                return Task.FromResult(outcomes);
            });
        var tokens = Enumerable.Range(0, 501).Select(i => $"t{i}").ToList();

        // when
        var result = await _CreateService().SendMulticastAsync(tokens, "title", "body", cancellationToken: AbortToken);
        // then
        result.Responses.Should().HaveCount(501);
        result.SuccessCount.Should().Be(500);
        result.FailureCount.Should().Be(1);
    }

    [Fact]
    public async Task should_throw_when_multicast_tokens_empty()
    {
        // when
        var action = async () =>
            await _CreateService().SendMulticastAsync([], "title", "body", cancellationToken: AbortToken);
        // then
        await action.Should().ThrowAsync<ArgumentException>();
    }
}
