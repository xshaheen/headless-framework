// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.Dashboard;

/// <summary>
/// The dashboard's requeue of published rows, on the real pipeline against the simulated broker and store. The
/// requeue endpoint hands each selected row to <see cref="IDispatcher.EnqueueToPublish"/>, so this drives that call:
/// a row that already succeeded is not sent again, and a row that failed terminally is sent once.
/// </summary>
public sealed class PublishedRequeueTests : TestBase
{
    [Fact]
    [Guarantee("MSG-DASH-03")]
    public async Task should_refuse_to_resend_a_succeeded_row_and_send_a_failed_row_once_when_requeued()
    {
        MessagingGuarantees.RequireSupport("MSG-DASH-03");

        // given: publishes get no retry, so a publish the broker refuses leaves its row Failed for good
        var probe = new PipelineProbe();
        await using var host = await PipelineHosts.StartAsync(
            probe,
            AbortToken,
            static setup =>
            {
                setup.Options.RetryPolicy.RetryStrategy.MaxRetryAttempts = 0;
                setup.Options.RetryPolicy.MaxPersistedRetries = 0;
            }
        );

        await host.EnqueueAsync("order-sent", AbortToken);
        var succeeded = await host.WaitForPublishedRowAsync(row => row.StatusName == StatusName.Succeeded, AbortToken);

        host.Broker.Disconnect();
        await host.EnqueueAsync("order-refused", AbortToken);
        var failed = await host.WaitForPublishedRowAsync(
            row => row.StatusName == StatusName.Failed && row.NextRetryAt is null,
            AbortToken
        );
        host.Broker.Restore();

        // when: an operator requeues both rows
        var dispatcher = host.GetRequiredService<IDispatcher>();
        var rows = await host
            .Storage.GetMonitoringApi()
            .GetPublishedMessagesAsync([succeeded.StorageId, failed.StorageId], AbortToken);
        foreach (var row in rows)
        {
            await dispatcher.EnqueueToPublish(row, AbortToken);
        }

        await host.WaitForPublishedRowAsync(
            row => row.StorageId == failed.StorageId && row.StatusName == StatusName.Succeeded,
            AbortToken
        );
        await host.RunForAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then: the failed row was sent once, and the succeeded row was not sent again
        _Sends(host, failed.MessageId).Should().Be(1);
        _Sends(host, succeeded.MessageId).Should().Be(1, "the row was sent once before the requeue, and not again");
    }

    private static int _Sends(SimulationHost host, string messageId) =>
        host.Broker.Events.Count(e =>
            e.Kind == BrokerEventKind.Published && string.Equals(e.MessageId, messageId, StringComparison.Ordinal)
        );
}
