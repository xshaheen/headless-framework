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

## Design Notes

The package registers both bus and queue capabilities. Bus publishes use SNS and subscribe SQS queues to the topic. Queue sends bypass SNS and write directly to the message queue name in SQS.

## Installation

```bash
dotnet add package Headless.Messaging.Aws
```

## Quick Start

```csharp
builder.Services.AddHeadlessMessaging(options =>
{
    options.UsePostgreSql("connection_string");

    options.UseAws(sqs =>
    {
        sqs.Region = RegionEndpoint.USEast1;
        sqs.Credentials = new BasicAWSCredentials("key", "secret");
    });

    options.SubscribeFromAssemblyContaining<Program>();
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
```

## Messaging Semantics

- Bus publish sends the serialized body through SNS and preserves message headers.
- Queue send writes the serialized body directly to SQS and preserves message headers as message attributes.
- Delay stays in the core pipeline. This provider does not add broker-native scheduling.
- Commit deletes the SQS message.
- Reject shortens visibility to trigger SQS redelivery. Dead-lettering follows queue redrive policy.
- Bus `FetchTopicsAsync(...)` creates SNS topics and returns ARNs.
- Queue `FetchTopicsAsync(...)` creates direct SQS queues and returns queue URLs.
- Bus consumer startup creates the queue, updates its access policy, and `SubscribeAsync(...)` binds the queue to SNS topics.
- Ordering follows the configured SQS/SNS resources. Do not assume FIFO unless AWS FIFO entities are used.
- Topic names, payload size, and header limits follow AWS SNS and SQS broker limits.

## Dependencies

- `Headless.Messaging.Core`
- `AWSSDK.SimpleNotificationService`
- `AWSSDK.SQS`

## Side Effects

- Creates SQS queues and SNS topics if they don't exist
- Configures IAM policies for queue access
- Establishes persistent connections to AWS services
