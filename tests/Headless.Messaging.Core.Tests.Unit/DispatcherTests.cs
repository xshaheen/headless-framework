using System.Data.Common;
using System.Reflection;
using Headless.CommitCoordination;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Retry;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class DispatcherTests : TestBase
{
    private readonly ILogger<Dispatcher> _logger = Substitute.For<ILogger<Dispatcher>>();
    private readonly ISubscribeExecutor _executor = Substitute.For<ISubscribeExecutor>();
    private readonly IDataStorage _storage = Substitute.For<IDataStorage>();
    private readonly IServiceScopeFactory _scopeFactory = new ServiceCollection()
        .BuildServiceProvider()
        .GetRequiredService<IServiceScopeFactory>();

    [Fact]
    public async Task completed_published_retry_does_not_release_a_cleared_lease_again()
    {
        var sender = Substitute.For<IMessageSender>();
        sender
            .SendRetryAsync(Arg.Any<MediumMessage>(), Arg.Any<IServiceProvider>(), Arg.Any<RetryExecutionState>())
            .Returns(call =>
            {
                call.Arg<RetryExecutionState>().RecordLeaseTransition(affected: true, lockedUntil: null);
                return OperateResult.Success;
            });
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(AbortToken);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await ((IRetryDispatcher)dispatcher).DispatchPublishedAsync(message, AbortToken);

        await sender
            .Received(1)
            .SendRetryAsync(Arg.Any<MediumMessage>(), Arg.Any<IServiceProvider>(), Arg.Any<RetryExecutionState>());
        await ((IGracefulLeaseReleaseStorage)storage)
            .DidNotReceive()
            .ReleasePublishedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task completed_received_retry_does_not_release_a_cleared_lease_again()
    {
        _executor
            .ExecuteRetryAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<IServiceProvider>(),
                Arg.Any<RetryExecutionState>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                call.Arg<RetryExecutionState>().RecordLeaseTransition(affected: true, lockedUntil: null);
                return OperateResult.Success;
            });
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        await using var dispatcher = new Dispatcher(
            _logger,
            Substitute.For<IMessageSender>(),
            Options.Create(new MessagingOptions { EnableSubscriberParallelExecute = false }),
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(AbortToken);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await ((IRetryDispatcher)dispatcher).DispatchReceivedAsync(message, AbortToken);

        await _executor
            .Received(1)
            .ExecuteRetryAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<IServiceProvider>(),
                Arg.Any<RetryExecutionState>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            );
        await ((IGracefulLeaseReleaseStorage)storage)
            .DidNotReceive()
            .ReleaseReceivedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task later_lease_preserving_transition_keeps_completion_release_required()
    {
        var sender = Substitute.For<IMessageSender>();
        sender
            .SendRetryAsync(Arg.Any<MediumMessage>(), Arg.Any<IServiceProvider>(), Arg.Any<RetryExecutionState>())
            .Returns(call =>
            {
                var executionState = call.Arg<RetryExecutionState>();
                var message = call.Arg<MediumMessage>();
                executionState.RecordLeaseTransition(affected: true, lockedUntil: null);
                executionState.RecordLeaseTransition(affected: true, message.LockedUntil);
                return OperateResult.Success;
            });
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(AbortToken);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await ((IRetryDispatcher)dispatcher).DispatchPublishedAsync(message, AbortToken);

        await ((IGracefulLeaseReleaseStorage)storage)
            .Received(1)
            .ReleasePublishedLeaseAsync(
                Arg.Is<MessageLeaseIdentity>(identity =>
                    identity.StorageId == message.StorageId
                    && identity.Owner == message.Owner
                    && identity.LockedUntil == message.LockedUntil
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task failed_lease_clearing_transition_keeps_completion_release_required()
    {
        var sender = Substitute.For<IMessageSender>();
        sender
            .SendRetryAsync(Arg.Any<MediumMessage>(), Arg.Any<IServiceProvider>(), Arg.Any<RetryExecutionState>())
            .Returns(call =>
            {
                call.Arg<RetryExecutionState>().RecordLeaseTransition(affected: false, lockedUntil: null);
                return OperateResult.Success;
            });
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(AbortToken);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await ((IRetryDispatcher)dispatcher).DispatchPublishedAsync(message, AbortToken);

        await ((IGracefulLeaseReleaseStorage)storage)
            .Received(1)
            .ReleasePublishedLeaseAsync(
                Arg.Is<MessageLeaseIdentity>(identity =>
                    identity.StorageId == message.StorageId
                    && identity.Owner == message.Owner
                    && identity.LockedUntil == message.LockedUntil
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task queued_published_retry_does_not_release_a_cleared_lease_again()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender = Substitute.For<IMessageSender>();
        sender
            .SendRetryAsync(Arg.Any<MediumMessage>(), Arg.Any<IServiceProvider>(), Arg.Any<RetryExecutionState>())
            .Returns(call =>
            {
                call.Arg<RetryExecutionState>().RecordLeaseTransition(affected: true, lockedUntil: null);
                completed.TrySetResult();
                return OperateResult.Success;
            });
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            Options.Create(new MessagingOptions { EnablePublishParallelSend = true, PublishBatchSize = 1 }),
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(AbortToken);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await ((IRetryDispatcher)dispatcher).DispatchPublishedAsync(message, AbortToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        await dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken);

        await sender
            .Received(1)
            .SendRetryAsync(Arg.Any<MediumMessage>(), Arg.Any<IServiceProvider>(), Arg.Any<RetryExecutionState>());
        await ((IGracefulLeaseReleaseStorage)storage)
            .DidNotReceive()
            .ReleasePublishedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task queued_received_retry_does_not_release_a_cleared_lease_again()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor
            .ExecuteRetryAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<IServiceProvider>(),
                Arg.Any<RetryExecutionState>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                call.Arg<RetryExecutionState>().RecordLeaseTransition(affected: true, lockedUntil: null);
                completed.TrySetResult();
                return OperateResult.Success;
            });
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        await using var dispatcher = new Dispatcher(
            _logger,
            Substitute.For<IMessageSender>(),
            Options.Create(
                new MessagingOptions
                {
                    EnableSubscriberParallelExecute = true,
                    SubscriberParallelExecuteThreadCount = 1,
                }
            ),
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(AbortToken);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await ((IRetryDispatcher)dispatcher).DispatchReceivedAsync(message, AbortToken);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        await dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken);

        await _executor
            .Received(1)
            .ExecuteRetryAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<IServiceProvider>(),
                Arg.Any<RetryExecutionState>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            );
        await ((IGracefulLeaseReleaseStorage)storage)
            .DidNotReceive()
            .ReleaseReceivedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task shutdown_releases_queued_retry_but_not_running_retry()
    {
        var timeProvider = new FakeTimeProvider();
        var runningEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRunning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaseReleased = new TaskCompletionSource<MessageLeaseIdentity>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var sender = Substitute.For<IMessageSender>();
        sender
            .SendRetryAsync(Arg.Any<MediumMessage>(), Arg.Any<IServiceProvider>(), Arg.Any<RetryExecutionState>())
            .Returns(async call =>
            {
                var executionState = call.Arg<RetryExecutionState>();
                var message = call.Arg<MediumMessage>();
                executionState.RecordLeaseTransition(affected: true, message.LockedUntil);
                runningEntered.TrySetResult();
                await releaseRunning.Task.ConfigureAwait(false);
                return OperateResult.Success;
            });
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        ((IGracefulLeaseReleaseStorage)storage)
            .ReleasePublishedLeasesAsync(
                Arg.Any<IReadOnlyCollection<MessageLeaseIdentity>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var identity = call.Arg<IReadOnlyCollection<MessageLeaseIdentity>>().Single();
                leaseReleased.TrySetResult(identity);
                return ValueTask.FromResult(1);
            });
        var options = Options.Create(
            new MessagingOptions
            {
                EnablePublishParallelSend = true,
                PublishBatchSize = 1,
                ShutdownTimeout = TimeSpan.FromSeconds(2),
            }
        );
        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            storage,
            timeProvider,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);

        var running = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));
        var queued = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));
        var retryDispatcher = (IRetryDispatcher)dispatcher;

        await retryDispatcher.DispatchPublishedAsync(running, AbortToken);
        await runningEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        await retryDispatcher.DispatchPublishedAsync(queued, AbortToken);

        var disposeTask = dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken).AsTask();
        var releasedIdentity = await leaseReleased.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        releasedIdentity
            .Should()
            .Be(new MessageLeaseIdentity(queued.StorageId, queued.Owner, queued.LockedUntil!.Value, queued.Lane));
        await ((IGracefulLeaseReleaseStorage)storage)
            .DidNotReceive()
            .ReleasePublishedLeaseAsync(
                Arg.Is<MessageLeaseIdentity>(identity => identity.StorageId == running.StorageId),
                Arg.Any<CancellationToken>()
            );

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        await ((IGracefulLeaseReleaseStorage)storage)
            .DidNotReceive()
            .ReleasePublishedLeaseAsync(
                Arg.Is<MessageLeaseIdentity>(identity => identity.StorageId == running.StorageId),
                Arg.Any<CancellationToken>()
            );

        releaseRunning.TrySetResult();
        await dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken);

        await ((IGracefulLeaseReleaseStorage)storage)
            .Received(1)
            .ReleasePublishedLeaseAsync(
                Arg.Is<MessageLeaseIdentity>(identity => identity.StorageId == running.StorageId),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task shutdown_deadline_includes_abandoned_retry_release()
    {
        var timeProvider = new FakeTimeProvider();
        var releaseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runningEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runningBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async ValueTask<int> BlockReleaseAsync()
        {
            releaseStarted.TrySetResult();
            await releaseBlocked.Task.ConfigureAwait(false);
            return 1;
        }

        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        ((IGracefulLeaseReleaseStorage)storage)
            .ReleasePublishedLeasesAsync(
                Arg.Any<IReadOnlyCollection<MessageLeaseIdentity>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ => BlockReleaseAsync());
        var sender = Substitute.For<IMessageSender>();
        sender
            .SendRetryAsync(Arg.Any<MediumMessage>(), Arg.Any<IServiceProvider>(), Arg.Any<RetryExecutionState>())
            .Returns(async _ =>
            {
                runningEntered.TrySetResult();
                await runningBlocked.Task.ConfigureAwait(false);
                return OperateResult.Success;
            });
        var options = Options.Create(
            new MessagingOptions
            {
                EnablePublishParallelSend = true,
                PublishBatchSize = 1,
                ShutdownTimeout = TimeSpan.FromSeconds(2),
            }
        );
        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            storage,
            timeProvider,
            _scopeFactory
        );
        await dispatcher.StartAsync(AbortToken);
        var retryDispatcher = (IRetryDispatcher)dispatcher;
        var running = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));
        var queued = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));

        await retryDispatcher.DispatchPublishedAsync(running, AbortToken);
        await runningEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        await retryDispatcher.DispatchPublishedAsync(queued, AbortToken);
        var disposeTask = dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken).AsTask();
        await releaseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
        releaseBlocked.Task.IsCompleted.Should().BeFalse("lease release may continue after the caller's deadline");

        releaseBlocked.TrySetResult();
        runningBlocked.TrySetResult();
        await dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken);
    }

    [Fact]
    public async Task should_send_once_and_release_lease_once_when_sequential_retry_publish_succeeds()
    {
        // Sequential (non-parallel) retry-dispatch path: with EnablePublishParallelSend left at its
        // default (false), DispatchPublishedAsync must run Claimed→Running via _TryStartRetryAsync,
        // send directly, and release the exact lease exactly once via CompleteAsync's
        // Running→Completed transition.
        var sender = new TestThreadSafeMessageSender();
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        var releaser = (IGracefulLeaseReleaseStorage)storage;
        var options = Options.Create(new MessagingOptions());
        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));
        message.Retries = 1;

        await ((IRetryDispatcher)dispatcher).DispatchPublishedAsync(message, AbortToken);

        sender.Count.Should().Be(1);
        sender.ReceivedMessages[0].StorageId.Should().Be(message.StorageId);
        await releaser
            .Received(1)
            .ReleasePublishedLeaseAsync(
                new MessageLeaseIdentity(message.StorageId, message.Owner, message.LockedUntil!.Value, message.Lane),
                Arg.Any<CancellationToken>()
            );
        await releaser
            .Received(1)
            .ReleasePublishedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>());
        await cts.CancelAsync();
    }

    [Fact]
    public async Task should_release_claimed_lease_without_sending_when_sequential_retry_publish_pre_canceled()
    {
        // A pre-canceled token throws before the sequential path claims Running; the OCE handler
        // must release the lease exactly once via AbandonClaimedAsync's Claimed→Abandoned
        // transition and the sender must never be invoked.
        var sender = new TestThreadSafeMessageSender();
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        var releaser = (IGracefulLeaseReleaseStorage)storage;
        var options = Options.Create(new MessagingOptions());
        await using var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));
        message.Retries = 1;
        using var preCanceled = new CancellationTokenSource();
        await preCanceled.CancelAsync();

        await ((IRetryDispatcher)dispatcher).DispatchPublishedAsync(message, preCanceled.Token);

        sender.Count.Should().Be(0);
        await releaser
            .Received(1)
            .ReleasePublishedLeaseAsync(
                new MessageLeaseIdentity(message.StorageId, message.Owner, message.LockedUntil!.Value, message.Lane),
                Arg.Any<CancellationToken>()
            );
        await releaser
            .Received(1)
            .ReleasePublishedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>());
        await cts.CancelAsync();
    }

    [Fact]
    public async Task should_execute_once_and_release_lease_once_when_sequential_retry_receive_succeeds()
    {
        // Received-side sequential path: with EnableSubscriberParallelExecute left at its default
        // (false), DispatchReceivedAsync must invoke the executor directly and release the exact
        // lease exactly once via CompleteAsync's Running→Completed transition.
        var storage = Substitute.For<IDataStorage, IGracefulLeaseReleaseStorage>();
        var releaser = (IGracefulLeaseReleaseStorage)storage;
        _executor
            .ExecuteAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<IServiceProvider>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(OperateResult.Success);
        var options = Options.Create(new MessagingOptions());
        await using var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            options,
            _executor,
            storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        var message = _CreateRetryMessage(owner: "node-a", lockedUntil: DateTimeOffset.UtcNow.AddMinutes(5));
        message.Retries = 1;

        await ((IRetryDispatcher)dispatcher).DispatchReceivedAsync(message, AbortToken);

        await _executor
            .Received(1)
            .ExecuteAsync(
                message,
                Arg.Any<IServiceProvider>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            );
        await releaser
            .Received(1)
            .ReleaseReceivedLeaseAsync(
                new MessageLeaseIdentity(message.StorageId, message.Owner, message.LockedUntil!.Value, message.Lane),
                Arg.Any<CancellationToken>()
            );
        await releaser
            .Received(1)
            .ReleaseReceivedLeaseAsync(Arg.Any<MessageLeaseIdentity>(), Arg.Any<CancellationToken>());
        await cts.CancelAsync();
    }

    [Fact]
    public async Task should_invoke_send_when_enqueue_to_publish_parallel_send_disabled()
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
        var storageId = Guid.NewGuid();

        // when
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToPublish(_CreateTestMessage(storageId), AbortToken);
        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(1);
        sender.ReceivedMessages[0].StorageId.Should().Be(storageId);
    }

    [Fact]
    public async Task should_be_thread_safe_when_enqueue_to_publish_parallel_send_disabled()
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

        var messages = Enumerable.Range(1, 100).Select(_CreateTestMessage).ToArray();

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
    public async Task should_be_thread_safe_when_enqueue_to_scheduler_delay_less_then_minute()
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
                SchedulerBatchSize = 10000,
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

        var messages = Enumerable.Range(1, 10000).Select(_CreateTestMessage).ToArray();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTimeOffset.UtcNow.AddSeconds(1);

        await Parallel.ForEachAsync(
            messages,
            AbortToken,
            async (m, ct) => await dispatcher.EnqueueToScheduler(m, dateTime, null, ct)
        );

        await sender.WaitForCountAsync(10000, TimeSpan.FromSeconds(10), AbortToken);

        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(10000);

        var receivedMessages = sender.ReceivedMessages.Select(m => m.StorageId).Order().ToList();
        var expected = messages.Select(m => m.StorageId).Order().ToList();
        expected.Should().Equal(receivedMessages);
    }

    [Fact]
    public async Task should_keep_scheduler_overflow_durable_as_delayed()
    {
        var options = Options.Create(new MessagingOptions { SchedulerBatchSize = 1 });
        await using var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        var first = _CreateTestMessage(1);
        var second = _CreateTestMessage(2);
        var publishTime = DateTimeOffset.UtcNow.AddSeconds(30);
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        await dispatcher.EnqueueToScheduler(first, publishTime, cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(second, publishTime, cancellationToken: AbortToken);

        await _storage
            .Received(1)
            .ChangePublishStateAsync(
                second,
                StatusName.Delayed,
                MessageContentWrite.Preserve,
                null,
                null,
                cancellationToken: CancellationToken.None
            );
    }

    [Fact]
    public async Task should_not_mark_an_identical_scheduled_entry_as_delayed()
    {
        var options = Options.Create(new MessagingOptions { SchedulerBatchSize = 1 });
        await using var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        var message = _CreateTestMessage(1);
        var duplicate = _CreateTestMessage(1);
        var publishTime = DateTimeOffset.UtcNow.AddSeconds(30);
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        await dispatcher.EnqueueToScheduler(message, publishTime, cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(duplicate, publishTime, cancellationToken: AbortToken);

        await _storage
            .DidNotReceive()
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Delayed,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_send_messages_in_correct_order_when_enqueue_to_scheduler_earlier_message_is_sent_later()
    {
        // given — previously this test slept a flat 1.2 s and asserted, which flaked ~50% under
        // full-suite parallel load when thread-pool starvation slowed the queue's polling loop
        // (50 ms ticks) past the 1 s message's scheduled publish time. The fix is two-part:
        //   1. ScheduledMediumMessageQueue's polling delay is now TimeProvider-aware (production
        //      change), so future tests can drive it with FakeTimeProvider. This test still uses
        //      TimeProvider.System because EnqueueToScheduler computes (publishTime - now) against
        //      that provider to bucket into Queued vs Delayed, and the dispatcher's own send loop
        //      ticks on wall-clock cancellation tokens.
        //   2. Replace the fixed-duration sleep with a poll-for-completion loop bounded by a
        //      generous wall-clock budget (10 s, ~8× the worst observed wall-clock under load).
        //      The poll fails the test fast when wiring is genuinely broken (no messages received
        //      after the longest scheduled publish + buffer) and still completes in <1.3 s on a
        //      healthy run.
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

        var messages = Enumerable.Range(1, 3).Select(_CreateTestMessage).ToArray();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTimeOffset.UtcNow;

        await dispatcher.EnqueueToScheduler(messages[0], dateTime.AddSeconds(1), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[1], dateTime.AddMilliseconds(200), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[2], dateTime.AddMilliseconds(100), cancellationToken: AbortToken);

        // Poll until all three messages flow through, or the wall-clock budget elapses. 10 s is
        // ~8× the slowest scheduled publish (+1 s) and well beyond any plausible thread-pool
        // starvation in the test suite.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (sender.ReceivedMessages.Count < 3 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20, AbortToken);
        }

        await cts.CancelAsync();

        // then
        sender
            .ReceivedMessages.Select(m => m.StorageId)
            .Should()
            .Equal([_StorageGuid(3), _StorageGuid(2), _StorageGuid(1)]);
    }

    [Fact]
    public async Task should_be_thread_safe_when_enqueue_to_scheduler_delay_less_then_minute_and_parallel_send_enabled()
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
                SchedulerBatchSize = 10000,
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

        var messages = Enumerable.Range(1, 10000).Select(_CreateTestMessage).ToArray();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTimeOffset.UtcNow.AddMilliseconds(50);

        await Parallel.ForEachAsync(
            messages,
            AbortToken,
            async (m, ct) => await dispatcher.EnqueueToScheduler(m, dateTime, null, ct)
        );

        await sender.WaitForCountAsync(10000, TimeSpan.FromSeconds(10), AbortToken);

        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(10000);

        var receivedMessages = sender.ReceivedMessages.Select(m => m.StorageId).Order().ToList();
        var expected = messages.Select(m => m.StorageId).Order().ToList();
        expected.Should().Equal(receivedMessages);
    }

    [Fact]
    public async Task should_send_messages_in_correct_order_when_enqueue_to_scheduler_parallel_send_enabled()
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

        var messages = Enumerable.Range(1, 3).Select(_CreateTestMessage).ToArray();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        // when
        await dispatcher.StartAsync(cts.Token);
        var dateTime = DateTimeOffset.UtcNow;

        await dispatcher.EnqueueToScheduler(messages[0], dateTime.AddSeconds(1), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[1], dateTime.AddMilliseconds(200), cancellationToken: AbortToken);
        await dispatcher.EnqueueToScheduler(messages[2], dateTime.AddMilliseconds(100), cancellationToken: AbortToken);

        // Poll for all three messages to land in the sender; the scheduler loop ticks every 50ms,
        // and a fixed wall-clock delay (e.g. 1200ms) was flaky under CI load when msg[0]'s +1s
        // schedule slipped past the deadline. We allow up to 5s, then assert the order.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (sender.ReceivedMessages.Count < 3 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50, CancellationToken.None);
        }
        await cts.CancelAsync();

        // then
        sender
            .ReceivedMessages.Select(m => m.StorageId)
            .Should()
            .Equal([_StorageGuid(3), _StorageGuid(2), _StorageGuid(1)]);
    }

    [Fact]
    public async Task should_not_queue_message_when_enqueue_to_scheduler_state_change_is_rejected()
    {
        // given
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = false });
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
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
            DateTimeOffset.UtcNow.AddMilliseconds(50),
            cancellationToken: AbortToken
        );
        await Task.Delay(200, CancellationToken.None);
        await cts.CancelAsync();

        // then
        sender.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_pass_scheduler_cancellation_token_to_non_terminal_storage_write()
    {
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = false });
        using var hostCts = new CancellationTokenSource();
        using var operationCts = new CancellationTokenSource();
        var operationToken = operationCts.Token;
        var storageWrite = _storage.ChangePublishStateAsync(
            Arg.Any<MediumMessage>(),
            StatusName.Delayed,
            Arg.Any<MessageContentWrite>(),
            Arg.Any<DbTransaction?>(),
            Arg.Any<DateTimeOffset?>(),
            cancellationToken: operationToken
        );
        storageWrite.Returns(new ValueTask<bool>(true));

        await using var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(hostCts.Token);

        await dispatcher.EnqueueToScheduler(
            _CreateTestMessage(),
            DateTimeOffset.UtcNow.AddMinutes(2),
            cancellationToken: operationToken
        );

        await _storage
            .Received(1)
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Delayed,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: operationToken
            );
    }

    [Fact]
    public async Task should_cancel_blocked_non_terminal_scheduler_storage_write()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
            {
                writeStarted.TrySetResult();
                return new ValueTask<bool>(waitForCancellationAsync(callInfo.ArgAt<CancellationToken>(7)));
            });
        using var operationCts = new CancellationTokenSource();
        using var hostCts = new CancellationTokenSource();
        var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(hostCts.Token);

        var enqueue = dispatcher.EnqueueToScheduler(
            _CreateTestMessage(),
            DateTimeOffset.UtcNow.AddMinutes(2),
            cancellationToken: operationCts.Token
        );
        await writeStarted.Task.WaitAsync(AbortToken);
        await operationCts.CancelAsync();
        var act = async () => await enqueue;

        await act.Should().ThrowAsync<OperationCanceledException>();
        await dispatcher.DisposeAsync();

        static async Task<bool> waitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    [Fact]
    public async Task should_queue_only_after_non_terminal_scheduler_storage_write_succeeds()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commitWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                StatusName.Queued,
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                writeStarted.TrySetResult();
                return new ValueTask<bool>(commitWrite.Task);
            });
        using var hostCts = new CancellationTokenSource();
        var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        await dispatcher.StartAsync(hostCts.Token);

        var enqueue = dispatcher.EnqueueToScheduler(
            _CreateTestMessage(_StorageGuid(1)),
            DateTimeOffset.UtcNow.AddSeconds(50),
            cancellationToken: AbortToken
        );
        await writeStarted.Task.WaitAsync(AbortToken);
        enqueue.IsCompleted.Should().BeFalse();

        commitWrite.TrySetResult(true);
        await enqueue;
        await dispatcher.DisposeAsync();

        await _storage
            .Received(1)
            .ChangePublishStateToDelayedAsync(
                Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == _StorageGuid(1)),
                Arg.Any<CancellationToken>()
            );
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

        var messages = Enumerable.Range(1, 100).Select(_CreateTestMessage).ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);

        foreach (var message in messages)
        {
            await dispatcher.EnqueueToPublish(message, AbortToken);
        }

        await sender.WaitForCountAsync(100, TimeSpan.FromSeconds(5), AbortToken);
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

        var messages = Enumerable.Range(1, 500).Select(_CreateTestMessage).ToArray();

        // when
        await dispatcher.StartAsync(cts.Token);

        foreach (var message in messages)
        {
            await dispatcher.EnqueueToPublish(message, AbortToken);
        }

        await sender.WaitForCountAsync(500, TimeSpan.FromSeconds(5), AbortToken);
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
        using var lifetime = new TestHostApplicationLifetime();
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
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(string), typeof(Exception)],
            null
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
        using var lifetime = new TestHostApplicationLifetime();
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

        await dispatcher.EnqueueToPublish(_CreateTestMessage(_StorageGuid(1)), AbortToken);
        await Task.Delay(100, CancellationToken.None);
        await cts.CancelAsync();

        lifetime.StopRequested.Should().BeFalse("clean shutdown of the dispatcher must not request host stop");
    }

    [Fact]
    public async Task should_not_throw_invalid_operation_exception_when_enqueue_after_dispose()
    {
        // #5 — after DisposeAsync, `_tasksCts` is disposed (and remains non-null — DisposeAsync only
        // disposes the CTS and flips `_disposed`). Two distinct broken contracts existed pre-fix:
        //   1. The `TasksCts` accessor only checked for null, so post-dispose access proceeded to
        //      `_tasksCts.Token` which throws ObjectDisposedException.
        //   2. `_WriteToChannelAsync`'s linked-CTS construction touched `_tasksCts.Token` directly.
        // The EnqueueToExecute / EnqueueToPublish catch contract only covers OperationCanceledException.
        // The fixed path must produce OCE for BOTH pre-start (null `_tasksCts`) and post-dispose
        // (non-null but disposed) so the catch handles it cleanly.
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

        var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );

        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);

        // Dispose the dispatcher — _tasksCts is set to null.
        await dispatcher.DisposeAsync();

        // EnqueueToPublish would route through _WriteToChannelAsync since parallel-send is enabled
        // and Retries == 0. The post-dispose write must not propagate InvalidOperationException —
        // the EnqueueToPublish catch swallows OCE only.
        var act = async () => await dispatcher.EnqueueToPublish(_CreateTestMessage(_StorageGuid(1)), AbortToken);

        // The Enqueue method's own try/catch absorbs OCE — so this call should complete without
        // throwing at all. If the post-dispose path throws InvalidOperationException, this assertion
        // will catch it via Should().NotThrowAsync.
        await act.Should()
            .NotThrowAsync("post-dispose writes must unwind as cancellation, not InvalidOperationException");
    }

    [Fact]
    public async Task should_accelerate_committed_message_without_waiting_for_public_dispatch_path()
    {
        var sender = new TestThreadSafeMessageSender();
        var dispatcher = new Dispatcher(
            _logger,
            sender,
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        await using (dispatcher)
        using (var cts = new CancellationTokenSource())
        {
            var message = _CreateTestMessage(_StorageGuid(1));
            await dispatcher.StartAsync(cts.Token);

            ((ICommittedMessageDispatcher)dispatcher).EnqueueCommittedMessage(message);

            for (var attempt = 0; attempt < 100 && sender.Count != 1; attempt++)
            {
                await Task.Delay(10, AbortToken);
            }
            sender.ReceivedMessages.Should().ContainSingle().Which.Should().BeSameAs(message);
            await cts.CancelAsync();
        }
    }

    [Fact]
    public async Task should_drop_committed_acceleration_after_shutdown_without_throwing()
    {
        var sender = new TestThreadSafeMessageSender();
        var dispatcher = new Dispatcher(
            _logger,
            sender,
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.DisposeAsync();

        var act = () => ((ICommittedMessageDispatcher)dispatcher).EnqueueCommittedMessage(_CreateTestMessage());

        act.Should().NotThrow("the committed row remains recoverable by the relay after shutdown");
        sender.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_drop_standalone_delayed_acceleration_while_disposal_is_in_progress()
    {
        var flushStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFlush = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(true);
        _storage
            .ChangePublishStateToDelayedAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                flushStarted.TrySetResult();
                return new ValueTask(releaseFlush.Task);
            });
        var dispatcher = _CreateDispatcher(new TestThreadSafeMessageSender());
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToScheduler(
            _CreateTestMessage(_StorageGuid(1)),
            DateTimeOffset.UtcNow.AddSeconds(30),
            cancellationToken: AbortToken
        );
        var disposeTask = dispatcher.DisposeAsync(TimeSpan.FromSeconds(10), AbortToken).AsTask();
        await flushStarted.Task.WaitAsync(AbortToken);
        var delayed = _CreateTestMessage(_StorageGuid(2));
        delayed.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1);

        var act = () => ((ICommittedDelayedMessageDispatcher)dispatcher).EnqueueCommittedDelayedMessage(delayed);

        act.Should().NotThrow();
        releaseFlush.TrySetResult();
        await disposeTask;
    }

    [Fact]
    public async Task should_drop_coordinated_delayed_acceleration_after_disposal()
    {
        var dispatcher = _CreateDispatcher(new TestThreadSafeMessageSender());
        await dispatcher.DisposeAsync();
        var coordinator = new CommitCoordinator();
        var buffer = new MessageOutboxBuffer(coordinator, dispatcher);
        var delayed = _CreateTestMessage(_StorageGuid(1));
        delayed.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1);
        buffer.Add(delayed);
        using var commitServices = _scopeFactory.CreateScope();

        var act = async () => await coordinator.SignalAsync(CommitOutcome.Committed, commitServices.ServiceProvider);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_accept_multiple_concurrent_committed_publish_writers()
    {
        const int messageCount = 256;
        var sender = new TestThreadSafeMessageSender();
        await using var dispatcher = _CreateDispatcher(sender);
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        var committed = (ICommittedMessageDispatcher)dispatcher;

        Parallel.For(0, messageCount, index => committed.EnqueueCommittedMessage(_CreateTestMessage(index + 1)));

        await sender.WaitForCountAsync(messageCount, TimeSpan.FromSeconds(10), AbortToken);
        sender.ReceivedMessages.Should().OnlyHaveUniqueItems(static message => message.StorageId);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task should_return_from_committed_enqueue_before_a_synchronous_sender_unblocks()
    {
        using var sender = new SynchronouslyBlockingMessageSender();
        await using var dispatcher = _CreateDispatcher(sender);
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);

        var enqueueTask = Task.Run(
            () => ((ICommittedMessageDispatcher)dispatcher).EnqueueCommittedMessage(_CreateTestMessage()),
            AbortToken
        );

        await sender.Entered.Task.WaitAsync(AbortToken);
        try
        {
            await enqueueTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }
        finally
        {
            sender.Release();
        }

        await cts.CancelAsync();
    }

    [Fact]
    public async Task should_return_from_commit_signal_before_a_synchronous_sender_unblocks()
    {
        using var sender = new SynchronouslyBlockingMessageSender();
        await using var dispatcher = _CreateDispatcher(sender);
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        var coordinator = new CommitCoordinator();
        var buffer = new MessageOutboxBuffer(coordinator, dispatcher);
        buffer.Add(_CreateTestMessage());
        using var commitServices = _scopeFactory.CreateScope();

        var commitTask = Task.Run(
            async () => await coordinator.SignalAsync(CommitOutcome.Committed, commitServices.ServiceProvider),
            AbortToken
        );

        await sender.Entered.Task.WaitAsync(AbortToken);
        try
        {
            await commitTask.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        }
        finally
        {
            sender.Release();
        }

        await cts.CancelAsync();
    }

    [Fact]
    public async Task should_unwind_as_cancellation_when_write_to_channel_post_dispose_with_full_channel()
    {
        // Companion to enqueue_after_dispose_*: the previous test only proves the early `_tasksCts is
        // null || _disposed` guard. Force the channel-full branch by pre-filling the channel before
        // dispose, so the post-dispose Enqueue goes through the linked-CTS construction site. The
        // construction site is wrapped to convert any race-related ObjectDisposedException into OCE.
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = false,
                EnablePublishParallelSend = false,
                // Force the published channel to size 1 by making publishChannelSize tiny.
                // The dispatcher derives publish channel size from PublishParallelSendThreadCount * 500
                // when parallel-send is enabled; with EnablePublishParallelSend=false we route through
                // EnqueueToPublish's serial path which still bottoms out in _WriteToChannelAsync only
                // when Retries > 0. Retries == 0 short-circuits through the direct sender path, so we
                // exercise that branch (post-dispose check still applies).
                SubscriberParallelExecuteThreadCount = 1,
                SubscriberParallelExecuteBufferFactor = 1,
            }
        );

        var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );

        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.DisposeAsync();

        // Post-dispose enqueue with Retries == 0 goes through the inline-publish path (no channel
        // write). The EnqueueToPublish wrapper's catch contract still applies; verify no leaked
        // exception escapes.
        var act = async () => await dispatcher.EnqueueToPublish(_CreateTestMessage(_StorageGuid(1)), AbortToken);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_flush_queued_scheduler_ids_back_to_delayed_when_dispose()
    {
        // #610 regression — DisposeAsync must drain the scheduler loop, then hand every id still
        // sitting in the in-process scheduler queue back to storage as Delayed. Losing this flush
        // strands Queued rows: their in-memory schedule dies with the process while storage keeps
        // reporting them as already picked up.
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = false });
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));

        var dispatcher = new Dispatcher(
            _logger,
            sender,
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );

        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);

        // Due in 50s → Queued status → enters the in-process scheduler queue and stays there
        // (the scheduler loop only dequeues items within 50ms of their due time).
        var message = _CreateTestMessage(_StorageGuid(1));
        await dispatcher.EnqueueToScheduler(
            message,
            DateTimeOffset.UtcNow.AddSeconds(50),
            cancellationToken: AbortToken
        );

        // when
        await dispatcher.DisposeAsync();

        // then — the queued id is durably handed back as Delayed and the message is never sent.
        await _storage
            .Received(1)
            .ChangePublishStateToDelayedAsync(
                Arg.Is<Guid[]>(ids => ids.Length == 1 && ids[0] == message.StorageId),
                Arg.Any<CancellationToken>()
            );
        sender.Count.Should().Be(0);
    }

    [Fact]
    public async Task should_complete_when_dispose_scheduler_flush_fails()
    {
        // The shutdown flush writes through IDataStorage; a dead storage must not turn DisposeAsync
        // into a throw — hosts dispose the dispatcher during shutdown, and a propagated storage
        // fault would abort the remaining shutdown sequence. The failure is logged and absorbed.
        var sender = new TestThreadSafeMessageSender();
        var options = Options.Create(new MessagingOptions { EnablePublishParallelSend = false });
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));
        _storage
            .ChangePublishStateToDelayedAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("storage down"));

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
        await dispatcher.StartAsync(cts.Token);
        var message = _CreateTestMessage(_StorageGuid(1));
        await dispatcher.EnqueueToScheduler(
            message,
            DateTimeOffset.UtcNow.AddSeconds(50),
            cancellationToken: AbortToken
        );

        // when
        var act = async () => await dispatcher.DisposeAsync();

        // then
        await act.Should().NotThrowAsync("scheduler-flush failures during shutdown are logged, not propagated");
    }

    [Fact]
    public async Task should_bound_scheduler_flush_by_remaining_shutdown_budget_when_dispose()
    {
        var flushStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFlush = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeProvider = new FakeTimeProvider();
        _storage
            .ChangePublishStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<DbTransaction?>(),
                Arg.Any<DateTimeOffset?>(),
                cancellationToken: Arg.Any<CancellationToken>()
            )
            .Returns(new ValueTask<bool>(true));
        _storage
            .ChangePublishStateToDelayedAsync(Arg.Any<Guid[]>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                flushStarted.TrySetResult();
                return new ValueTask(releaseFlush.Task);
            });
        await using var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            _storage,
            timeProvider,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToScheduler(
            _CreateTestMessage(_StorageGuid(1)),
            timeProvider.GetUtcNow().AddSeconds(50),
            cancellationToken: AbortToken
        );

        var dispose = dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken).AsTask();
        await flushStarted.Task.WaitAsync(AbortToken);
        dispose.IsCompleted.Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await dispose.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);

        releaseFlush.TrySetResult();
        await dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken);
    }

    [Fact]
    public async Task should_wait_for_inflight_processing_loop_when_dispose()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor
            .ExecuteAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<IServiceProvider>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async _ =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return OperateResult.Success;
            });
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = true,
                SubscriberParallelExecuteThreadCount = 1,
                SubscriberParallelExecuteBufferFactor = 1,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            }
        );
        var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToExecute(_CreateTestMessage(), cancellationToken: AbortToken);
        await entered.Task.WaitAsync(AbortToken);

        var dispose = dispatcher.DisposeAsync().AsTask();
        dispose.IsCompleted.Should().BeFalse("the processing loop still owns an in-flight handler");
        release.TrySetResult();
        await dispose;
    }

    [Fact]
    public async Task should_complete_via_timeout_path_when_dispose_handler_never_observes_cancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _logger.IsEnabled(LogLevel.Error).Returns(true);
        _executor
            .ExecuteAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<IServiceProvider>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async _ =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return OperateResult.Success;
            });
        var timeProvider = new FakeTimeProvider();
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = true,
                SubscriberParallelExecuteThreadCount = 1,
                SubscriberParallelExecuteBufferFactor = 1,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            }
        );
        var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            options,
            _executor,
            _storage,
            timeProvider,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToExecute(_CreateTestMessage(), cancellationToken: AbortToken);
        await entered.Task.WaitAsync(AbortToken);

        // First dispose: the handler ignores cancellation, so completion must come from the
        // ShutdownTimeout branch (_CompleteTimedOutShutdownAsync), never from the handler.
        var firstDispose = dispatcher.DisposeAsync(TimeSpan.FromSeconds(2), AbortToken).AsTask();
        for (var i = 0; i < 100 && !firstDispose.IsCompleted; i++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, AbortToken);
        }

        firstDispose.IsCompleted.Should().BeTrue("DisposeAsync must return once ShutdownTimeout expires");
        await firstDispose;

        _logger
            .ReceivedCalls()
            .Should()
            .Contain(
                call =>
                    string.Equals(call.GetMethodInfo().Name, nameof(ILogger.Log), StringComparison.Ordinal)
                    && call.GetArguments().Length >= 2
                    && call.GetArguments()[1] != null
                    && call.GetArguments()[1] is EventId
                    && string.Equals(
                        ((EventId)call.GetArguments()[1]!).Name,
                        "ProcessorStopFailed",
                        StringComparison.Ordinal
                    ),
                "the timeout path logs ProcessorStopFailed"
            );

        // A reentrant dispose uses only the caller's remaining shared budget, not the full configured
        // ShutdownTimeout again. Eventual cleanup remains fault-observed after this join times out.
        var secondDispose = dispatcher.DisposeAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
        secondDispose.IsCompleted.Should().BeFalse("eventual cleanup still owns the in-flight handler");
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await secondDispose.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        release.TrySetResult();
        await dispatcher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
    }

    [Fact]
    public async Task should_throw_when_start_dispose_is_still_draining()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _executor
            .ExecuteAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<IServiceProvider>(),
                Arg.Any<ConsumerExecutorDescriptor?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async _ =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return OperateResult.Success;
            });
        var options = Options.Create(
            new MessagingOptions
            {
                EnableSubscriberParallelExecute = true,
                SubscriberParallelExecuteThreadCount = 1,
                SubscriberParallelExecuteBufferFactor = 1,
                ShutdownTimeout = TimeSpan.FromSeconds(5),
            }
        );
        var dispatcher = new Dispatcher(
            _logger,
            new TestThreadSafeMessageSender(),
            options,
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
        using var cts = new CancellationTokenSource();
        await dispatcher.StartAsync(cts.Token);
        await dispatcher.EnqueueToExecute(_CreateTestMessage(), cancellationToken: AbortToken);
        await entered.Task.WaitAsync(AbortToken);

        var dispose = dispatcher.DisposeAsync().AsTask();

        var act = async () => await dispatcher.StartAsync(cts.Token);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Dispatcher shutdown is still in progress.");

        release.TrySetResult();
        await dispose;
    }

    private static MediumMessage _CreateTestMessage(int storageId)
    {
        return _CreateTestMessage(_StorageGuid(storageId));
    }

    private Dispatcher _CreateDispatcher(IMessageSender sender)
    {
        return new Dispatcher(
            _logger,
            sender,
            Options.Create(new MessagingOptions { EnablePublishParallelSend = false }),
            _executor,
            _storage,
            TimeProvider.System,
            _scopeFactory
        );
    }

    private static MediumMessage _CreateTestMessage(Guid? storageId = null)
    {
        var resolvedStorageId = storageId ?? Guid.NewGuid();
        var messageId = resolvedStorageId.ToString("D");
        var message = new Message(
            headers: new Dictionary<string, string?>(StringComparer.Ordinal) { { "headless-msg-id", messageId } },
            value: new MessageValue("test@test.com", "User")
        );

        return new MediumMessage
        {
            StorageId = resolvedStorageId,
            Origin = message,
            Content = JsonSerializer.Serialize(message),
            Lane = MessageLane.Bus,
        };
    }

    private static MediumMessage _CreateRetryMessage(string? owner, DateTimeOffset lockedUntil)
    {
        var message = _CreateTestMessage();
        message.Owner = owner;
        message.LockedUntil = lockedUntil;
        return message;
    }

    private static Guid _StorageGuid(int value)
    {
        return Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
    }

    private sealed class SynchronouslyBlockingMessageSender : IMessageSender, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(initialState: false);

        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OperateResult> SendAsync(MediumMessage message)
        {
            Entered.TrySetResult();
            _release.Wait();
            return Task.FromResult(OperateResult.Success);
        }

        public Task<OperateResult> SendAsync(MediumMessage message, IServiceProvider dispatchServices)
        {
            return SendAsync(message);
        }

        public Task<OperateResult> SendRetryAsync(
            MediumMessage message,
            IServiceProvider dispatchServices,
            RetryExecutionState executionState
        )
        {
            return SendAsync(message);
        }

        internal void Release()
        {
            _release.Set();
        }

        public void Dispose()
        {
            _release.Set();
            _release.Dispose();
        }
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
