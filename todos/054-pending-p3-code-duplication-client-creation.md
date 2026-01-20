---
status: pending
priority: p3
issue_id: "054"
tags: [code-review, refactoring, dry, code-quality]
created: 2026-01-20
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

Extract factory methods:

```csharp
private IAmazonSimpleNotificationService CreateSnsClient(AmazonSqsOptions opts)
{
    var config = string.IsNullOrWhiteSpace(opts.SnsServiceUrl)
        ? null
        : new AmazonSimpleNotificationServiceConfig { ServiceURL = opts.SnsServiceUrl };

    return opts.Credentials != null
        ? new AmazonSimpleNotificationServiceClient(opts.Credentials, config ?? opts.Region)
        : new AmazonSimpleNotificationServiceClient(config ?? opts.Region);
}

private IAmazonSQS CreateSqsClient(AmazonSqsOptions opts) { /* similar */ }
```

**LOC savings:** ~40 lines

## Acceptance Criteria

- [ ] Extract CreateSnsClient helper
- [ ] Extract CreateSqsClient helper
- [ ] Update all 3 call sites
- [ ] Verify builds and tests pass

**Effort:** 1 hour | **Risk:** Very Low
