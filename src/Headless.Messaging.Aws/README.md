# Headless.Messaging.Aws

Amazon SNS bus and SQS queue transport provider for the messaging system.

## Problem Solved

Enables bus fan-out through SNS topics and queue work delivery through SQS queues with automatic topic, queue, and policy provisioning.

## Key Features

- **SNS Bus**: Broadcasts messages through SNS topics.
- **SQS Queue**: Sends queue-intent messages directly to SQS queues.
- **SQS Consumer**: Receives both SNS-enveloped bus messages and direct queue messages.
- **Auto-Provisioning**: Automatic queue and topic creation
- **Dead Letter Queues**: Built-in failure handling
- **IAM Integration**: Automatic policy configuration
- **FIFO Support**: Preserves `.fifo` suffixes and configures FIFO topics/queues when message names end with `.fifo`.

## Design Notes

The package registers both bus and queue capabilities. Bus publishes use SNS and subscribes SQS queues to topics. Queue sends bypass SNS and write directly to the SQS queue named by the message.

Standard AWS entities remain the default. If a message name ends with `.fifo`, the provider preserves that suffix, creates FIFO SNS/SQS entities with content-based deduplication, and sends `MessageGroupId` from `AwsMessagingHeaders.MessageGroupId` when present, then `headless-msg-group` when present, otherwise `default`. When `headless-msg-id` is present, it is used as the AWS deduplication ID.

SQS message attributes are limited by AWS to 10 entries. Queue sends fail before the AWS call when non-null headers exceed that limit so headers are not silently dropped.

## Installation

```bash
dotnet add package Headless.Messaging.Aws
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.ForMessagesFromAssemblyContaining<Program>();
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

options.ForMessage<OrderEvent>(message =>
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
- Queue-intent consumers subscribe directly to queue URLs and do not create the bus group queue.
