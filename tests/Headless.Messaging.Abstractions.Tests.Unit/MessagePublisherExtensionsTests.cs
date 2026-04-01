// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed class MessagePublisherExtensionsTests
{
    [Fact]
    public async Task publish_async_should_forward_payload_and_cancellation_token_with_null_options()
    {
        // given
        var publisher = new RecordingPublisher();
        var message = new TestMessage("msg-1");
        using var cts = new CancellationTokenSource();

        // when
        await publisher.PublishAsync(message, cts.Token);

        // then
        publisher.LastMessage.Should().BeSameAs(message);
        publisher.LastOptions.Should().BeNull();
        publisher.LastCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task publish_async_should_throw_when_publisher_is_null()
    {
        // given
        IMessagePublisher publisher = null!;

        // when
        var act = async () => await publisher.PublishAsync(new TestMessage("msg-1"), CancellationToken.None);

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("publisher");
    }

    [Fact]
    public async Task publish_delay_async_should_forward_delay_payload_and_cancellation_token_with_null_options()
    {
        // given
        var publisher = new RecordingScheduledPublisher();
        var message = new TestMessage("msg-2");
        var delay = TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource();

        // when
        await publisher.PublishDelayAsync(delay, message, cts.Token);

        // then
        publisher.LastDelay.Should().Be(delay);
        publisher.LastMessage.Should().BeSameAs(message);
        publisher.LastOptions.Should().BeNull();
        publisher.LastCancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task publish_delay_async_should_throw_when_publisher_is_null()
    {
        // given
        IScheduledPublisher publisher = null!;

        // when
        var act = async () =>
            await publisher.PublishDelayAsync(
                TimeSpan.FromSeconds(1),
                new TestMessage("msg-1"),
                CancellationToken.None
            );

        // then
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("publisher");
    }

    private sealed record TestMessage(string Id);

    private sealed class RecordingPublisher : IMessagePublisher
    {
        public object? LastMessage { get; private set; }

        public PublishOptions? LastOptions { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task PublishAsync<T>(
            T? contentObj,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            LastMessage = contentObj;
            LastOptions = options;
            LastCancellationToken = cancellationToken;

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingScheduledPublisher : IScheduledPublisher
    {
        public TimeSpan LastDelay { get; private set; }

        public object? LastMessage { get; private set; }

        public PublishOptions? LastOptions { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task PublishDelayAsync<T>(
            TimeSpan delayTime,
            T? contentObj,
            PublishOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            LastDelay = delayTime;
            LastMessage = contentObj;
            LastOptions = options;
            LastCancellationToken = cancellationToken;

            return Task.CompletedTask;
        }
    }
}
