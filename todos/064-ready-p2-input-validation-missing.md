---
status: ready
priority: p2
issue_id: "064"
tags: [code-review, security, rabbitmq, validation]
created: 2026-01-20
resolved: 2026-01-21
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

- [x] Add ValidateQueueName helper
- [x] Add ValidateExchangeName helper
- [x] Add ValidateTopicName helper
- [x] Add options validation for Port, HostName, VirtualHost, ExchangeName
- [x] Add validation in RabbitMqConsumerClient constructor for groupName
- [x] Add validation in RabbitMqConsumerClient.SubscribeAsync for topic names
- [x] Add validation in RabbitMqTransport.SendAsync for topic names
- [x] Add test: invalid queue name → ArgumentException
- [x] Add test: invalid exchange name → ArgumentException
- [x] Add test: invalid topic name → ArgumentException
- [x] Add test: invalid port → options validation failure
- [x] Add test: options validator for all RabbitMqOptions fields
- [x] Add test: consumer client rejects invalid group names
- [x] Add test: transport rejects invalid topic names

**Effort:** 2 hours | **Risk:** Low

## Resolution

Added comprehensive input validation for RabbitMQ implementation:

**Created RabbitMqValidation helper class**:
- `ValidateQueueName()` - validates queue names (alphanumeric, dash, underscore, period; max 255 chars)
- `ValidateExchangeName()` - validates exchange names (same rules as queue names)
- `ValidateTopicName()` - validates topic names (same rules as queue names)

**Added RabbitMqOptionsValidator**:
- Validates `HostName` is not null/whitespace
- Validates `Port` is -1 or in range 1-65535
- Validates `VirtualHost` is not null/whitespace
- Validates `ExchangeName` is not null/whitespace and matches naming rules
- Registered as `IValidateOptions<RabbitMqOptions>` in DI container

**Updated RabbitMqConsumerClient**:
- Constructor validates `groupName` parameter before use
- `SubscribeAsync` validates each topic name before binding

**Updated RabbitMqTransport**:
- `SendAsync` validates topic name before publishing

**Comprehensive test coverage**:
- `RabbitMqValidationTests` - 18 tests for validation helpers
- `RabbitMqOptionsValidatorTests` - 11 tests for options validation
- `RabbitMqConsumerClientValidationTests` - 6 tests for consumer validation
- `RabbitMqTransportTests` - added 2 tests for transport validation

All validation uses existing `Framework.Checks.Argument` class for consistency with framework conventions.
