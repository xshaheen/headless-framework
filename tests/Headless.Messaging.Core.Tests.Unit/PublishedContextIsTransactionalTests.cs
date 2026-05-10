// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transactions;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Tests for F9 — <see cref="PublishedContext.IsTransactional"/>: surfaces the transactional
/// boundary as a typed contract so post-success filters can detect when a publish is enrolled
/// in an ambient outbox transaction whose commit is the caller's responsibility.
/// </summary>
public sealed class PublishedContextIsTransactionalTests : TestBase
{
    private sealed record TestMessage(string Value);

    [Fact]
    public async Task should_set_IsTransactional_false_for_direct_publisher()
    {
        // given
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishFilter<IsTransactionalCapturingFilter>();
        var pipeline = new PublishExecutionPipeline(services.BuildServiceProvider());

        var transport = new RecordingTransport();
        var options = new MessagingOptions { TopicMappings = { [typeof(TestMessage)] = "test.topic" } };
        var optionsAccessor = Options.Create(options);
        var serializer = new JsonUtf8Serializer(optionsAccessor);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SnowflakeIdLongIdGenerator(),
            TimeProvider.System,
            optionsAccessor,
            new NullCurrentTenant()
        );
        var publisher = new DirectPublisher(serializer, transport, publishRequestFactory, pipeline);

        // when
        await publisher.PublishAsync(new TestMessage("hi"), cancellationToken: AbortToken);

        // then — DirectPublisher always commits to the wire; rollback has no semantic
        observed.Captured.Should().BeFalse();
    }

    [Fact]
    public async Task should_set_IsTransactional_true_when_outbox_publisher_is_non_autocommit()
    {
        // given — non-AutoCommit ambient transaction: the publish is buffered into the outbox
        // and waits for the caller to commit. Filters running OnPublishExecutedAsync should see
        // IsTransactional = true so they can defer durable side-effects until after commit.
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishFilter<IsTransactionalCapturingFilter>();
        var pipeline = new PublishExecutionPipeline(services.BuildServiceProvider());

        var (publisher, _) = _BuildOutboxPublisher(pipeline, autoCommit: false, ambientTransaction: true);

        // when
        await publisher.PublishAsync(new TestMessage("hi"), cancellationToken: AbortToken);

        // then — the executed-phase filter saw the transactional flag
        observed.Captured.Should().BeTrue();
    }

    [Fact]
    public async Task should_set_IsTransactional_false_when_outbox_publisher_is_autocommit()
    {
        // given — AutoCommit branch: the publisher commits inside the call, so for downstream
        // filters there is no caller-driven rollback to worry about; flag stays false.
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishFilter<IsTransactionalCapturingFilter>();
        var pipeline = new PublishExecutionPipeline(services.BuildServiceProvider());

        var (publisher, _) = _BuildOutboxPublisher(pipeline, autoCommit: true, ambientTransaction: true);

        // when
        await publisher.PublishAsync(new TestMessage("hi"), cancellationToken: AbortToken);

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
        new MessagingBuilder(services).AddPublishFilter<IsTransactionalCapturingFilter>();
        var pipeline = new PublishExecutionPipeline(services.BuildServiceProvider());

        var (publisher, _) = _BuildOutboxPublisher(pipeline, autoCommit: false, ambientTransaction: false);

        // when
        await publisher.PublishAsync(new TestMessage("hi"), cancellationToken: AbortToken);

        // then
        observed.Captured.Should().BeFalse();
    }

    private static (OutboxPublisher publisher, TestOutboxTransaction? tx) _BuildOutboxPublisher(
        PublishExecutionPipeline pipeline,
        bool autoCommit,
        bool ambientTransaction
    )
    {
        var options = new MessagingOptions { TopicMappings = { [typeof(TestMessage)] = "test.topic" } };
        var optionsAccessor = Options.Create(options);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SnowflakeIdLongIdGenerator(),
            TimeProvider.System,
            optionsAccessor,
            new NullCurrentTenant()
        );

        var storage = Substitute.For<IDataStorage>();
        storage
            .StoreMessageAsync(Arg.Any<string>(), Arg.Any<Message>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var content = (Message)call[1];
                return ValueTask.FromResult(
                    new MediumMessage
                    {
                        StorageId = 1L,
                        Origin = content,
                        Content = "{}",
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

        TestOutboxTransaction? tx = null;
        var accessor = new InMemoryOutboxTransactionAccessor();
        if (ambientTransaction)
        {
            tx = new TestOutboxTransaction { DbTransaction = new object(), AutoCommit = autoCommit };
            accessor.Current = tx;
        }

        var outbox = new OutboxPublisher(storage, dispatcher, publishRequestFactory, accessor, pipeline);
        return (outbox, tx);
    }
}

internal sealed class TransactionalCapture
{
    public bool Captured { get; set; }
}

internal sealed class IsTransactionalCapturingFilter(TransactionalCapture capture) : PublishFilter
{
    public override ValueTask OnPublishExecutedAsync(
        PublishedContext context,
        CancellationToken cancellationToken = default
    )
    {
        capture.Captured = context.IsTransactional;
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingTransport : ITransport
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

internal sealed class InMemoryOutboxTransactionAccessor : IOutboxTransactionAccessor
{
    public IOutboxTransaction? Current { get; set; }
}

internal sealed class TestOutboxTransaction : IOutboxTransaction, IOutboxMessageBuffer
{
    public bool AutoCommit { get; set; }

    public object? DbTransaction { get; set; }

    public void AddToSent(MediumMessage message) { }

    public void Commit() { }

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Rollback() { }

    public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
