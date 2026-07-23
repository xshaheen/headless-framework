// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Transport;

namespace Tests;

public sealed class MessagingEnumCompatibilityTests
{
    [Fact]
    public void should_keep_messaging_event_kind_numeric_contract_stable()
    {
        new[]
        {
            (int)MessagingEventKind.Persist,
            (int)MessagingEventKind.Publish,
            (int)MessagingEventKind.Consume,
            (int)MessagingEventKind.SubscriberInvoke,
        }
            .Should()
            .Equal(0, 1, 2, 3);
    }

    [Fact]
    public void should_keep_persisted_status_name_contract_stable()
    {
        new[]
        {
            (int)StatusName.Failed,
            (int)StatusName.Scheduled,
            (int)StatusName.Succeeded,
            (int)StatusName.Delayed,
            (int)StatusName.Queued,
        }
            .Should()
            .Equal(-1, 0, 1, 2, 3);

        new[]
        {
            nameof(StatusName.Failed),
            nameof(StatusName.Scheduled),
            nameof(StatusName.Succeeded),
            nameof(StatusName.Delayed),
            nameof(StatusName.Queued),
        }
            .Should()
            .Equal("Failed", "Scheduled", "Succeeded", "Delayed", "Queued");
    }

    [Fact]
    public void should_keep_transport_log_type_numeric_contract_stable()
    {
        Enum.GetValues<MqLogType>().Select(static value => (int)value).Should().Equal(Enumerable.Range(0, 14));
    }
}
