// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Testing.Tests;
using Headless.Messaging.Messages;

namespace Tests;

public sealed class ConsumerContextTests : TestBase
{
    [Fact]
    public void should_create_context_from_descriptor_and_message()
    {
        // given
        var descriptor = _CreateDescriptor();
        var mediumMessage = _CreateMediumMessage();

        // when
        var context = new ConsumerContext(descriptor, mediumMessage);

        // then
        context.ConsumerDescriptor.Should().BeSameAs(descriptor);
        context.MediumMessage.Should().BeSameAs(mediumMessage);
    }

    [Fact]
    public void should_expose_deliver_message_from_medium_message()
    {
        // given
        var descriptor = _CreateDescriptor();
        var mediumMessage = _CreateMediumMessage();

        // when
        var context = new ConsumerContext(descriptor, mediumMessage);

        // then
        context.DeliverMessage.Should().BeSameAs(mediumMessage.Origin);
    }

    [Fact]
    public void should_create_context_from_another_context()
    {
        // given
        var descriptor = _CreateDescriptor();
        var mediumMessage = _CreateMediumMessage();
        var originalContext = new ConsumerContext(descriptor, mediumMessage);

        // when
        var copiedContext = new ConsumerContext(originalContext);

        // then
        copiedContext.ConsumerDescriptor.Should().BeSameAs(originalContext.ConsumerDescriptor);
        copiedContext.MediumMessage.Should().BeSameAs(originalContext.MediumMessage);
    }

    [Fact]
    public void should_throw_when_descriptor_is_null()
    {
        // given
        var mediumMessage = _CreateMediumMessage();

        // when
        var act = () => new ConsumerContext(null!, mediumMessage);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_throw_when_message_is_null()
    {
        // given
        var descriptor = _CreateDescriptor();

        // when
        var act = () => new ConsumerContext(descriptor, null!);

        // then
        act.Should().Throw<ArgumentNullException>();
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor()
    {
        var methodInfo = typeof(ConsumerContextTestConsumer).GetMethod(
            nameof(ConsumerContextTestConsumer.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            [typeof(ContextTestMessage), typeof(CancellationToken)]
        )!;

        return new ConsumerExecutorDescriptor
        {
            MethodInfo = methodInfo,
            ImplTypeInfo = typeof(ConsumerContextTestConsumer).GetTypeInfo(),
            TopicName = "test.topic",
            GroupName = "test-group",
        };
    }

    private MediumMessage _CreateMediumMessage()
    {
        var message = new Message(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [Headers.MessageId] = Faker.Random.Guid().ToString(),
                [Headers.MessageName] = "test.topic",
            },
            new ContextTestMessage("test-value")
        );

        return new MediumMessage
        {
            DbId = Faker.Random.Guid().ToString(),
            Origin = message,
            Content = "{}",
            Added = DateTime.UtcNow,
        };
    }
}

public sealed record ContextTestMessage(string Value);

public sealed class ConsumerContextTestConsumer
{
    public static ValueTask Consume(ContextTestMessage message, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
