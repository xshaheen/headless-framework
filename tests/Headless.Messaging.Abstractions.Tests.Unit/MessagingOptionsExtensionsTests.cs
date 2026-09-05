// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MessagingOptionsExtensionsTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_configure_once_forward_metadata_and_detach_retained_builder(bool publish)
    {
        var bus = Substitute.For<IBus>();
        var queue = Substitute.For<IQueue>();
        var calls = 0;
        Action mutateRetainedBuilder = () => { };
        using var caller = new CancellationTokenSource();

        if (publish)
        {
            await bus.PublishAsync(
                "message",
                options =>
                {
                    ++calls;
                    options
                        .WithCorrelationId("corr")
                        .WithCausationId("cause")
                        .WithMessageId("id")
                        .WithTenantId("tenant")
                        .WithDelay(TimeSpan.FromMinutes(1))
                        .WithHeader("source", "checkout");
                    mutateRetainedBuilder = () => options.WithHeader("source", "changed").WithTenantId("changed");
                },
                caller.Token
            );
        }
        else
        {
            await queue.EnqueueAsync(
                "message",
                options =>
                {
                    ++calls;
                    options
                        .WithCorrelationId("corr")
                        .WithCausationId("cause")
                        .WithMessageId("id")
                        .WithTenantId("tenant")
                        .WithDelay(TimeSpan.FromMinutes(1))
                        .WithHeader("source", "checkout");
                    mutateRetainedBuilder = () => options.WithHeader("source", "changed").WithTenantId("changed");
                },
                caller.Token
            );
        }

        mutateRetainedBuilder();
        calls.Should().Be(1);
        var arguments = (publish ? bus.ReceivedCalls() : queue.ReceivedCalls())
            .Should()
            .ContainSingle()
            .Subject.GetArguments();
        arguments[0].Should().Be("message");
        arguments[2].Should().Be(caller.Token);
        var optionsSnapshot = arguments[1].Should().BeAssignableTo<MessageOptions>().Subject;
        optionsSnapshot.CorrelationId.Should().Be("corr");
        optionsSnapshot.CausationId.Should().Be("cause");
        optionsSnapshot.MessageId.Should().Be("id");
        optionsSnapshot.TenantId.Should().Be("tenant");
        optionsSnapshot.Delay.Should().Be(TimeSpan.FromMinutes(1));
        optionsSnapshot.Headers!["source"].Should().Be("checkout");
        optionsSnapshot.DeliveryMode.Should().Be(DeliveryMode.Durable);
        (publish ? queue.ReceivedCalls() : bus.ReceivedCalls()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task should_return_original_faulted_or_canceled_task_without_precanceling_callback(
        bool publish,
        bool cancel
    )
    {
        var bus = Substitute.For<IBus>();
        var queue = Substitute.For<IQueue>();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        var failure = new InvalidOperationException("publisher failed");
        var original = cancel ? Task.FromCanceled(caller.Token) : Task.FromException(failure);
        bus.PublishAsync("message", Arg.Any<PublishOptions>(), caller.Token).Returns(original);
        queue.EnqueueAsync("message", Arg.Any<QueueOptions>(), caller.Token).Returns(original);
        var calls = 0;

        var result = publish
            ? bus.PublishAsync(
                "message",
                _ =>
                {
                    ++calls;
                },
                caller.Token
            )
            : queue.EnqueueAsync(
                "message",
                _ =>
                {
                    ++calls;
                },
                caller.Token
            );

        calls.Should().Be(1);
        result.Should().BeSameAs(original);
        Func<Task> observe = () => result;
        if (cancel)
        {
            await observe.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            (await observe.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        }
    }

    [Fact]
    public void should_guard_null_receivers_and_callbacks_before_user_code_or_submission()
    {
        var bus = Substitute.For<IBus>();
        var queue = Substitute.For<IQueue>();
        IBus nullBus = null!;
        IQueue nullQueue = null!;
        var callbacks = 0;
        Action missingBus = () =>
            nullBus.PublishAsync(
                "message",
                _ =>
                {
                    ++callbacks;
                },
                AbortToken
            );
        Action missingQueue = () =>
            nullQueue.EnqueueAsync(
                "message",
                _ =>
                {
                    ++callbacks;
                },
                AbortToken
            );
        Action missingPublishCallback = () => bus.PublishAsync("message", configure: null!, AbortToken);
        Action missingQueueCallback = () => queue.EnqueueAsync("message", configure: null!, AbortToken);

        missingBus.Should().Throw<ArgumentNullException>().WithParameterName("bus");
        missingQueue.Should().Throw<ArgumentNullException>().WithParameterName("queue");
        missingPublishCallback.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        missingQueueCallback.Should().Throw<ArgumentNullException>().WithParameterName("configure");
        callbacks.Should().Be(0);
        bus.ReceivedCalls().Should().BeEmpty();
        queue.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_propagate_callback_or_enumeration_failure_without_submission(bool failEnumeration)
    {
        var bus = Substitute.For<IBus>();
        var queue = Substitute.For<IQueue>();
        var failure = new InvalidOperationException("configuration failed");
        IEnumerable<KeyValuePair<string, string?>> headers()
        {
            yield return new("source", "checkout");
            throw failure;
        }
        Action publish = () =>
            bus.PublishAsync(
                "message",
                options =>
                {
                    if (failEnumeration)
                    {
                        options.WithHeaders(headers());
                    }
                    throw failure;
                },
                AbortToken
            );
        Action enqueue = () =>
            queue.EnqueueAsync(
                "message",
                options =>
                {
                    if (failEnumeration)
                    {
                        options.WithHeaders(headers());
                    }
                    throw failure;
                },
                AbortToken
            );

        publish.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        enqueue.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
        bus.ReceivedCalls().Should().BeEmpty();
        queue.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_accept_typed_null_payloads_and_use_fresh_empty_builders()
    {
        var bus = Substitute.For<IBus>();
        var queue = Substitute.For<IQueue>();
        await bus.PublishAsync<string>(null, options => options.WithTenantId("tenant"), AbortToken);
        await queue.EnqueueAsync<string>(null, options => options.WithTenantId("tenant"), AbortToken);

        await bus.PublishAsync<string>(null, _ => { }, AbortToken);
        await queue.EnqueueAsync<string>(null, _ => { }, AbortToken);

        await bus.Received(1)
            .PublishAsync<string>(null, Arg.Is<PublishOptions>(options => options == new PublishOptions()), AbortToken);
        await queue
            .Received(1)
            .EnqueueAsync<string>(null, Arg.Is<QueueOptions>(options => options == new QueueOptions()), AbortToken);
    }
}
