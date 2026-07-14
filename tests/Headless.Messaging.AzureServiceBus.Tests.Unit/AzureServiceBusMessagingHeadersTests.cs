// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.AzureServiceBus;

namespace Tests;

public sealed class AzureServiceBusMessagingHeadersTests
{
    [Fact]
    public void should_have_session_id_header()
    {
        // then
        AzureServiceBusMessagingHeaders.SessionId.Should().Be("headless-session-id");
    }

    [Fact]
    public void should_have_scheduled_enqueue_time_utc_header()
    {
        // then
        AzureServiceBusMessagingHeaders.ScheduledEnqueueTimeUtc.Should().Be("headless-scheduled-enqueue-time-utc");
    }
}
