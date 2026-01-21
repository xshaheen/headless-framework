# Headless.Messaging.AmazonSqs

Amazon SQS and SNS transport provider for the messaging system.

## Problem Solved

Enables reliable message delivery using AWS SQS queues and SNS topics with automatic queue creation, dead-letter queues, and IAM policy management.

## Key Features

- **SQS Consumer**: Reliable queue-based message consumption
- **SNS Publisher**: Topic-based message distribution
- **Auto-Provisioning**: Automatic queue and topic creation
- **Dead Letter[Headless.Messaging.AwsSqs.csproj](Headless.Messaging.AwsSqs.csproj) Queues**: Built-in failure handling
- **IAM Integration**: Automatic policy configuration

## Installation

```bash
dotnet add package Headless.Messaging.AmazonSqs
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

## Dependencies

- `Headless.Messaging.Core`
- `AWSSDK.SimpleNotificationService`
- `AWSSDK.SQS`

## Side Effects

- Creates SQS queues and SNS topics if they don't exist
- Configures IAM policies for queue access
- Establishes persistent connections to AWS services
