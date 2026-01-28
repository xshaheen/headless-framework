using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Helpers;

namespace Tests;

// ReSharper disable AccessToDisposedClosure
public sealed class DispatcherTests : TestBase
{
    private readonly ILogger<Dispatcher> _logger = Substitute.For<ILogger<Dispatcher>>();
    private readonly ISubscribeExecutor _executor = Substitute.For<ISubscribeExecutor>();
    private readonly IDataStorage _storage = Substitute.For<IDataStorage>();

    [Fact]
    public async Task EnqueueToPublish_ShouldInvokeSend_WhenParallelSendDisabled()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = true,
                EnablePublishParallelSend = false,
                SubscriberParallelExecuteThreadCount = 2,
                SubscriberParallelExecuteBufferFactor = 2,
            }
        );

        await using var dispatcher = new Dispatcher(_logger, sender, options, _executor, _storage, TimeProvider.System);

        using var cts = new CancellationTokenSource();
        const string messageId = "testId";

        // when
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToPublish(_CreateTestMessage(messageId), AbortToken);
        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(1);
        sender.ReceivedMessages[0].DbId.Should().Be(messageId);
    }

    [Fact]
    public async Task EnqueueToPublish_ShouldBeThreadSafe_WhenParallelSendDisabled()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = true,
                EnablePublishParallelSend = false,
                SubscriberParallelExecuteThreadCount = 2,
                SubscriberParallelExecuteBufferFactor = 2,
            }
        );

        await using var dispatcher = new Dispatcher(_logger, sender, options, _executor, _storage, TimeProvider.System);
        using var cts = new CancellationTokenSource();

        var messages = Enumerable
            .Range(1, 100)
            .Select(i => _CreateTestMessage(i.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);

        var tasks = messages.Select(msg => Task.Run(() => dispatcher.EnqueueToPublish(msg, AbortToken), AbortToken));
        await Task.WhenAll(tasks);
        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(100);
        var receivedMessages = sender.ReceivedMessages.Select(m => m.DbId).Order(StringComparer.Ordinal).ToList();
        var expected = messages.Select(m => m.DbId).Order(StringComparer.Ordinal).ToList();
        expected.Should().Equal(receivedMessages);
    }

    [Fact]
    public async Task EnqueueToScheduler_ShouldBeThreadSafe_WhenDelayLessThenMinute()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = true,
                EnablePublishParallelSend = false,
                SubscriberParallelExecuteThreadCount = 2,
                SubscriberParallelExecuteBufferFactor = 2,
            }
        );

        await using var dispatcher = new Dispatcher(_logger, sender, options, _executor, _storage, TimeProvider.System);
        using var cts = new CancellationTokenSource();

        var messages = Enumerable
            .Range(1, 10000)
            .Select(i => _CreateTestMessage(i.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTime.UtcNow.AddSeconds(1);
        await Parallel.ForEachAsync(
            messages,
            CancellationToken.None,
            async (m, _) =>
            {
                await dispatcher.EnqueueToScheduler(m, dateTime);
            }
        );

        await Task.Delay(1500, CancellationToken.None);

        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(10000);

        var receivedMessages = sender.ReceivedMessages.Select(m => m.DbId).Order(StringComparer.Ordinal).ToList();
        var expected = messages.Select(m => m.DbId).Order(StringComparer.Ordinal).ToList();
        expected.Should().Equal(receivedMessages);
    }

    [Fact]
    public async Task EnqueueToScheduler_ShouldSendMessagesInCorrectOrder_WhenEarlierMessageIsSentLater()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = true,
                EnablePublishParallelSend = false,
                SubscriberParallelExecuteThreadCount = 2,
                SubscriberParallelExecuteBufferFactor = 2,
            }
        );

        await using var dispatcher = new Dispatcher(_logger, sender, options, _executor, _storage, TimeProvider.System);
        using var cts = new CancellationTokenSource();

        var messages = Enumerable
            .Range(1, 3)
            .Select(i => _CreateTestMessage(i.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTime.UtcNow;

        await dispatcher.EnqueueToScheduler(messages[0], dateTime.AddSeconds(1), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[1], dateTime.AddMilliseconds(200), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[2], dateTime.AddMilliseconds(100), cancellationToken: AbortToken);

        await Task.Delay(1200, CancellationToken.None);
        await cts.CancelAsync();

        // then
        sender.ReceivedMessages.Select(m => m.DbId).Should().Equal(["3", "2", "1"]);
    }

    [Fact]
    public async Task EnqueueToScheduler_ShouldBeThreadSafe_WhenDelayLessThenMinuteAndParallelSendEnabled()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = false,
                EnablePublishParallelSend = true,
                SubscriberParallelExecuteThreadCount = 2,
                SubscriberParallelExecuteBufferFactor = 2,
            }
        );

        await using var dispatcher = new Dispatcher(_logger, sender, options, _executor, _storage, TimeProvider.System);
        using var cts = new CancellationTokenSource();

        var messages = Enumerable
            .Range(1, 10000)
            .Select(i => _CreateTestMessage(i.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTime.UtcNow.AddMilliseconds(50);

        await Parallel.ForEachAsync(
            messages,
            CancellationToken.None,
            async (m, _) =>
            {
                await dispatcher.EnqueueToScheduler(m, dateTime);
            }
        );

        await Task.Delay(3000, CancellationToken.None);

        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(10000);

        var receivedMessages = sender.ReceivedMessages.Select(m => m.DbId).Order(StringComparer.Ordinal).ToList();
        var expected = messages.Select(m => m.DbId).Order(StringComparer.Ordinal).ToList();
        expected.Should().Equal(receivedMessages);
    }

    [Fact]
    public async Task EnqueueToScheduler_ShouldSendMessagesInCorrectOrder_WhenParallelSendEnabled()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = true,
                EnablePublishParallelSend = true,
                SubscriberParallelExecuteThreadCount = 2,
                SubscriberParallelExecuteBufferFactor = 2,
            }
        );

        await using var dispatcher = new Dispatcher(_logger, sender, options, _executor, _storage, TimeProvider.System);
        using var cts = new CancellationTokenSource();

        var messages = Enumerable
            .Range(1, 3)
            .Select(i => _CreateTestMessage(i.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTime.UtcNow;

        await dispatcher.EnqueueToScheduler(messages[0], dateTime.AddSeconds(1), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[1], dateTime.AddMilliseconds(200), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[2], dateTime.AddMilliseconds(100), cancellationToken: AbortToken);

        await Task.Delay(1200, CancellationToken.None);
        await cts.CancelAsync();

        // then
        sender.ReceivedMessages.Select(m => m.DbId).Should().Equal(["3", "2", "1"]);
    }

    [Fact]
    public async Task should_use_configured_batch_size_when_specified()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = true, PublishBatchSize = 50 });

        await using var dispatcher = new Dispatcher(_logger, sender, options, _executor, _storage, TimeProvider.System);
        using var cts = new CancellationTokenSource();

        var messages = Enumerable
            .Range(1, 100)
            .Select(i => _CreateTestMessage(i.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);

        foreach (var message in messages)
        {
            await dispatcher.EnqueueToPublish(message, AbortToken);
        }

        await Task.Delay(200, CancellationToken.None);
        await cts.CancelAsync();

        // then - verify all messages sent successfully
        sender.Count.Should().Be(100);
    }

    [Fact]
    public async Task should_process_all_messages_with_auto_calculated_batch_size()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(
            new MessagingOptions { EnablePublishParallelSend = true } // Auto-calculate batch size
        );

        await using var dispatcher = new Dispatcher(_logger, sender, options, _executor, _storage, TimeProvider.System);
        using var cts = new CancellationTokenSource();

        var messages = Enumerable
            .Range(1, 500)
            .Select(i => _CreateTestMessage(i.ToString(CultureInfo.InvariantCulture)))
            .ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);

        foreach (var message in messages)
        {
            await dispatcher.EnqueueToPublish(message, AbortToken);
        }

        await Task.Delay(300, CancellationToken.None);
        await cts.CancelAsync();

        // then - verify all messages sent successfully
        sender.Count.Should().Be(500);
    }

    private static MediumMessage _CreateTestMessage(string id = "1")
    {
        var message = new Message(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { { "headless-msg-id", id } },
            value: new MessageValue("test@test.com", "User")
        );

        return new MediumMessage
        {
            DbId = id,
            Origin = message,
            Content = JsonSerializer.Serialize(message),
        };
    }
}
