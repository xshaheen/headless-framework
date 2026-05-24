// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;

namespace Tests.Internal;

public sealed class PublishOptionsAdapterTests : TestBase
{
    [Fact]
    public void should_strip_delay_for_direct_queue_publish_options()
    {
        // given
        var options = new EnqueueOptions { Topic = "jobs", Delay = TimeSpan.FromMinutes(5) };

        // when
        var publishOptions = PublishOptionsAdapter.ToPublishOptions(options, includeDelay: false);

        // then
        publishOptions.Should().NotBeNull();
        publishOptions!.Topic.Should().Be("jobs");
        publishOptions.Delay.Should().BeNull();
    }

    [Fact]
    public void should_preserve_delay_for_outbox_queue_publish_options()
    {
        // given
        var delay = TimeSpan.FromMinutes(5);
        var options = new EnqueueOptions { Topic = "jobs", Delay = delay };

        // when
        var publishOptions = PublishOptionsAdapter.ToPublishOptions(options);

        // then
        publishOptions.Should().NotBeNull();
        publishOptions!.Delay.Should().Be(delay);
    }

    [Fact]
    public void should_strip_delay_for_direct_bus_publish_options()
    {
        // given
        var options = new PublishOptions { Topic = "events", Delay = TimeSpan.FromMinutes(5) };

        // when
        var publishOptions = PublishOptionsAdapter.WithoutDelay(options);

        // then
        publishOptions.Should().NotBeNull();
        publishOptions!.Topic.Should().Be("events");
        publishOptions.Delay.Should().BeNull();
    }
}
