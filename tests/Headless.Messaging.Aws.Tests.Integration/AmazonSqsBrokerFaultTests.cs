// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Tests;

[Collection<LocalStackTestFixture>]
public sealed class AmazonSqsBrokerFaultTests(LocalStackTestFixture fixture) : BrokerFaultTestsBase
{
    protected override string ProviderName => "AWS/LocalStack";

    protected override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        CancellationToken cancellationToken
    )
    {
        return fixture.CreateConformanceSessionAsync(cancellationToken);
    }

    protected override async ValueTask<InterruptibleTransportSession> CreateInterruptibleSessionAsync(
        CancellationToken cancellationToken
    )
    {
        // A LocalStack of its own on a fixed host port: a restart must not disturb the collection's LocalStack, and the
        // session must find the restarted one at the address it already holds.
#pragma warning disable CA2000 // Ownership moves to the returned session, or the catch disposes it.
        var broker = new FixedPortLocalStackFixture();
#pragma warning restore CA2000

        try
        {
            await broker.StartAsync();
            var session = await LocalStackTestFixture.CreateConformanceSessionAsync(
                broker.ConnectionString,
                cancellationToken
            );

            return new InterruptibleTransportSession(
                session,
                token => _RestartAsync(broker, token),
                () => ((IAsyncDisposable)broker).DisposeAsync()
            );
        }
        catch
        {
            await ((IAsyncDisposable)broker).DisposeAsync();
            throw;
        }
    }

    [Fact]
    public override Task should_resume_delivery_once_after_broker_restart()
    {
        return base.should_resume_delivery_once_after_broker_restart();
    }

    // LocalStack keeps its queues in memory, so the queues are noted before the stop and created again after the start,
    // as a real SQS outage would leave them.
    private static async Task _RestartAsync(FixedPortLocalStackFixture broker, CancellationToken cancellationToken)
    {
        List<string> queueNames;

        using (var sqs = _CreateSqsClient(broker))
        {
            var queues = await sqs.ListQueuesAsync(new ListQueuesRequest(), cancellationToken);
            queueNames = [.. (queues.QueueUrls ?? []).Select(static url => url[(url.LastIndexOf('/') + 1)..])];
        }

        await broker.Container.StopAsync(cancellationToken);
        await broker.Container.StartAsync(cancellationToken);

        using (var sqs = _CreateSqsClient(broker))
        {
            foreach (var queueName in queueNames)
            {
                await sqs.CreateQueueAsync(queueName, cancellationToken);
            }
        }
    }

    private static AmazonSQSClient _CreateSqsClient(FixedPortLocalStackFixture broker)
    {
        return new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig { ServiceURL = broker.ConnectionString, AuthenticationRegion = "us-east-1" }
        );
    }
}
