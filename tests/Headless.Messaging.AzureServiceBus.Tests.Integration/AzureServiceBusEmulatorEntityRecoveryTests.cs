// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Headless.Messaging;
using Headless.Messaging.AzureServiceBus;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

[Collection("AzureServiceBusEmulator")]
public sealed class AzureServiceBusEmulatorEntityRecoveryTests(AzureServiceBusEmulatorFixture fixture) : TestBase
{
    [Fact]
    public async Task should_receive_again_when_the_queue_is_deleted_and_recreated_under_a_running_consumer()
    {
        // given — a consumer receiving from a queue of its own, which no Config.json entity shares
        AzureServiceBusEmulatorFixture.RequireEulaAccepted();
        var administration = new ServiceBusAdministrationClient(fixture.AdministrationConnectionString);
        var queueName = $"recreated-{Guid.NewGuid():N}";
        await administration.CreateQueueAsync(queueName, AbortToken);

        await using var session = await AzureServiceBusFixture.CreateSessionAsync(
            fixture.ConnectionString,
            MessageLane.Queue,
            queueName,
            $"group-{Guid.NewGuid():N}",
            AzureServiceBusMessagingOptions.DefaultTopicPath,
            disposeEntity: async () => await administration.DeleteQueueAsync(queueName, CancellationToken.None),
            AbortToken
        );
        var entityMissing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await session.StartAsync(
            onLog: log =>
            {
                if (
                    log.LogType is MqLogType.ExceptionReceived
                    && log.Reason?.Contains(
                        nameof(ServiceBusFailureReason.MessagingEntityNotFound),
                        StringComparison.Ordinal
                    )
                        is true
                )
                {
                    entityMissing.TrySetResult();
                }
            },
            cancellationToken: AbortToken
        );

        (await session.PublishAsync(_CreateMessage(queueName, "before"), AbortToken)).Succeeded.Should().BeTrue();
        var before = await session.ReceiveAsync(TimeSpan.FromSeconds(20), AbortToken);
        await session.Consumer.CommitAsync(before.SettlementValue, AbortToken);

        // when — the queue is deleted under the running processor, which then fails its receives, and is recreated
        await administration.DeleteQueueAsync(queueName, AbortToken);
        await entityMissing.Task.WaitAsync(TimeSpan.FromSeconds(60), AbortToken);
        await administration.CreateQueueAsync(queueName, AbortToken);

        // then — the same processor receives from the recreated queue: no client rebuild and no host restart
        await session.PublishUntilSucceededAsync(
            _CreateMessage(queueName, "after"),
            TimeSpan.FromSeconds(30),
            AbortToken
        );
        var after = await session.ReceiveAsync(TimeSpan.FromSeconds(90), AbortToken);
        after.Message.Id.Should().Be("after");
        await session.Consumer.CommitAsync(after.SettlementValue, AbortToken);
    }

    private static TransportMessage _CreateMessage(string messageName, string messageId)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = messageId,
            [MessagingHeaders.MessageName] = messageName,
            [MessagingHeaders.Intent] = nameof(MessageLane.Queue),
        };

        return new TransportMessage(headers, "entity-recovery"u8.ToArray());
    }
}
