// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Primitives;
using Headless.Testing.Tests;

namespace Tests.Monitoring;

public sealed class MonitoringApiDefaultMemberTests : TestBase
{
    [Fact]
    public async Task should_throw_not_supported_when_a_provider_does_not_report_its_backlog()
    {
        // given: a provider written before the backlog member existed
        IMonitoringApi monitoring = new ProviderWithoutBacklog();

        // when
        var act = async () => await monitoring.GetBacklogAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage($"*{nameof(ProviderWithoutBacklog)}*{nameof(IMonitoringApi.GetBacklogAsync)}*");
    }

    private sealed class ProviderWithoutBacklog : IMonitoringApi
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

        public ValueTask<IndexPage<MessageView>> GetMessagesAsync(
            MessageQuery query,
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
