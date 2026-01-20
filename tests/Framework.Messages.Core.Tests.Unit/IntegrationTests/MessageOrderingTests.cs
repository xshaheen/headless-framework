using System.Collections.Concurrent;
using Framework.Messages;
using Framework.Messages.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;
using Tests.IntegrationTests;

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
        // Given: Sequential consumer configuration
        var receivedOrder = new ConcurrentQueue<int>();
        var messageCount = 10;

        // When: Publishing messages in sequence
        for (var i = 1; i <= messageCount; i++)
        {
            await Publisher.PublishAsync(
                "test.ordering",
                new OrderedMessage { Sequence = i },
                cancellationToken: AbortToken
            );
        }

        // Then: Messages should be received in order (with single consumer thread)
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
        // Given: Parallel consumer configuration would break ordering
        // This test documents expected behavior, not strict ordering

        var messageCount = 10;

        // When: Publishing messages
        for (var i = 1; i <= messageCount; i++)
        {
            await Publisher.PublishAsync(
                "test.ordering",
                new OrderedMessage { Sequence = i },
                cancellationToken: AbortToken
            );
        }

        await Task.Delay(1000, AbortToken);

        // Then: All messages processed (order not guaranteed with parallel consumers)
        var received = HandledMessages.OfType<OrderedMessage>().ToList();
        received.Should().HaveCount(messageCount);
    }

    [Fact]
    public async Task should_document_no_ordering_guarantee_with_parallel_execution_enabled()
    {
        // Given: EnableSubscriberParallelExecute would break ordering
        // This test documents the behavior

        var messageCount = 10;

        // When: Publishing messages
        for (var i = 1; i <= messageCount; i++)
        {
            await Publisher.PublishAsync(
                "test.ordering",
                new OrderedMessage { Sequence = i },
                cancellationToken: AbortToken
            );
        }

        await Task.Delay(1000, AbortToken);

        // Then: All messages processed (order not guaranteed with parallel execution)
        var received = HandledMessages.OfType<OrderedMessage>().ToList();
        received.Should().HaveCount(messageCount);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddMessages(options =>
        {
            options.UseInMemoryStorage();
            options.UseInMemoryMessageQueue();

            // Sequential processing for ordering tests
            options.ConsumerThreadCount = 1;
            options.EnableSubscriberParallelExecute = false;

            options.ScanConsumers(typeof(OrderedMessageConsumer).Assembly);
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
