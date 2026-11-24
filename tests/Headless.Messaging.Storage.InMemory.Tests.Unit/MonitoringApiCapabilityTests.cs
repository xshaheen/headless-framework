// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests;

public sealed class MonitoringApiCapabilityTests : TestBase
{
    [Fact]
    public async Task should_refuse_the_correlation_lookup_naming_it_when_a_store_does_not_implement_it()
    {
        // given: a custom store written before the lookup existed
        IMonitoringApi monitoring = new LegacyMonitoringApi();

        // when
        var act = async () =>
            await monitoring.GetCorrelatedMessagesAsync(
                new MessageTraceQuery { CorrelationId = "c-42", Limit = 10 },
                AbortToken
            );

        // then
        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should()
            .Contain("correlation id")
            .And.Contain(nameof(LegacyMonitoringApi));
    }

    // Implements only what existed before the lookup; the rest is never called.
    private sealed class LegacyMonitoringApi : IMonitoringApi
    {
        public ValueTask<MediumMessage?> GetPublishedMessageAsync(
            Guid storageId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<MediumMessage>> GetPublishedMessagesAsync(
            IReadOnlyList<Guid> storageIds,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<MediumMessage?> GetReceivedMessageAsync(
            Guid storageId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<MediumMessage>> GetReceivedMessagesAsync(
            IReadOnlyList<Guid> storageIds,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<StatisticsView> GetStatisticsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<BacklogView> GetBacklogAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IndexPage<MessageView>> GetMessagesAsync(
            MessageQuery query,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<MessageListPage> ListMessagesAsync(
            MessageListQuery query,
            MessageListPosition? after,
            int pageSize,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<MessageCount> CountMessagesAsync(
            MessageListQuery query,
            int cap = MessageCount.DefaultCap,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<MessageFailureGroup>> GetFailureGroupsAsync(
            MessageType messageType,
            string? consumerIdentity = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<IndexPage<UnknownLaneMessageView>> GetUnknownLaneMessagesAsync(
            UnknownLaneMessageQuery query,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<long> GetPublishedFailedCountAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<long> GetPublishedSucceededCountAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<long> GetReceivedFailedCountAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<long> GetReceivedSucceededCountAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlySucceededJobsAsync(
            MessageType type,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public ValueTask<IReadOnlyDictionary<DateTimeOffset, int>> GetHourlyFailedJobsAsync(
            MessageType type,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
