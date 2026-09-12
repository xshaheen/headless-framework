# Headless.Messaging.Aws

Amazon SNS bus and SQS queue transport provider for the messaging system.

## Problem Solved

Enables bus fan-out through SNS topics and queue work delivery through SQS queues with automatic topic, queue, and policy provisioning.

## Key Features

- **SNS Bus**: Broadcasts messages through SNS topics.
- **SQS Queue**: Sends Queue-lane messages directly to SQS queues.
- **SQS Consumer**: Receives both SNS-enveloped bus messages and direct queue messages.
- **Auto-Provisioning**: Automatic queue and topic creation
- **Malformed Message Handling**: Terminally deletes malformed SNS transport envelopes so they cannot create a visibility-timeout redelivery storm
- **SNS Delivery Policy**: SQS queue resource policies grant `sqs:SendMessage` only to SNS and only from the subscribing topic ARN; provisioning failures name the required actions and resources
- **FIFO Support**: Preserves `.fifo` suffixes and configures FIFO topics/queues when message names end with `.fifo`.
- **Host-Cancellable Startup**: Consumer connection, topology provisioning, and subscription honor host shutdown.

## Design Notes

The package registers immutable Bus and Queue transport capabilities with independent physical lane topology. Bus uses `bus-{logical-name}` SNS topics and one `bus-{subscriber-group}` SQS queue per logical subscriber group; replicas inside a group compete on that queue. Queue sends bypass SNS and write directly to `queue-{logical-name}`, so the same contract/logical name may be registered independently on both lane roots.

Standard AWS entities remain the default. If a message name ends with `.fifo`, the provider preserves that suffix, creates FIFO SNS/SQS entities with content-based deduplication, and sends `MessageGroupId` from `AwsMessagingHeaders.MessageGroupId` when present, then `headless-msg-group` when present, otherwise `default`. When `headless-msg-id` is present, it is used as the AWS deduplication ID.

SQS supports at most ten native message attributes. Unkeyed Queue sends exceeding that limit fail before client effects. Typed keyed Queue sends use the lossless versioned header bag described below.

Malformed SNS transport envelopes are terminally deleted after sanitized logging. Handler rejection remains a normal visibility-timeout retry and can use an external SQS redrive policy.

`AmazonSqsMessagingOptions.Credentials` (or the AWS SDK default credential chain) supplies one identity to runtime and topology calls; there is no separate provisioning credential or disable-auto-provision switch. Grant only the actions used by each deployed workload:

| Workload / owner | Runtime actions | Provisioning and discovery actions | Resource scope |
| --- | --- | --- | --- |
| Bus publisher workload role | `sns:ListTopics`, `sns:Publish` | `sns:CreateTopic` when a `bus-*` topic is absent | `sns:ListTopics` requires `Resource: "*"`; scope create/publish to `arn:${Partition}:sns:${Region}:${Account}:bus-*` |
| Queue publisher workload role | `sqs:SendMessage` | `sqs:CreateQueue` on first use in each process, including for a pre-created queue because the provider uses the idempotent create call to resolve its URL | Scope both actions to `arn:${Partition}:sqs:${Region}:${Account}:queue-*` |
| Bus consumer workload role | `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility` | `sns:CreateTopic`, `sqs:CreateQueue`, `sqs:GetQueueAttributes`, `sqs:SetQueueAttributes`, `sns:Subscribe` | Scope SNS actions to the exact generated `arn:${Partition}:sns:${Region}:${Account}:bus-*` topics and SQS actions to the exact generated `arn:${Partition}:sqs:${Region}:${Account}:bus-*` group queues |
| Queue consumer workload role | `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility` | `sqs:CreateQueue` on startup | Scope all actions to the consumer-owned `arn:${Partition}:sqs:${Region}:${Account}:queue-*` destinations |
| SNS service principal; queue resource-policy owner is the Bus consumer deployment | `sqs:SendMessage` | None | The provider writes the Bus queue policy for principal `sns.amazonaws.com`, resource = that group queue ARN, and `aws:SourceArn` = the subscribing `bus-*` topic ARN |

The deployment owner owns the workload-role policies and the provider-created queue resource policy. Consumer topology failures from AWS surface as `AWS_MESSAGING_PROVISIONING_DENIED` with the lane, logical group, AWS error code, and the aggregate action set for that stage; use the denied AWS API operation to identify the exact missing action. Publisher denials return a failed `OperateResult` with the AWS exception message and retain the service exception as the inner exception, while receive denials are logged and retried with backoff. Do not grant delete, wildcard SNS/SQS administration, or unrelated IAM actions: the current transport does not call them.

## Installation

```bash
dotnet add package Headless.Messaging.Aws
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForMessage<OrderPlaced>(message =>
        message.Consumer<OrderPlacedConsumer>(consumer =>
            consumer.ConsumerIdentity("orders.order-placed")
        )
    );
    options.Options.RequiredInboxCapability = MessagingInboxCapabilityTier.DurableDedupeOnly;
    options.UsePostgreSql("connection_string");

    options.UseAws(sqs =>
    {
        sqs.Region = RegionEndpoint.USEast1;
        sqs.Credentials = new BasicAWSCredentials("key", "secret");
    });
});
```

## Configuration

`RoutingAffinityKey` maps to native `MessageGroupId` only for registered `.fifo` SNS topics or SQS queues. Keys are 1–128 printable ASCII characters (`!` through `~`), without spaces. `AwsMessagingHeaders.MessageGroupId` and `MessageGroupId(...)` remain raw adapters and must agree with a supplied typed key. Standard SQS message-group fairness is not an affinity guarantee; typed keys on standard routes are rejected. Shared groups do not imply whole-pipeline FIFO or handler exclusivity. No application headers are discarded.

All SQS Queue sends encode the complete header dictionary, including null, delivery, trace, and business metadata, as one String attribute named `headless-aws-headers-v1`. The payload body and native `MessageGroupId` remain unchanged. Consumers require that exact attribute; other names with the same prefix are ordinary application headers inside the bag. Missing bags, mixed attributes, malformed JSON, duplicate or reserved header names, and invalid value types are terminally deleted from the source queue without a handler callback. Affinity is optional, so unkeyed messages use the same format. SNS Bus uses its separate SNS envelope format.

```csharp
options.UseAws(sqs =>
{
    sqs.Region = RegionEndpoint.USEast1;
    sqs.Credentials = awsCredentials;
    sqs.SnsServiceUrl = "https://sns.us-east-1.amazonaws.com";
    sqs.SqsServiceUrl = "https://sqs.us-east-1.amazonaws.com";
});

options.Bus.ForMessage<OrderEvent>(message =>
    message.Contract("orders.events.fifo").UseAws(aws => aws.MessageGroupId(order => order.CustomerId.ToString()))
);
```

`MessageGroupId(...)` stamps `AwsMessagingHeaders.MessageGroupId` (`headless-aws-message-group-id`) during publish and is limited to 128 characters. The selector output is broker-visible metadata, so do not put secrets or raw PII in it.

**Registration overloads:** `UseAws(...)` accepts the standard trio — an `IConfiguration` section, an `Action<AmazonSqsMessagingOptions>` delegate, or an `Action<AmazonSqsMessagingOptions, IServiceProvider>` delegate — plus the `RegionEndpoint` convenience form. Options are validated on start.

## Dependencies

- `Headless.Messaging.Core`
- `AWSSDK.SimpleNotificationService`
- `AWSSDK.SQS`

## Side Effects

- Creates SQS queues and SNS topics when they do not exist.
- Configures SQS queue resource policies for SNS Bus delivery.
- Establishes persistent connections to AWS services.
- Queue-lane consumers subscribe directly to queue URLs and do not create the Bus group queue.
- Malformed SNS transport envelopes are terminally deleted; handler failures remain eligible for externally configured SQS redrive.
