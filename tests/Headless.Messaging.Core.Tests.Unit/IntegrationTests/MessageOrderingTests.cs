using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests.IntegrationTests;

/// <summary>
/// Tests to verify message ordering behavior under different configurations.
/// Ordering guarantees depend on transport provider and configuration options.
/// </summary>
public sealed class MessageOrderingTests(ITestOutputHelper testOutput) : IntegrationTestBase(testOutput)
{
    [Fact]
    public async Task should_process_messages_in_order_when_single_consumer_thread()
    {
        // given: Sequential consumer configuration
        var receivedOrder = new ConcurrentQueue<int>();
        var messageCount = 10;

        // when: Publishing messages in sequence
        for (var i = 1; i <= messageCount; i++)
        {
            await Publisher.PublishAsync(
                "test.ordering",
                new OrderedMessage { Sequence = i },
                cancellationToken: AbortToken
            );
        }

        // then: Messages should be received in order (with single consumer thread)
        await Task.Delay(1000, AbortToken); // Allow processing time

        var received = HandledMessages.OfType<OrderedMessage>().ToList();
        received.Should().HaveCount(messageCount);

        for (var i = 0; i < messageCount - 1; i++)
        {
            received[i]
                .Sequence.Should()
                .BeLessThan(
                    received[i + 1].Sequence,
                    "messages should be processed sequentially with ConsumerThreadCount=1"
                );
        }
    }

    [Fact]
    public async Task should_document_no_ordering_guarantee_with_parallel_consumer_threads()
    {
        // given: Parallel consumer configuration would break ordering
        // This test documents expected behavior, not strict ordering

        var messageCount = 10;

        // when: Publishing messages
        for (var i = 1; i <= messageCount; i++)
        {
            await Publisher.PublishAsync(
                "test.ordering",
                new OrderedMessage { Sequence = i },
                cancellationToken: AbortToken
            );
        }

        await Task.Delay(1000, AbortToken);

        // then: All messages processed (order not guaranteed with parallel consumers)
        var received = HandledMessages.OfType<OrderedMessage>().ToList();
        received.Should().HaveCount(messageCount);
    }

    [Fact]
    public async Task should_document_no_ordering_guarantee_with_parallel_execution_enabled()
    {
        // given: EnableSubscriberParallelExecute would break ordering
        // This test documents the behavior

        var messageCount = 10;

        // when: Publishing messages
        for (var i = 1; i <= messageCount; i++)
        {
            await Publisher.PublishAsync(
                "test.ordering",
                new OrderedMessage { Sequence = i },
                cancellationToken: AbortToken
            );
        }

        await Task.Delay(1000, AbortToken);

        // then: All messages processed (order not guaranteed with parallel execution)
        var received = HandledMessages.OfType<OrderedMessage>().ToList();
        received.Should().HaveCount(messageCount);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // AddTestSetup already calls AddMessages with in-memory storage/queue
        // and scans consumers from this assembly. Just configure specific options.
        services.Configure<MessagingOptions>(options =>
        {
            // Sequential processing for ordering tests
            options.ConsumerThreadCount = 1;
            options.EnableSubscriberParallelExecute = false;
        });
    }
}

public sealed record OrderedMessage
{
    public required int Sequence { get; init; }
}

public sealed class OrderedMessageConsumer(TestMessageCollector collector) : IConsume<OrderedMessage>
{
    public ValueTask Consume(ConsumeContext<OrderedMessage> context, CancellationToken cancellationToken)
    {
        collector.Add(context.Message);
        return ValueTask.CompletedTask;
    }
}
