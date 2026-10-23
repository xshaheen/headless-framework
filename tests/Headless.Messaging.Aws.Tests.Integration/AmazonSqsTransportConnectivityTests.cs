// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Testing.Testcontainers;
using Testcontainers.LocalStack;

namespace Tests;

[Collection("AwsConnectivity")]
public sealed class AmazonSqsTransportConnectivityTests : TransportConnectivityTestsBase
{
    private readonly FixedPortLocalStackFixture _broker = new();
    private List<string> _queueNames = [];

    protected override MessageLane ProbeLane => MessageLane.Queue;

    [Fact]
    public override Task should_report_the_broker_unreachable_while_it_is_down_and_reachable_once_it_is_back()
    {
        return base.should_report_the_broker_unreachable_while_it_is_down_and_reachable_once_it_is_back();
    }

    protected override ValueTask StartBrokerAsync(CancellationToken cancellationToken)
    {
        return _broker.StartAsync();
    }

    protected override void ConfigureTransport(MessagingSetupBuilder setup)
    {
        setup.UseAws(options =>
        {
            options.Region = Amazon.RegionEndpoint.USEast1;
            options.SqsServiceUrl = _broker.ConnectionString;
            options.SnsServiceUrl = _broker.ConnectionString;
            options.Credentials = new BasicAWSCredentials("test", "test");
        });
    }

    protected override async Task InterruptBrokerAsync(CancellationToken cancellationToken)
    {
        // LocalStack keeps its queues in memory, so the ones the host created are noted to bring them back on restore,
        // as a real SQS outage would leave them.
        using var sqs = _CreateSqsClient();
        var queues = await sqs.ListQueuesAsync(new ListQueuesRequest(), cancellationToken);
        _queueNames = [.. (queues.QueueUrls ?? []).Select(static url => url[(url.LastIndexOf('/') + 1)..])];
        _queueNames.Should().NotBeEmpty("the host created its queue before the outage");

        await _broker.Container.StopAsync(cancellationToken);
    }

    protected override async Task RestoreBrokerAsync(CancellationToken cancellationToken)
    {
        await _broker.Container.StartAsync(cancellationToken);

        using var sqs = _CreateSqsClient();
        foreach (var queueName in _queueNames)
        {
            await sqs.CreateQueueAsync(queueName, cancellationToken);
        }
    }

    protected override ValueTask StopBrokerAsync()
    {
        return ((IAsyncDisposable)_broker).DisposeAsync();
    }

    private AmazonSQSClient _CreateSqsClient()
    {
        return new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig { ServiceURL = _broker.ConnectionString, AuthenticationRegion = "us-east-1" }
        );
    }
}

[CollectionDefinition("AwsConnectivity", DisableParallelization = true)]
public sealed class AwsConnectivityCollection;

/// <summary>
/// A LocalStack of its own on a fixed host port, so a stop and start brings it back at the address the host uses;
/// Docker assigns a new random port on every start otherwise.
/// </summary>
internal sealed class FixedPortLocalStackFixture : HeadlessLocalStackFixture
{
    private readonly int _hostPort = BrokerHostPort.Reserve();

    public string ConnectionString => Container.GetConnectionString();

    public ValueTask StartAsync()
    {
        return InitializeAsync();
    }

    protected override LocalStackBuilder Configure()
    {
        return new LocalStackBuilder(TestImages.LocalStack).WithPortBinding(
            _hostPort,
            LocalStackBuilder.LocalStackPort
        );
    }
}
