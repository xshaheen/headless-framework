// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Headless.Abstractions;
using Headless.CommitCoordination;
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

namespace Tests.Internal;

/// <summary>
/// Tests for F9 — <see cref="PublishContext{TMessage}.IsTransactional"/>: surfaces the transactional
/// boundary as a typed contract so post-success middleware can detect when a publish is enlisted on an
/// ambient commit coordinator whose relational commit drives outbox dispatch post-commit.
/// </summary>
public sealed class IsTransactionalPropagationTests : TestBase
{
    private sealed record TestMessage(string Value);

    [Fact]
    public async Task should_set_is_transactional_false_for_direct_publisher()
    {
        // given
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishMiddlewareFor<IsTransactionalCapturingMiddleware, TestMessage>();
        var pipeline = _BuildPublishPipeline(services);

        await using var transport = new RecordingTransport();
        var options = new MessagingOptions();
        var registry = _CreateRegistry();
        var optionsAccessor = Options.Create(options);
        var serializer = new JsonUtf8Serializer(optionsAccessor);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            registry,
            new NullCurrentTenant()
        );
        var publisher = new Bus(serializer, transport, publishRequestFactory, pipeline, TimeProvider.System);

        // when
        await publisher.PublishAsync(new TestMessage("hi"), options: null, cancellationToken: AbortToken);

        // then — Bus always commits to the wire; rollback has no semantic
        observed.Captured.Should().BeFalse();
    }

    [Fact]
    public async Task should_set_is_transactional_true_when_coordinator_has_relational_transaction()
    {
        // given — an ambient commit coordinator exposes a relational transaction: the publish is buffered
        // into the outbox and waits for the coordinator's commit. Post-success middleware should see
        // IsTransactional = true so it can defer durable side-effects until after commit.
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishMiddlewareFor<IsTransactionalCapturingMiddleware, TestMessage>();
        var pipeline = _BuildPublishPipeline(services);

        await using var transaction = new TestDbTransaction();
        var stack = new CommitScopeStack();
        var scope = new CommitScopeFactory(stack).Begin(
            new EmptyServiceProvider(),
            [new RelationalCommitContext(() => null, () => transaction)]
        );

        await using (scope)
        {
            var publisher = _BuildOutboxMessageWriter(pipeline, stack);

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
    }

    [Fact]
    public async Task should_set_is_transactional_false_when_no_ambient_coordinator()
    {
        // given — no ambient coordinator: publishes go straight to the dispatcher, so there is no
        // commit-driven rollback semantic.
        var observed = new TransactionalCapture();
        var services = new ServiceCollection();
        services.AddSingleton(observed);
        new MessagingBuilder(services).AddPublishMiddlewareFor<IsTransactionalCapturingMiddleware, TestMessage>();
        var pipeline = _BuildPublishPipeline(services);

        var publisher = _BuildOutboxMessageWriter(pipeline, new MessagingNullCommitCoordinator());

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

    private static OutboxMessageWriter _BuildOutboxMessageWriter(
        IPublishMiddlewarePipeline pipeline,
        ICurrentCommitCoordinator currentCommitCoordinator
    )
    {
        var options = new MessagingOptions();
        var registry = _CreateRegistry();
        var optionsAccessor = Options.Create(options);
        var publishRequestFactory = new MessagePublishRequestFactory(
            new SequentialGuidGenerator(SequentialGuidType.SqlServer),
            TimeProvider.System,
            optionsAccessor,
            registry,
            new NullCurrentTenant()
        );

        var storage = Substitute.For<IDataStorage>();
        storage
            .StoreMessageAsync(
                Arg.Any<string>(),
                Arg.Any<MediumMessage>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var content = ((MediumMessage)call[1]).Origin;
                return ValueTask.FromResult(
                    new MediumMessage
                    {
                        StorageId = Guid.NewGuid(),
                        Origin = content,
                        Content = "{}",
                        IntentType = IntentType.Bus,
                        Added = DateTimeOffset.UtcNow,
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
                Arg.Any<DateTimeOffset>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        return new OutboxMessageWriter(
            storage,
            dispatcher,
            publishRequestFactory,
            currentCommitCoordinator,
            pipeline,
            TimeProvider.System,
            Options.Create(new MessagingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageOutboxBuffer>.Instance
        );
    }

    private static ConsumerRegistry _CreateRegistry()
    {
        var registry = new ConsumerRegistry();
        registry.RegisterMessageName(typeof(TestMessage), "test.messageName");

        return registry;
    }

    private static PublishMiddlewarePipeline _BuildPublishPipeline(ServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        return new PublishMiddlewarePipeline(provider, provider.GetService<IMiddlewareDescriptorRegistry>());
    }

    private sealed class IsTransactionalCapturingMiddleware(TransactionalCapture capture)
        : IPublishMiddleware<PublishContext<TestMessage>>
    {
        public async ValueTask InvokeAsync(PublishContext<TestMessage> context, Func<ValueTask> next)
        {
            await next().ConfigureAwait(false);
            capture.Captured = context.IsTransactional;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestDbTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection? DbConnection => null;

        public override void Commit() { }

        public override void Rollback() { }
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
