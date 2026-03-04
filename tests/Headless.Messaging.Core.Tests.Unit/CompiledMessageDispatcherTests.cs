// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class CompiledMessageDispatcherTests : TestBase
{
    [Fact]
    public async Task should_set_correlation_scope_during_consumer_execution()
    {
        // given
        MessagingCorrelationScope? capturedScope = null;
        var (sut, handler) = _CreateSut();
        handler
            .Consume(Arg.Any<ConsumeContext<TestMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                capturedScope = MessagingCorrelationScope.Current;
                return ValueTask.CompletedTask;
            });

        var context = _CreateContext(correlationId: "test-correlation-id");

        // when
        await sut.DispatchAsync(context, AbortToken);

        // then
        capturedScope.Should().NotBeNull();
        capturedScope!.CorrelationId.Should().Be("test-correlation-id");
    }

    [Fact]
    public async Task should_use_message_id_as_correlation_when_correlation_id_is_null()
    {
        // given
        MessagingCorrelationScope? capturedScope = null;
        var (sut, handler) = _CreateSut();
        handler
            .Consume(Arg.Any<ConsumeContext<TestMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                capturedScope = MessagingCorrelationScope.Current;
                return ValueTask.CompletedTask;
            });

        var context = _CreateContext(correlationId: null, messageId: "msg-123");

        // when
        await sut.DispatchAsync(context, AbortToken);

        // then
        capturedScope.Should().NotBeNull();
        capturedScope!.CorrelationId.Should().Be("msg-123");
    }

    [Fact]
    public async Task should_increment_correlation_sequence_on_multiple_calls()
    {
        // given
        var sequences = new List<int>();
        var (sut, handler) = _CreateSut();
        handler
            .Consume(Arg.Any<ConsumeContext<TestMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var scope = MessagingCorrelationScope.Current!;
                sequences.Add(scope.IncrementSequence());
                sequences.Add(scope.IncrementSequence());
                sequences.Add(scope.IncrementSequence());
                return ValueTask.CompletedTask;
            });

        var context = _CreateContext();

        // when
        await sut.DispatchAsync(context, AbortToken);

        // then
        sequences.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task should_dispose_correlation_scope_after_consumer_completes()
    {
        // given
        var (sut, _) = _CreateSut();
        var context = _CreateContext();

        // when
        await sut.DispatchAsync(context, AbortToken);

        // then
        MessagingCorrelationScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_dispose_correlation_scope_when_consumer_throws()
    {
        // given
        var (sut, handler) = _CreateSut();
        handler
            .Consume(Arg.Any<ConsumeContext<TestMessage>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw new InvalidOperationException("consumer failure"));

        var context = _CreateContext();

        // when
        var act = () => sut.DispatchAsync(context, AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
        MessagingCorrelationScope.Current.Should().BeNull();
    }

    [Fact]
    public async Task should_set_scope_even_when_explicit_correlation_header_present()
    {
        // given — The scope is always set by CompiledMessageDispatcher.
        // OutboxPublisher separately checks headers.ContainsKey(Headers.CorrelationId)
        // before reading the scope, so explicit headers win at publish time.
        MessagingCorrelationScope? capturedScope = null;
        var (sut, handler) = _CreateSut();
        handler
            .Consume(Arg.Any<ConsumeContext<TestMessage>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                capturedScope = MessagingCorrelationScope.Current;
                return ValueTask.CompletedTask;
            });

        var context = _CreateContext(correlationId: "explicit-correlation");

        // when
        await sut.DispatchAsync(context, AbortToken);

        // then — scope IS set; explicit header override is handled by OutboxPublisher
        capturedScope.Should().NotBeNull();
        capturedScope!.CorrelationId.Should().Be("explicit-correlation");
    }

    // -- helpers --

    private static (CompiledMessageDispatcher Sut, IConsume<TestMessage> Handler) _CreateSut()
    {
        var handler = Substitute.For<IConsume<TestMessage>>();
        var services = new ServiceCollection();
        services.AddSingleton(handler);
        var sp = services.BuildServiceProvider();
        var sut = new CompiledMessageDispatcher(sp.GetRequiredService<IServiceScopeFactory>());
        return (sut, handler);
    }

    private static ConsumeContext<TestMessage> _CreateContext(
        string? correlationId = "test-correlation-id",
        string messageId = "test-message-id"
    )
    {
        return new ConsumeContext<TestMessage>
        {
            Message = new TestMessage(),
            MessageId = messageId,
            CorrelationId = correlationId,
            Topic = "test-topic",
            Timestamp = DateTimeOffset.UtcNow,
            Headers = new MessageHeader(new Dictionary<string, string?>(StringComparer.Ordinal)),
        };
    }
}

public sealed class TestMessage;
