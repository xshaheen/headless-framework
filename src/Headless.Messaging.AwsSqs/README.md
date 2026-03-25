# Headless.Messaging.AwsSqs

Amazon SQS and SNS transport provider for the messaging system.

## Problem Solved

Enables reliable message delivery using AWS SQS queues and SNS topics with automatic queue creation, dead-letter queues, and IAM policy management.

## Key Features

- **SQS Consumer**: Reliable queue-based message consumption
- **SNS Publisher**: Topic-based message distribution
- **Auto-Provisioning**: Automatic queue and topic creation
- **Dead Letter Queues**: Built-in failure handling
- **IAM Integration**: Automatic policy configuration

## Installation

```bash
dotnet add package Headless.Messaging.AwsSqs
```

## Quick Start

```csharp
builder.Services.AddMessages(options =>
{
    options.UsePostgreSql("connection_string");

    options.UseAmazonSqs(sqs =>
    {
        sqs.Region = "us-east-1";
        sqs.Credentials = new BasicAWSCredentials("key", "secret");
    });

    options.ScanConsumers(typeof(Program).Assembly);
});
```

## Configuration

```csharp
options.UseAmazonSqs(sqs =>
{
    sqs.Region = "us-east-1";
    sqs.Credentials = awsCredentials;
    sqs.SNSServiceUrl = "https://sns.us-east-1.amazonaws.com";
    sqs.SQSServiceUrl = "https://sqs.us-east-1.amazonaws.com";
});
```

## Messaging Semantics

- Publish sends the serialized body through SNS and preserves message headers.
- Delay stays in the core pipeline. This provider does not add broker-native scheduling.
- Commit deletes the SQS message.
- Reject shortens visibility to trigger SQS redelivery. Dead-lettering follows queue redrive policy.
- `FetchTopicsAsync(...)` creates SNS topics and returns ARNs.
- Consumer startup creates the queue, updates its access policy, and `SubscribeAsync(...)` binds the queue.
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
