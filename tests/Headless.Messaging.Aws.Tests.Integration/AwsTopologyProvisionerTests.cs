// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Hosting;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// Against LocalStack, the shared SNS/SQS topology as a deploy-time resource: <c>resources provision</c> creates what the
/// registered consumers and messages need, the host only checks it, clear purges it, and teardown deletes it.
/// </summary>
/// <remarks>
/// LocalStack keeps a separate account for each 12-digit access key, so each test sees only the topics and queues its
/// own hosts create, and the fixed consumer identities of the modules below never collide across tests.
/// </remarks>
[Collection<LocalStackTestFixture>]
public sealed class AwsTopologyProvisionerTests(LocalStackTestFixture fixture) : TestBase
{
    private const string _BusIdentity = "tests.aws.topology-bus";

    private readonly string _accountId = string.Create(
        CultureInfo.InvariantCulture,
        $"{RandomNumberGenerator.GetInt32(100_000, 1_000_000)}{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}"
    );

    private readonly string _name = $"topology-{Guid.NewGuid():N}";

    private string _Topic => AwsPhysicalAddress.BusTopic("", _name + "-paid");

    private static string _BusQueue => AwsPhysicalAddress.BusSubscriptionQueue("", _BusIdentity);

    private string _OrdersQueue => AwsPhysicalAddress.QueueDestination("", _name + "-orders");

    private string _InvoicesQueue => AwsPhysicalAddress.QueueDestination("", _name + "-invoices");

    [Fact]
    public async Task should_provision_verify_purge_and_delete_the_shared_topology()
    {
        // given
        await using var provider = _Build();
        using var sqs = AwsClientFactory.CreateSqsClient(_Options());
        using var sns = AwsClientFactory.CreateSnsClient(_Options());
        var topology = (await _ResourcesAsync(provider)).Should().ContainSingle(r => r.Kind == "AmazonSqs").Which;
        topology.Name.Should().Be("topology");
        await FluentActions
            .Awaiting(() => topology.VerifyAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*not exist*Run 'resources provision'*");

        // when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then: verify passes, and SNS delivers a published message raw to the identity queue
        await topology.VerifyAsync(AbortToken);
        var topic = await sns.FindTopicAsync(_Topic);
        topic.Should().NotBeNull();
        var topicArn = topic!.TopicArn;
        var busQueueUrl = await _QueueUrlAsync(sqs, _BusQueue);
        await sns.PublishAsync(
            new PublishRequest { TopicArn = topicArn, Message = """{"Value":"paid"}""" },
            AbortToken
        );
        var delivered = await sqs.ReceiveMessageAsync(
            new ReceiveMessageRequest(busQueueUrl) { WaitTimeSeconds = 10, MaxNumberOfMessages = 1 },
            AbortToken
        );
        delivered.Messages.Should().ContainSingle().Which.Body.Should().Be("""{"Value":"paid"}""");

        // and when: a provision run again changes nothing, and a message waits in a queue the host consumes
        await provider.ProvisionResourcesAsync(AbortToken);
        (await sns.ListSubscriptionsByTopicAsync(topicArn, AbortToken)).Subscriptions.Should().ContainSingle();
        var ordersQueueUrl = await _QueueUrlAsync(sqs, _OrdersQueue);
        await sqs.SendMessageAsync(ordersQueueUrl, """{"Value":"order"}""", AbortToken);
        await provider.PurgeResourcesAsync(AbortToken);

        // then
        (await _PendingAsync(sqs, ordersQueueUrl))
            .Should()
            .Be(0);

        // and when
        await provider.DeleteResourcesAsync(AbortToken);

        // then: the queues the host consumes and their subscriptions go; the shared topic and the queue the host only
        // sends to stay
        (await _QueueExistsAsync(sqs, _BusQueue))
            .Should()
            .BeFalse();
        (await _QueueExistsAsync(sqs, _OrdersQueue)).Should().BeFalse();
        (await _QueueExistsAsync(sqs, _InvoicesQueue)).Should().BeTrue();
        (await sns.FindTopicAsync(_Topic)).Should().NotBeNull("every service that publishes the message shares it");
        (await sns.ListSubscriptionsByTopicAsync(topicArn, AbortToken)).Subscriptions.Should().BeNullOrEmpty();
        await FluentActions
            .Awaiting(() => topology.VerifyAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*not exist*Run 'resources provision'*");
    }

    [Fact]
    public async Task should_hold_readiness_and_create_nothing_when_a_default_host_starts()
    {
        // given
        await using var provider = _Build();
        using var sqs = AwsClientFactory.CreateSqsClient(_Options());
        using var sns = AwsClientFactory.CreateSnsClient(_Options());
        var warmup = provider.GetServices<IWarmup>().Should().ContainSingle(w => w.Name == "Resources").Which;

        // when
        var readiness = () => warmup.WarmUpAsync(AbortToken);
        var bootstrap = () => provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        // then: a reachable broker without the topology is a deployment fault the start names, not an outage to retry
        await readiness
            .Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage($"*SNS topic '{_Topic}'*Run 'resources provision'*");
        await bootstrap.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*Run 'resources provision'*");
        (await sns.ListTopicsAsync(new ListTopicsRequest(), AbortToken)).Topics.Should().BeNullOrEmpty();
        (await sqs.ListQueuesAsync(new ListQueuesRequest(), AbortToken)).QueueUrls.Should().BeNullOrEmpty();

        // and when: the deployment creates the topology
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        await warmup.WarmUpAsync(AbortToken);
    }

    [Fact]
    public async Task should_consume_on_both_lanes_once_the_deployment_set_the_topology_up()
    {
        // given
        await using var provider = _Build();
        using var sqs = AwsClientFactory.CreateSqsClient(_Options());
        using var sns = AwsClientFactory.CreateSnsClient(_Options());
        await provider.ProvisionResourcesAsync(AbortToken);

        // when
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);
        await sns.PublishAsync(
            new PublishRequest
            {
                TopicArn = (await sns.FindTopicAsync(_Topic))!.TopicArn,
                Message = """{"Value":"paid"}""",
            },
            AbortToken
        );
        await sqs.SendMessageAsync(await _QueueUrlAsync(sqs, _OrdersQueue), """{"Value":"order"}""", AbortToken);

        // then
        var recorder = provider.GetRequiredService<Recorder>();
        await recorder.Bus.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        await recorder.Queue.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
    }

    [Fact]
    public async Task should_leave_a_queue_this_host_only_sends_to_untouched_by_clear_and_teardown()
    {
        // given: the consumer service owns the queue; the publisher only sends to it
        await using var consumer = _Build();
        await using var publisher = _Build(consume: false);
        using var sqs = AwsClientFactory.CreateSqsClient(_Options());
        await consumer.ProvisionResourcesAsync(AbortToken);
        await publisher.ProvisionResourcesAsync(AbortToken);
        var ordersQueueUrl = await _QueueUrlAsync(sqs, _OrdersQueue);
        await sqs.SendMessageAsync(ordersQueueUrl, """{"Value":"order"}""", AbortToken);

        // when
        await publisher.PurgeResourcesAsync(AbortToken);
        await publisher.DeleteResourcesAsync(AbortToken);

        // then
        (await _PendingAsync(sqs, ordersQueueUrl))
            .Should()
            .Be(1, "the publisher neither purged nor deleted the consumer's queue");
        (await _QueueExistsAsync(sqs, _BusQueue)).Should().BeTrue();
    }

    private ServiceProvider _Build(bool consume = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Recorder>();
        services.AddSingleton<PaidConsumer>();
        services.AddSingleton<OrderConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseAws(_Configure);
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<Paid>(_name + "-paid", "1").OnBus(bus => bus.RawPayload());
            messaging.Message<Order>(_name + "-orders", "1").OnQueue(queue => queue.RawPayload());
            messaging.Message<Invoice>(_name + "-invoices", "1").OnQueue(queue => queue.RawPayload());

            if (consume)
            {
                messaging.AddModule<TopologyModule>();
            }
        });

        return services.BuildServiceProvider();
    }

    private AmazonSqsMessagingOptions _Options()
    {
        var options = new AmazonSqsMessagingOptions { Region = Amazon.RegionEndpoint.USEast1 };
        _Configure(options);
        return options;
    }

    private void _Configure(AmazonSqsMessagingOptions options)
    {
        options.Region = Amazon.RegionEndpoint.USEast1;
        options.SnsServiceUrl = fixture.ConnectionString;
        options.SqsServiceUrl = fixture.ConnectionString;
        options.Credentials = new BasicAWSCredentials(_accountId, "test");
        options.ReceiveWaitTime = TimeSpan.FromSeconds(1);
    }

    private static async Task<List<IResourceProvisioner>> _ResourcesAsync(IServiceProvider provider)
    {
        var resources = new List<IResourceProvisioner>();

        foreach (var source in provider.GetServices<IResourceProvisionerSource>())
        {
            resources.AddRange(await source.GetProvisionersAsync(AbortToken));
        }

        return resources;
    }

    private static async Task<string> _QueueUrlAsync(IAmazonSQS sqs, string queueName) =>
        (await sqs.GetQueueUrlAsync(queueName, AbortToken)).QueueUrl;

    private static async Task<bool> _QueueExistsAsync(IAmazonSQS sqs, string queueName)
    {
        try
        {
            await sqs.GetQueueUrlAsync(queueName, AbortToken);
            return true;
        }
        catch (QueueDoesNotExistException)
        {
            return false;
        }
    }

    private static async Task<int> _PendingAsync(IAmazonSQS sqs, string queueUrl)
    {
        var response = await sqs.GetQueueAttributesAsync(
            queueUrl,
            [QueueAttributeName.ApproximateNumberOfMessages, QueueAttributeName.ApproximateNumberOfMessagesNotVisible],
            AbortToken
        );

        return response.ApproximateNumberOfMessages + response.ApproximateNumberOfMessagesNotVisible;
    }

    public sealed record Paid(string Value);

    public sealed record Order(string Value);

    public sealed record Invoice(string Value);

    public sealed class Recorder
    {
        public TaskCompletionSource Bus { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Queue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class PaidConsumer(Recorder recorder) : IConsume<Paid>
    {
        public ValueTask ConsumeAsync(ConsumeContext<Paid> context, CancellationToken cancellationToken)
        {
            recorder.Bus.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    public sealed class OrderConsumer(Recorder recorder) : IConsume<Order>
    {
        public ValueTask ConsumeAsync(ConsumeContext<Order> context, CancellationToken cancellationToken)
        {
            recorder.Queue.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    public sealed class TopologyModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddBusConsumer<PaidConsumer, Paid>(
                _BusIdentity,
                everyInstance: false,
                TestConsumerDispatch.FromServices<PaidConsumer, Paid>()
            );
            catalog.AddQueueConsumer<OrderConsumer, Order>(
                "tests.aws.topology-queue",
                TestConsumerDispatch.FromServices<OrderConsumer, Order>()
            );
        }
    }
}
