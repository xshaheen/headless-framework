---
status: ready
priority: p3
issue_id: "054"
tags: [code-review, refactoring, dry, code-quality]
created: 2026-01-20
resolved: 2026-01-21
dependencies: []
---

# Code Duplication: AWS Client Creation

## Problem

~60 lines duplicated 3x for SNS/SQS client creation:
- `AmazonSqsConsumerClient.cs:159-187` (SNS)
- `AmazonSqsConsumerClient.cs:199-217` (SQS)
- `AmazonSqsTransport.cs:91-112` (SNS)

Identical nested ternary logic: credentials check + service URL check.

## Solution

Extracted factory methods to `AwsClientFactory.cs`:

```csharp
internal static class AwsClientFactory
{
    public static IAmazonSimpleNotificationService CreateSnsClient(AmazonSqsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SnsServiceUrl))
        {
            return options.Credentials != null
                ? new AmazonSimpleNotificationServiceClient(options.Credentials, options.Region)
                : new AmazonSimpleNotificationServiceClient(options.Region);
        }

        var config = new AmazonSimpleNotificationServiceConfig { ServiceURL = options.SnsServiceUrl };
        return options.Credentials != null
            ? new AmazonSimpleNotificationServiceClient(options.Credentials, config)
            : new AmazonSimpleNotificationServiceClient(config);
    }

    public static IAmazonSQS CreateSqsClient(AmazonSqsOptions options) { /* similar */ }
}
```

**LOC savings:** ~40 lines

## Acceptance Criteria

- [x] Extract CreateSnsClient helper
- [x] Extract CreateSqsClient helper
- [x] Update all 3 call sites
- [x] Verify builds and tests pass

## Resolution

Created `AwsClientFactory.cs` with static factory methods for SNS and SQS client creation.
Updated call sites in:
- `AmazonSqsConsumerClient.cs` (2 locations: SNS and SQS)
- `AmazonSqsTransport.cs` (1 location: SNS)

Build successful with 0 errors, 0 warnings.

**Effort:** 1 hour | **Risk:** Very Low
