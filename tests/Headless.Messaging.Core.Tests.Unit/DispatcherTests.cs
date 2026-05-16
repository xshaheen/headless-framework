using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    private readonly IServiceScopeFactory _scopeFactory = new ServiceCollection()
        .BuildServiceProvider()
        .GetRequiredService<IServiceScopeFactory>();

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

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );

        using var cts = new CancellationTokenSource();
        const long storageId = 1L;

        // when
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToPublish(_CreateTestMessage(storageId), AbortToken);
        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(1);
        sender.ReceivedMessages[0].StorageId.Should().Be(storageId);
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

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();

        var messages = Enumerable.Range(1, 100).Select(i => _CreateTestMessage(i)).ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);

        var tasks = messages.Select(msg => Task.Run(() => dispatcher.EnqueueToPublish(msg, AbortToken), AbortToken));
        await Task.WhenAll(tasks);
        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(100);
        var receivedMessages = sender.ReceivedMessages.Select(m => m.StorageId).Order().ToList();
        var expected = messages.Select(m => m.StorageId).Order().ToList();
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

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();

        var messages = Enumerable.Range(1, 10000).Select(i => _CreateTestMessage(i)).ToArray();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

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

        var receivedMessages = sender.ReceivedMessages.Select(m => m.StorageId).Order().ToList();
        var expected = messages.Select(m => m.StorageId).Order().ToList();
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

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();

        var messages = Enumerable.Range(1, 3).Select(i => _CreateTestMessage(i)).ToArray();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTime.UtcNow;

        await dispatcher.EnqueueToScheduler(messages[0], dateTime.AddSeconds(1), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[1], dateTime.AddMilliseconds(200), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[2], dateTime.AddMilliseconds(100), cancellationToken: AbortToken);

        await Task.Delay(1200, CancellationToken.None);
        await cts.CancelAsync();

        // then
        sender.ReceivedMessages.Select(m => m.StorageId).Should().Equal([3L, 2L, 1L]);
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

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();

        var messages = Enumerable.Range(1, 10000).Select(i => _CreateTestMessage(i)).ToArray();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

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

        var receivedMessages = sender.ReceivedMessages.Select(m => m.StorageId).Order().ToList();
        var expected = messages.Select(m => m.StorageId).Order().ToList();
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

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();

        var messages = Enumerable.Range(1, 3).Select(i => _CreateTestMessage(i)).ToArray();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTime.UtcNow;

        await dispatcher.EnqueueToScheduler(messages[0], dateTime.AddSeconds(1), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[1], dateTime.AddMilliseconds(200), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[2], dateTime.AddMilliseconds(100), cancellationToken: AbortToken);

        await Task.Delay(1200, CancellationToken.None);
        await cts.CancelAsync();

        // then
        sender.ReceivedMessages.Select(m => m.StorageId).Should().Equal([3L, 2L, 1L]);
    }

    [Fact]
    public async Task EnqueueToScheduler_ShouldNotQueueMessage_WhenStateChangeIsRejected()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = false });
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<object?>(),
                Arg.Any<DateTime?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(false));

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();

        // when
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToScheduler(
            _CreateTestMessage(),
            DateTime.UtcNow.AddMilliseconds(50),
            cancellationToken: AbortToken
        );
        await Task.Delay(200, CancellationToken.None);
        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_use_configured_batch_size_when_specified()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = true, PublishBatchSize = 50 });

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();

        var messages = Enumerable.Range(1, 100).Select(i => _CreateTestMessage(i)).ToArray();

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

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();

        var messages = Enumerable.Range(1, 500).Select(i => _CreateTestMessage(i)).ToArray();

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

    [Fact]
    public async Task should_signal_host_when_dispatcher_loop_faults()
    {
        // R2 regression — when a dispatcher loop dies on a non-OCE exception the dispatcher must
        // signal IHostApplicationLifetime.StopApplication so process supervisors recycle the host.
        // Before R2 the fault continuation only logged; PublishedChannel would fill indefinitely
        // (BoundedChannelFullMode.Wait) while the host stayed "healthy".
        //
        // The three loops (sending / processing / scheduler) all funnel into _SignalLoopTermination.
        // The interior of each loop body is wrapped in try/catch that absorbs non-OCE exceptions,
        // so to force a synthetic fault we attach a fault continuation to a *manually-faulted Task*
        // using the same wiring shape Dispatcher uses. This pins the contract that the continuation
        // calls IHostApplicationLifetime.StopApplication and survives a nested throw inside
        // StopApplication via the LoggerExtensions.DispatcherLoopStopApplicationFailed event.
        var lifetime = new TestHostApplicationLifetime();
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = false });

        await using var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory,
            lifetime
        );

        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);

        // Synthesise a loop fault by directly running the same continuation shape Dispatcher uses
        // (`OnlyOnFaulted` + `TaskScheduler.Default`) against a faulted Task. The continuation
        // delegate is bound on the live dispatcher instance via the private _SignalLoopTermination
        // method which is the single funnel for all 3 loops' faults. We invoke it through
        // reflection because the harness intentionally does NOT expose it on the public surface
        // (it is implementation detail of the fault-handling pipeline).
        var fault = new InvalidOperationException("synthetic loop fault — R2 regression");
        var signalMethod = typeof(Dispatcher).GetMethod(
            "_SignalLoopTermination",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        signalMethod
            .Should()
            .NotBeNull("Dispatcher must expose a single fault funnel for the three loop continuations");
        signalMethod!.Invoke(dispatcher, ["sending", fault]);

        await cts.CancelAsync();

        lifetime
            .StopRequested.Should()
            .BeTrue("dispatcher must request host shutdown when any loop fault continuation fires");
    }

    [Fact]
    public async Task should_not_request_host_stop_on_clean_dispatcher_shutdown()
    {
        // R2 negative — normal start/stop must not trip the host-lifetime contract. Pairs with the
        // synthesised-fault test above to pin the wiring in both directions.
        var lifetime = new TestHostApplicationLifetime();
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = false });

        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory,
            lifetime
        );

        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);

        await dispatcher.EnqueueToPublish(_CreateTestMessage(1), AbortToken);
        await Task.Delay(100, CancellationToken.None);
        await cts.CancelAsync();

        lifetime.StopRequested.Should().BeFalse("clean shutdown of the dispatcher must not request host stop");
    }

    private static MediumMessage _CreateTestMessage(long storageId = 1)
    {
        var messageId = storageId.ToString(CultureInfo.InvariantCulture);
        var message = new Message(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { { "headless-msg-id", messageId } },
            value: new MessageValue("test@test.com", "User")
        );

        return new MediumMessage
        {
            StorageId = storageId,
            Origin = message,
            Content = JsonSerializer.Serialize(message),
        };
    }

    /// <summary>
    /// Captures <see cref="IHostApplicationLifetime.StopApplication"/> calls so tests can assert
    /// the dispatcher signalled host shutdown after a loop fault. Implements the full lifetime
    /// surface but only the StopApplication path needs to be observable for R2.
    /// </summary>
    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _startedCts = new();
        private readonly CancellationTokenSource _stoppingCts = new();
        private readonly CancellationTokenSource _stoppedCts = new();
        private readonly TaskCompletionSource<bool> _stopRequestedTcs = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public CancellationToken ApplicationStarted => _startedCts.Token;
        public CancellationToken ApplicationStopping => _stoppingCts.Token;
        public CancellationToken ApplicationStopped => _stoppedCts.Token;

        public bool StopRequested { get; private set; }

        public void StopApplication()
        {
            StopRequested = true;
            _stopRequestedTcs.TrySetResult(true);
        }

        public async Task<bool> WaitForStopAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                return await _stopRequestedTcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return StopRequested;
            }
        }

        public void Dispose()
        {
            _startedCts.Dispose();
            _stoppingCts.Dispose();
            _stoppedCts.Dispose();
        }
    }
}
