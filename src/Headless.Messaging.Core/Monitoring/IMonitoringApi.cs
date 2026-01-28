// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Primitives;

namespace Headless.Messaging.Monitoring;

public interface IMonitoringApi
{
    ValueTask<MediumMessage?> GetPublishedMessageAsync(long id, CancellationToken cancellationToken = default);

    ValueTask<MediumMessage?> GetReceivedMessageAsync(long id, CancellationToken cancellationToken = default);

    ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default);

    ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    );

    ValueTask<int> PublishedFailedCount(CancellationToken cancellationToken = default);

    ValueTask<int> PublishedSucceededCount(CancellationToken cancellationToken = default);

    ValueTask<int> ReceivedFailedCount(CancellationToken cancellationToken = default);

    ValueTask<int> ReceivedSucceededCount(CancellationToken cancellationToken = default);

    ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    );

    ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    );
}
