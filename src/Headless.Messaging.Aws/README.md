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
- **IAM Integration**: Queue policies grant `sqs:SendMessage` only to SNS and only from the subscribing topic ARN; provisioning failures name the required actions and resources
- **FIFO Support**: Preserves `.fifo` suffixes and configures FIFO topics/queues when message names end with `.fifo`.
- **Host-Cancellable Startup**: Consumer connection, topology provisioning, and subscription honor host shutdown.

## Design Notes

The package registers immutable Bus and Queue transport capabilities with independent physical lane topology. Bus uses `bus-{logical-name}` SNS topics and one `bus-{subscriber-group}` SQS queue per logical subscriber group; replicas inside a group compete on that queue. Queue sends bypass SNS and write directly to `queue-{logical-name}`, so the same contract/logical name may be registered independently on both lane roots.

Standard AWS entities remain the default. If a message name ends with `.fifo`, the provider preserves that suffix, creates FIFO SNS/SQS entities with content-based deduplication, and sends `MessageGroupId` from `AwsMessagingHeaders.MessageGroupId` when present, then `headless-msg-group` when present, otherwise `default`. When `headless-msg-id` is present, it is used as the AWS deduplication ID.

SQS message attributes are limited by AWS to 10 entries. Queue sends fail before the AWS call when non-null headers exceed that limit so headers are not silently dropped.

Malformed SNS transport envelopes are terminally deleted after sanitized logging. Handler rejection remains a normal visibility-timeout retry and can use an external SQS redrive policy.

This topology replaces legacy unqualified topics and queues. Before deployment, stop old and new publishers, inventory producer/consumer versions plus SNS/SQS create/subscribe/policy permissions, drain legacy queues to zero visible/in-flight messages, and deploy consumers before publishers behind a version fence. Abort before the first lane-qualified publish if drain or provisioning fails. After publication begins, recover by rolling forward and reconciling legacy and lane-qualified queue counts; retain legacy entities until the deployment owner signs off.

## Installation

```bash
dotnet add package Headless.Messaging.Aws
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.Bus.ForConsumersFromAssemblyContaining<Program>();
    options.UsePostgreSql("connection_string");

    options.UseAws(sqs =>
    {
        sqs.Region = RegionEndpoint.USEast1;
        sqs.Credentials = new BasicAWSCredentials("key", "secret");
    });
});
```

## Configuration

```csharp
options.UseAws(sqs =>
{
    sqs.Region = RegionEndpoint.USEast1;
    sqs.Credentials = awsCredentials;
    sqs.SnsServiceUrl = "https://sns.us-east-1.amazonaws.com";
    sqs.SqsServiceUrl = "https://sqs.us-east-1.amazonaws.com";
});

options.Bus.ForMessage<OrderEvent>(message =>
    message.MessageName("orders.events.fifo").UseAws(aws => aws.MessageGroupId(order => order.CustomerId.ToString()))
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
- Configures IAM policies for bus queue access.
- Establishes persistent connections to AWS services.
- Queue-lane consumers subscribe directly to queue URLs and do not create the Bus group queue.
- Malformed SNS transport envelopes are terminally deleted; handler failures remain eligible for externally configured SQS redrive.
