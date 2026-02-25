// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class ConsumerExecutorDescriptorComparerTests : TestBase
{
    [Fact]
    public void should_produce_same_hash_for_case_insensitive_equal_descriptors()
    {
        // given
        var comparer = new ConsumerExecutorDescriptorComparer(Substitute.For<ILogger>());
        var first = _CreateDescriptor("runtime.topic", "runtime.group.v1");
        var second = _CreateDescriptor("Runtime.Topic", "Runtime.Group.V1");

        // when
        var equals = comparer.Equals(first, second);
        var firstHash = comparer.GetHashCode(first);
        var secondHash = comparer.GetHashCode(second);

        // then
        equals.Should().BeTrue();
        firstHash.Should().Be(secondHash);
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor(string topic, string group)
    {
        var method = typeof(DummyConsumer).GetMethod(
            nameof(DummyConsumer.Consume),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            types: [typeof(ConsumeContext<DummyMessage>), typeof(CancellationToken)],
            modifiers: null
        )!;

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = typeof(DummyConsumer).GetTypeInfo(),
            ImplTypeInfo = typeof(DummyConsumer).GetTypeInfo(),
            MethodInfo = method,
            TopicName = topic,
            GroupName = group,
        };
    }

    private sealed record DummyMessage(string Value);

    private sealed class DummyConsumer : IConsume<DummyMessage>
    {
        public ValueTask Consume(ConsumeContext<DummyMessage> context, CancellationToken cancellationToken)
        {
            _ = context;
            return ValueTask.CompletedTask;
        }
    }
}
