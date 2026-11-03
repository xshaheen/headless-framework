// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// The messaging backplane publishes each invalidation directly and as a system message, and lets a broker failure
/// reach the cache so auto-recovery can replay it.
/// </summary>
public sealed class MessagingHybridCacheBackplaneTests : TestBase
{
    [Fact]
    public async Task should_publish_directly_as_a_system_message()
    {
        // given
        var bus = Substitute.For<IBus>();
        var backplane = new MessagingHybridCacheBackplane(bus);
        var message = new CacheInvalidationMessage { InstanceId = "node-a", Key = "key" };

        // when
        await backplane.PublishAsync(message, AbortToken);

        // then - Direct keeps an open unit of work from capturing the invalidation; a system message needs no tenant
        await bus.Received(1)
            .PublishAsync(
                message,
                Arg.Is<PublishOptions?>(options =>
                    options!.DeliveryMode == DeliveryMode.Direct && options.IsSystemMessage
                ),
                AbortToken
            );
    }

    [Fact]
    public async Task should_surface_a_publish_failure_to_the_cache()
    {
        // given
        var bus = Substitute.For<IBus>();
        bus.PublishAsync(Arg.Any<CacheInvalidationMessage>(), Arg.Any<PublishOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<PublishReceipt>(new InvalidOperationException("broker down")));
        var backplane = new MessagingHybridCacheBackplane(bus);

        // when
        var act = async () =>
            await backplane.PublishAsync(
                new CacheInvalidationMessage { InstanceId = "node-a", Key = "key" },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("broker down");
    }
}
