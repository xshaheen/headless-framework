// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.AmbientTransactions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Internal;

/// <summary>
/// Tests for F9 — <see cref="PublishingContext{TMessage}.IsTransactional"/>: surfaces the transactional
/// boundary as a typed contract so post-success middleware can detect when a publish is enrolled
/// in an ambient outbox transaction whose commit is the caller's responsibility.
/// </summary>
public sealed class IsTransactionalPropagationTests : TestBase
{
    private sealed record TestMessage(string Value);

    [Fact]
    public async Task should_set_IsTransactional_false_for_direct_publisher()
    {
        // given
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishMiddlewareFor<IsTransactionalCapturingMiddleware, TestMessage>();
        var pipeline = _BuildPublishPipeline(services);

        var transport = new RecordingTransport();
        var options = new MessagingOptions { MessageNameMappings = { [typeof(TestMessage)] = "test.messageName" } };
        var optionsAccessor = Options.Create(options);
        var serializer = new JsonUtf8Serializer(optionsAccessor);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            new NullCurrentTenant()
        );
        var publisher = new Bus(serializer, transport, publishRequestFactory, pipeline, TimeProvider.System);

        // when
        await publisher.PublishAsync(new TestMessage("hi"), options: null, cancellationToken: AbortToken);

        // then — Bus always commits to the wire; rollback has no semantic
        observed.Captured.Should().BeFalse();
    }

    [Fact]
    public async Task should_set_IsTransactional_true_when_outbox_publisher_is_non_autocommit()
    {
        // given — non-AutoCommit ambient transaction: the publish is buffered into the outbox
        // and waits for the caller to commit. Post-success middleware should see
        // IsTransactional = true so they can defer durable side-effects until after commit.
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishMiddlewareFor<IsTransactionalCapturingMiddleware, TestMessage>();
        var pipeline = _BuildPublishPipeline(services);

        var (publisher, _) = _BuildOutboxMessageWriter(pipeline, autoCommit: false, ambientTransaction: true);

        // when
        await publisher.PublishAsync(
            new TestMessage("hi"),
            options: null,
            intentType: IntentType.Bus,
            cancellationToken: AbortToken
        );

        // then — post-success middleware saw the transactional flag
        observed.Captured.Should().BeTrue();
    }

    [Fact]
    public async Task should_set_IsTransactional_false_when_outbox_publisher_is_autocommit()
    {
        // given — AutoCommit branch: the publisher commits inside the call, so for downstream
        // middleware there is no caller-driven rollback to worry about; flag stays false.
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishMiddlewareFor<IsTransactionalCapturingMiddleware, TestMessage>();
        var pipeline = _BuildPublishPipeline(services);

        var (publisher, _) = _BuildOutboxMessageWriter(pipeline, autoCommit: true, ambientTransaction: true);

        // when
        await publisher.PublishAsync(
            new TestMessage("hi"),
            options: null,
            intentType: IntentType.Bus,
            cancellationToken: AbortToken
        );

        // then
        observed.Captured.Should().BeFalse();
    }

    [Fact]
    public async Task should_set_IsTransactional_false_when_outbox_publisher_has_no_ambient_transaction()
    {
        // given — no ambient transaction: publishes go straight to the dispatcher,
        // so there is no caller-driven rollback semantic.
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishMiddlewareFor<IsTransactionalCapturingMiddleware, TestMessage>();
        var pipeline = _BuildPublishPipeline(services);

        var (publisher, _) = _BuildOutboxMessageWriter(pipeline, autoCommit: false, ambientTransaction: false);

        // when
        await publisher.PublishAsync(
            new TestMessage("hi"),
            options: null,
            intentType: IntentType.Bus,
            cancellationToken: AbortToken
        );

        // then
        observed.Captured.Should().BeFalse();
    }

    private static (OutboxMessageWriter publisher, TestAmbientTransaction? tx) _BuildOutboxMessageWriter(
        IPublishMiddlewarePipeline pipeline,
        bool autoCommit,
        bool ambientTransaction
    )
    {
        var options = new MessagingOptions { MessageNameMappings = { [typeof(TestMessage)] = "test.messageName" } };
        var optionsAccessor = Options.Create(options);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            new NullCurrentTenant()
        );

        var storage = Substitute.For<IDataStorage>();
        storage
            .StoreMessageAsync(Arg.Any<string>(), Arg.Any<MediumMessage>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var content = (MediumMessage)call[1];
                return ValueTask.FromResult(
                    new MediumMessage
                    {
                        StorageId = Guid.NewGuid(),
                        Origin = content.Origin,
                        Content = content.Content,
                        IntentType = content.IntentType,
                        Added = DateTime.UtcNow,
                    }
                );
            });

        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher
            .EnqueueToPublish(Arg.Any<MediumMessage>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        dispatcher
            .EnqueueToScheduler(
                Arg.Any<MediumMessage>(),
                Arg.Any<DateTime>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        TestAmbientTransaction? tx = null;
        var currentAmbientTransaction = new InMemoryCurrentAmbientTransaction();
        if (ambientTransaction)
        {
            tx = new TestAmbientTransaction { DbTransaction = new object(), AutoCommit = autoCommit };
            currentAmbientTransaction.Current = tx;
        }

        var outbox = new OutboxMessageWriter(
            storage,
            dispatcher,
            publishRequestFactory,
            currentAmbientTransaction,
            [],
            pipeline,
            TimeProvider.System
        );
        return (outbox, tx);
    }

    private static PublishMiddlewarePipeline _BuildPublishPipeline(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        return new PublishMiddlewarePipeline(provider, provider.GetService<IMiddlewareDescriptorRegistry>());
    }

    private sealed class IsTransactionalCapturingMiddleware(TransactionalCapture capture)
        : IPublishMiddleware<PublishingContext<TestMessage>>
    {
        public async ValueTask InvokeAsync(PublishingContext<TestMessage> context, Func<ValueTask> next)
        {
            await next().ConfigureAwait(false);
            capture.Captured = context.IsTransactional;
        }
    }
}

internal sealed class TransactionalCapture
{
    public bool Captured { get; set; }
}

internal sealed class RecordingTransport : IBusTransport
{
    private readonly ConcurrentBag<TransportMessage> _sent = [];

    public BrokerAddress BrokerAddress { get; } = new("Test", "localhost");

    public IReadOnlyList<TransportMessage> Sent => [.. _sent];

    public Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Add(message);
        return Task.FromResult(OperateResult.Success);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class InMemoryCurrentAmbientTransaction : ICurrentAmbientTransaction
{
    public IAmbientTransaction? Current { get; set; }
}

internal sealed class TestAmbientTransaction : IAmbientTransaction
{
    private readonly List<Func<CancellationToken, ValueTask>> _commitWork = [];

    public bool AutoCommit { get; set; }

    public object? DbTransaction { get; set; }

    public void RegisterCommitWork(Func<CancellationToken, ValueTask> drain)
    {
        _commitWork.Add(drain);
    }

    public void CompleteExternally()
    {
        Commit();
    }

    public void Commit() { }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        foreach (var drain in _commitWork)
        {
            await drain(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Rollback() { }

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
