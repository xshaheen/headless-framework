// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Messages;
using Framework.Primitives;

namespace Framework.Messages.Monitoring;

public interface IMonitoringApi
{
    ValueTask<MediumMessage?> GetPublishedMessageAsync(long id);

    ValueTask<MediumMessage?> GetReceivedMessageAsync(long id);

    ValueTask<StatisticsView> GetStatisticsAsync();

    ValueTask<IndexPage<MessageView>> GetMessagesAsync(MessageQuery query);

    ValueTask<int> PublishedFailedCount();

    ValueTask<int> PublishedSucceededCount();

    ValueTask<int> ReceivedFailedCount();

    ValueTask<int> ReceivedSucceededCount();

    ValueTask<Dictionary<DateTime, int>> HourlySucceededJobs(MessageType type);

    ValueTask<Dictionary<DateTime, int>> HourlyFailedJobs(MessageType type);
}
