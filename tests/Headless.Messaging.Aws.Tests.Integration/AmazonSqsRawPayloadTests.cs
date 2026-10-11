// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Amazon.Runtime;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Helpers;
using Message = Amazon.SQS.Model.Message;

namespace Tests;

/// <summary>
/// A message a non-.NET producer sends with the plain AWS SDK, with no Headless header bag, reaches a consumer whose
/// contract accepts raw payloads.
/// </summary>
[Collection<LocalStackTestFixture>]
public sealed class AmazonSqsRawPayloadTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_consume_a_headerless_queue_message_once_across_a_redelivery_and_poison_a_malformed_body()
    {
        // given
        var name = $"raw-{Guid.NewGuid():N}";
        using var sqs = _CreateSqsClient();
        var queueUrl = (
            await sqs.CreateQueueAsync(AwsPhysicalAddress.QueueDestination(name).ToSqsCreateQueueRequest(), AbortToken)
        ).QueueUrl;
        var receives = new ConcurrentQueue<string>();
        await using var provider = await _StartHostAsync(
            messaging =>
            {
                messaging.Message<QueueProbe>(name).OnQueue(queue => queue.RawPayload());
                messaging.AddModule<QueueProbeModule>();
            },
            message =>
            {
                receives.Enqueue(message.MessageId);

                // The core deletes a message once its inbox holds it, so the redelivery is forced on the first receive:
                // the message is made visible again and this receive waits until the consumer has received it a
                // second time, so the same SQS message reaches the core twice.
                if (receives.Count == 1)
                {
                    _ForceRedeliveryAsync(sqs, queueUrl, message.ReceiptHandle).GetAwaiter().GetResult();
                }
            }
        );
        var recorder = provider.GetRequiredService<ProbeRecorder>();

        // when: a foreign producer sends a JSON body with no message attributes
        var sent = await sqs.SendMessageAsync(
            new SendMessageRequest { QueueUrl = queueUrl, MessageBody = """{"value":"from-python"}""" },
            AbortToken
        );
        await recorder.WaitForHandlerAsync(AbortToken);

        // then: the handler sees the message under the id SQS assigned at send
        var delivery = recorder.Deliveries.First();
        delivery.Value.Should().Be("from-python");
        delivery.MessageId.Should().Be($"sqs:{sent.MessageId}");

        // then: the redelivery reached the core under the same SQS id, and the inbox did not run the handler again
        await _WaitUntilAsync(() =>
            Task.FromResult(
                receives.Where(id => string.Equals(id, sent.MessageId, StringComparison.Ordinal)).Take(3).Count() == 2
            )
        );
        await _WaitUntilAsync(async () => await _PendingAsync(sqs, queueUrl) == 0);
        await Task.Delay(TimeSpan.FromSeconds(1), AbortToken);
        recorder.Deliveries.Should().ContainSingle();

        // when: a foreign producer sends a body that is not JSON
        await sqs.SendMessageAsync(
            new SendMessageRequest { QueueUrl = queueUrl, MessageBody = "not-json" },
            AbortToken
        );

        // then: it is stored as poisoned, deleted, and never handled
        var storage = provider.GetRequiredService<IDataStorage>();
        await _WaitUntilAsync(async () => await _CountFailedAsync(storage, name) == 1);
        await _WaitUntilAsync(async () => await _PendingAsync(sqs, queueUrl) == 0);
        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);
        recorder.Deliveries.Should().ContainSingle();
        (await _CountFailedAsync(storage, name)).Should().Be(1);
    }

    [Fact]
    public async Task should_consume_a_headerless_sns_message_when_the_identity_queue_carries_only_the_raw_contract()
    {
        // given
        var name = $"raw-bus-{Guid.NewGuid():N}";
        await using var provider = await _StartHostAsync(
            messaging =>
            {
                messaging.Message<BusProbe>(name).OnBus(bus => bus.RawPayload());
                messaging.AddModule<BusProbeModule>();
            },
            onReceive: null
        );
        var recorder = provider.GetRequiredService<ProbeRecorder>();
        using var sns = AwsClientFactory.CreateSnsClient(_Options());
        var topicArn = (
            await sns.CreateTopicAsync(AwsPhysicalAddress.BusTopic(name).ToSnsCreateTopicRequest(), AbortToken)
        ).TopicArn;

        // when: a foreign producer publishes to the topic with no message attributes
        await sns.PublishAsync(
            new PublishRequest { TopicArn = topicArn, Message = """{"value":"from-node"}""" },
            AbortToken
        );
        await recorder.WaitForHandlerAsync(AbortToken);

        // then
        var delivery = recorder.Deliveries.Single();
        delivery.Value.Should().Be("from-node");
        delivery.MessageId.Should().StartWith("sqs:");
    }

    private async Task<ServiceProvider> _StartHostAsync(
        Action<MessagingContributionBuilder> configureMessaging,
        Action<Message>? onReceive
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ProbeRecorder>();
        services.AddSingleton<QueueProbeConsumer>();
        services.AddSingleton<BusProbeConsumer>();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseAws(options =>
            {
                _Configure(options);

                // Observes each receive of the native message, which the redelivery assertion counts.
                options.CustomHeadersBuilder = (message, _) =>
                {
                    onReceive?.Invoke(message);
                    return [];
                };
            });
            setup.UseInMemoryStorage();

            // In-memory storage deduplicates within the process, which is all one host in a test needs.
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;

            // The foreign producer writes camelCase JSON; the default options match property names exactly.
            setup.Options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });
        services.ConfigureMessaging(configureMessaging);

        var provider = services.BuildServiceProvider();
        await provider.ApplyMessagingSchemaAsync(AbortToken);
        await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

        return provider;
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
        options.Credentials = new BasicAWSCredentials("test", "test");
        options.ReceiveWaitTime = TimeSpan.FromSeconds(1);
    }

    private async Task _ForceRedeliveryAsync(IAmazonSQS sqs, string queueUrl, string receiptHandle)
    {
        await sqs.ChangeMessageVisibilityAsync(queueUrl, receiptHandle, 0, AbortToken);
        await _WaitUntilAsync(async () => await _CountAsync(sqs, queueUrl, "ApproximateNumberOfMessages") == 0);
    }

    private IAmazonSQS _CreateSqsClient() => AwsClientFactory.CreateSqsClient(_Options());

    private static async Task<int> _PendingAsync(IAmazonSQS sqs, string queueUrl) =>
        await _CountAsync(sqs, queueUrl, "ApproximateNumberOfMessages")
        + await _CountAsync(sqs, queueUrl, "ApproximateNumberOfMessagesNotVisible");

    private static async Task<int> _CountAsync(IAmazonSQS sqs, string queueUrl, string attribute)
    {
        var response = await sqs.GetQueueAttributesAsync(queueUrl, [attribute]);
        return int.Parse(response.Attributes[attribute], CultureInfo.InvariantCulture);
    }

    private static async Task<long> _CountFailedAsync(IDataStorage storage, string name)
    {
        var page = await storage
            .GetMonitoringApi()
            .GetMessagesAsync(
                new MessageQuery
                {
                    MessageType = MessageType.Subscribe,
                    Name = name,
                    StatusName = StatusName.Failed,
                    CurrentPage = 0,
                    PageSize = 10,
                }
            );

        return page.TotalItems;
    }

    private async Task _WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = TimeSpan.FromSeconds(20).ToCancellationTokenSource(AbortToken);

        while (!await condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
        }
    }

    public sealed record QueueProbe(string Value);

    public sealed record BusProbe(string Value);

    public sealed record ProbeDelivery(string Value, string MessageId);

    public sealed class ProbeRecorder
    {
        private readonly TaskCompletionSource _handled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<ProbeDelivery> _deliveries = new();

        public IReadOnlyCollection<ProbeDelivery> Deliveries => _deliveries;

        public Task WaitForHandlerAsync(CancellationToken cancellationToken) =>
            _handled.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);

        public ValueTask RecordAsync(ProbeDelivery delivery)
        {
            _deliveries.Enqueue(delivery);
            _handled.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    public sealed class QueueProbeConsumer(ProbeRecorder recorder) : IConsume<QueueProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<QueueProbe> context, CancellationToken cancellationToken) =>
            recorder.RecordAsync(new ProbeDelivery(context.Message.Value, context.MessageId));
    }

    public sealed class BusProbeConsumer(ProbeRecorder recorder) : IConsume<BusProbe>
    {
        public ValueTask ConsumeAsync(ConsumeContext<BusProbe> context, CancellationToken cancellationToken) =>
            recorder.RecordAsync(new ProbeDelivery(context.Message.Value, context.MessageId));
    }

    public sealed class QueueProbeModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddQueueConsumer<QueueProbeConsumer, QueueProbe>(
                "tests.aws.raw-queue-probe",
                TestConsumerDispatch.FromServices<QueueProbeConsumer, QueueProbe>()
            );
    }

    public sealed class BusProbeModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog) =>
            catalog.AddBusConsumer<BusProbeConsumer, BusProbe>(
                "tests.aws.raw-bus-probe",
                everyInstance: false,
                TestConsumerDispatch.FromServices<BusProbeConsumer, BusProbe>()
            );
    }
}
