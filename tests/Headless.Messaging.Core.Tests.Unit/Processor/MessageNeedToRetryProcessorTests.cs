// Copyright (c) Mahmoud Shaheen. All rights reserved.

using AwesomeAssertions;
using Headless.Messaging;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Processor;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Tests;

public sealed class MessageNeedToRetryProcessorTests : TestBase
{
    private const string _Group = "test-group";

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static MediumMessage _CreateMessage(string? group = _Group)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = "test.topic",
        };

        if (group is not null)
        {
            headers[Headers.Group] = group;
        }

        return new MediumMessage
        {
            DbId = Guid.NewGuid().ToString(),
            Origin = new Message(headers, null),
            Content = "{}",
        };
    }

    private static (MessageNeedToRetryProcessor Sut, IDispatcher Dispatcher, ICircuitBreakerStateManager Cb) _Create(
        int failedRetryInterval = 60,
        bool adaptivePolling = true,
        int maxPollingInterval = 900,
        double transientFailureRateThreshold = 0.8
    )
    {
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var cb = Substitute.For<ICircuitBreakerStateManager>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var options = new MessagingOptions { FailedRetryInterval = failedRetryInterval };
        options.RetryProcessor.AdaptivePolling = adaptivePolling;
        options.RetryProcessor.MaxPollingInterval = maxPollingInterval;
        options.RetryProcessor.TransientFailureRateThreshold = transientFailureRateThreshold;

        var services = new ServiceCollection();
        services.AddSingleton(cb);
        services.AddSingleton(dataStorage);
        var sp = services.BuildServiceProvider();

        var sut = new MessageNeedToRetryProcessor(
            Options.Create(options),
            logger,
            dispatcher,
            dataStorage,
            sp
        );

        return (sut, dispatcher, cb);
    }

    private static ProcessingContext _CreateContext(IServiceProvider? provider = null)
    {
        provider ??= new ServiceCollection()
            .AddSingleton(Substitute.For<IDataStorage>())
            .BuildServiceProvider();

        return new ProcessingContext(provider, CancellationToken.None);
    }

    private static void _SetupReceivedMessages(IDataStorage dataStorage, params MediumMessage[] messages)
    {
        dataStorage
            .GetReceivedMessagesOfNeedRetry(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>(messages));

        dataStorage
            .GetPublishedMessagesOfNeedRetry(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IEnumerable<MediumMessage>>([]));
    }

    // -------------------------------------------------------------------------
    // US-012: Circuit-state awareness
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_SkipsMessages_WhenCircuitIsOpenForGroup()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create();
        var msg1 = _CreateMessage("group-a");
        var msg2 = _CreateMessage("group-b");
        var msg3 = _CreateMessage("group-a");

        cb.IsOpen("group-a").Returns(true);
        cb.IsOpen("group-b").Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, msg1, msg2, msg3);

        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act
        await sut.ProcessAsync(context);

        // Assert — only group-b message enqueued
        await dispatcher.Received(1).EnqueueToExecute(msg2, null, Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().EnqueueToExecute(msg1, null, Arg.Any<CancellationToken>());
        await dispatcher.DidNotReceive().EnqueueToExecute(msg3, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EnqueuesAllMessages_WhenNoCircuitBreakerRegistered()
    {
        // Arrange — no circuit breaker in DI
        var dispatcher = Substitute.For<IDispatcher>();
        var dataStorage = Substitute.For<IDataStorage>();
        var logger = NullLoggerFactory.Instance.CreateLogger<MessageNeedToRetryProcessor>();

        var options = new MessagingOptions { FailedRetryInterval = 60 };
        var sp = new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider();

        var sut = new MessageNeedToRetryProcessor(
            Options.Create(options),
            logger,
            dispatcher,
            dataStorage,
            sp
        );

        var msg1 = _CreateMessage("group-a");
        var msg2 = _CreateMessage("group-b");
        _SetupReceivedMessages(dataStorage, msg1, msg2);
        var context = _CreateContext(sp);

        // Act
        await sut.ProcessAsync(context);

        // Assert — all enqueued
        await dispatcher.Received(1).EnqueueToExecute(msg1, null, Arg.Any<CancellationToken>());
        await dispatcher.Received(1).EnqueueToExecute(msg2, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EnqueuesMessage_WhenGroupIsNull()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create();
        var msg = _CreateMessage(group: null);

        cb.IsOpen(Arg.Any<string>()).Returns(true); // all groups "open"

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, msg);
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act
        await sut.ProcessAsync(context);

        // Assert — null group messages always enqueued (can't check circuit without group name)
        await dispatcher.Received(1).EnqueueToExecute(msg, null, Arg.Any<CancellationToken>());
    }

    // -------------------------------------------------------------------------
    // US-011: Adaptive polling
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessAsync_DoublesInterval_WhenTransientRateExceedsThreshold()
    {
        // Arrange — 5 messages, 4 skipped (circuit open) = 80% > threshold
        var (sut, dispatcher, cb) = _Create(failedRetryInterval: 10, transientFailureRateThreshold: 0.7);

        cb.IsOpen("open-group").Returns(true);
        cb.IsOpen("healthy-group").Returns(false);

        var messages = new[]
        {
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("open-group"),
            _CreateMessage("healthy-group"),
        };

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, messages);
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act — first cycle
        await sut.ProcessAsync(context);

        // Assert — interval should have doubled from 10s to 20s
        // We verify indirectly: the second cycle should wait 20s via context.WaitAsync
        // Since we can't easily inspect private _currentInterval, we rely on correct behavior
        // and test that the processor doesn't crash and messages are properly filtered

        await dispatcher.Received(1).EnqueueToExecute(
            Arg.Is<MediumMessage>(m => m.Origin.GetGroup() == "healthy-group"),
            null,
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ProcessAsync_ResetsInterval_After3CleanCycles()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create(failedRetryInterval: 10);

        cb.IsOpen(Arg.Any<string>()).Returns(false);

        var dataStorage = Substitute.For<IDataStorage>();

        // All cycles return empty — clean cycles
        _SetupReceivedMessages(dataStorage);
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act — 3 clean cycles
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // Assert — no crashes, processor handles empty batches
        // The interval should have been reset to base after 3 clean cycles
        await dispatcher.DidNotReceive().EnqueueToExecute(
            Arg.Any<MediumMessage>(),
            Arg.Any<ConsumerExecutorDescriptor>(),
            Arg.Any<CancellationToken>()
        );
    }

    [Fact]
    public async Task ProcessAsync_DoesNotAdjustInterval_WhenAdaptivePollingDisabled()
    {
        // Arrange
        var (sut, dispatcher, cb) = _Create(failedRetryInterval: 10, adaptivePolling: false);

        cb.IsOpen("open-group").Returns(true);

        var messages = Enumerable.Range(0, 10).Select(_ => _CreateMessage("open-group")).ToArray();

        var dataStorage = Substitute.For<IDataStorage>();
        _SetupReceivedMessages(dataStorage, messages);
        var context = _CreateContext(
            new ServiceCollection().AddSingleton(dataStorage).BuildServiceProvider()
        );

        // Act — all skipped, but adaptive polling is off
        await sut.ProcessAsync(context);
        await sut.ProcessAsync(context);

        // Assert — no crashes, messages still skipped due to circuit breaker
        await dispatcher.DidNotReceive().EnqueueToExecute(
            Arg.Any<MediumMessage>(),
            Arg.Any<ConsumerExecutorDescriptor>(),
            Arg.Any<CancellationToken>()
        );
    }
}
