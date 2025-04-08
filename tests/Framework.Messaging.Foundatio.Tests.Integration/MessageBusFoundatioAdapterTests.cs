using Foundatio.Messaging;
using Framework.Abstractions;
using Framework.Messaging;
using Framework.Testing.Tests;
using Humanizer;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;

namespace Tests;

public sealed class MessageBusFoundatioAdapterTests(ITestOutputHelper output) : TestBase(output)
{
    private static readonly SequentialAsBinaryGuidGenerator _GuidGenerator = new();

    private MessageBusFoundatioAdapter _GetMessageBus()
    {
#pragma warning disable CA2000 // Dispose objects before losing scope
        var inMemoryMessageBus = new InMemoryMessageBus(builder =>
            builder.Topic("test-lock").LoggerFactory(LoggerFactory).Serializer(FoundationHelper.JsonSerializer)
        );
#pragma warning restore CA2000

        return new(inMemoryMessageBus, _GuidGenerator);
    }

    [Fact]
    public async Task should_be_able_to_publish_subscribe()
    {
        using var messageBus = _GetMessageBus();

        var countdown = new AsyncCountdownEvent(1);

        await messageBus.SubscribeAsync<MessageA>(msg =>
        {
            Logger.LogTrace("Got message");
            msg.Data.Should().Be("Hello");
            msg.Items.Should().ContainKey("Test");
            countdown.Signal();
            Logger.LogTrace("Set event");
        });

        await Task.Delay(100);

        await messageBus.PublishAsync(new MessageA { Data = "Hello", Items = { { "Test", "Test" } } });

        Logger.LogTrace("Published one...");
        await countdown.WaitAsync(5.Seconds());
        countdown.CurrentCount.Should().Be(0);
    }

    public sealed class MessageA
    {
        public required string Data { get; init; }

        public Dictionary<string, string> Items { get; init; } = new(StringComparer.Ordinal);
    }
}
