---
status: pending
priority: p2
issue_id: "064"
tags: [code-review, security, rabbitmq, validation]
created: 2026-01-20
dependencies: []
---

# Missing Input Validation (Security)

## Problem

No validation on user-provided inputs:

**RabbitMQConsumerClient.cs:119-148:**
- `queue.Name` (line 129) - used directly in `QueueDeclare`
- No length limits, special char checks
- Could inject malicious queue names

**RabbitMqOptions.cs:**
- `HostName` - no DNS validation
- `Port` - no range check (1-65535)
- `VirtualHost` - no validation

**RabbitMqTransport.cs:44:**
- `name` parameter - no validation before use

## Solution

**Add validation helpers:**
```csharp
private static void ValidateQueueName(string name)
{
    Argument.NotNullOrWhiteSpace(name);
    Argument.IsLessThanOrEqualTo(name.Length, 255);

    if (!Regex.IsMatch(name, @"^[a-zA-Z0-9._-]+$"))
        throw new ArgumentException("Invalid queue name format", nameof(name));
}
```

**Add options validation:**
```csharp
services.AddOptions<RabbitMqOptions>()
    .Validate(opts => opts.Port is >= 1 and <= 65535, "Port must be 1-65535")
    .Validate(opts => !string.IsNullOrWhiteSpace(opts.HostName), "HostName required");
```

## Acceptance Criteria

- [ ] Add ValidateQueueName helper
- [ ] Add ValidateExchangeName helper
- [ ] Add options validation for Port, HostName, VirtualHost
- [ ] Add test: invalid queue name → ArgumentException
- [ ] Add test: invalid port → options validation failure

**Effort:** 2 hours | **Risk:** Low
