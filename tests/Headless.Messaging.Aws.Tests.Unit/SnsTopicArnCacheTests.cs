// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SnsTopicArnCacheTests : TestBase
{
    private readonly IAmazonSimpleNotificationService _sns = Substitute.For<IAmazonSimpleNotificationService>();
    private readonly SnsTopicArnCache _cache = new();

    [Fact]
    public async Task should_create_again_when_a_create_failed_after_every_waiter_cancelled()
    {
        // given: the only waiter gives up before the create fails, so no waiter evicts the failure
        var throttled = new TaskCompletionSource<CreateTopicResponse>();
        _sns.CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                throttled.Task,
                Task.FromResult(new CreateTopicResponse { TopicArn = "arn:aws:sns:::bus-orders" })
            );
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var abandoned = async () => await _cache.GetOrCreateAsync(_sns, "bus-orders", cancelled.Token);
        await abandoned.Should().ThrowAsync<OperationCanceledException>();
        throttled.SetException(new AmazonSimpleNotificationServiceException("Rate exceeded"));

        // when
        var arn = await _cache
            .GetOrCreateAsync(_sns, "bus-orders", AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        arn.Should().Be("arn:aws:sns:::bus-orders");
        await _sns.Received(2).CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_surface_a_create_that_fails_at_once_without_retrying_it()
    {
        // given
        _sns.CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CreateTopicResponse>(new AmazonSimpleNotificationServiceException("denied")));

        // when
        var act = async () =>
            await _cache
                .GetOrCreateAsync(_sns, "bus-orders", AbortToken)
                .WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        await act.Should().ThrowAsync<AmazonSimpleNotificationServiceException>();
        await _sns.Received(1).CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_create_again_after_a_cached_topic_is_forgotten()
    {
        // given: the topic was created and cached, then deleted outside the process
        _sns.CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new CreateTopicResponse { TopicArn = "arn:aws:sns:::bus-orders-1" }),
                Task.FromResult(new CreateTopicResponse { TopicArn = "arn:aws:sns:::bus-orders-2" })
            );
        await _cache.GetOrCreateAsync(_sns, "bus-orders", AbortToken).WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        _cache.Forget("bus-orders");
        var arn = await _cache
            .GetOrCreateAsync(_sns, "bus-orders", AbortToken)
            .WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        arn.Should().Be("arn:aws:sns:::bus-orders-2");
        await _sns.Received(2).CreateTopicAsync(Arg.Any<CreateTopicRequest>(), Arg.Any<CancellationToken>());
    }
}
