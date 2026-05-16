// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Primitives;

namespace Headless.Messaging.Monitoring;

[PublicAPI]
public interface IMonitoringApi
{
    ValueTask<MediumMessage?> GetPublishedMessageAsync(long storageId, CancellationToken cancellationToken = default);

    ValueTask<MediumMessage?> GetReceivedMessageAsync(long storageId, CancellationToken cancellationToken = default);

    ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default);

    ValueTask<IndexPage<MessageView>> GetMessagesAsync(
        MessageQuery query,
        CancellationToken cancellationToken = default
    );

    ValueTask<long> PublishedFailedCount(CancellationToken cancellationToken = default);

    ValueTask<long> PublishedSucceededCount(CancellationToken cancellationToken = default);

    ValueTask<long> ReceivedFailedCount(CancellationToken cancellationToken = default);

    ValueTask<long> ReceivedSucceededCount(CancellationToken cancellationToken = default);

    ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    );

    ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(
        MessageType type,
        CancellationToken cancellationToken = default
    );
}
