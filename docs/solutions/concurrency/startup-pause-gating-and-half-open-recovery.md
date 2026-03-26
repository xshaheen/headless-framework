---
title: "Startup Pause Gating and Half-Open Recovery in Messaging Circuit Breakers"
category: concurrency
date: 2026-03-22
tags: [messaging, circuit-breaker, dotnet, rabbitmq, azure-service-bus, nats, retry-backpressure, options-validation]
problem_type: concurrency_issue
components:
  - ConsumerRegister
  - RabbitMqConsumerClient
  - AzureServiceBusConsumerClient
  - NatsConsumerClient
  - RetryProcessorOptionsValidator
  - CircuitBreakerStateManager
symptoms:
  - Open circuits can still admit work from consumers that start after the pause transition
  - HalfOpen can wedge when transport resume fails but the callback swallows the exception
  - Retry backpressure can invert under misconfiguration when MaxPollingInterval is below FailedRetryInterval
severity: p1
research:
  agents: [main-orchestrator]
  documented_at: 2026-03-22T00:00:00Z
  conversation_context: "Resolved four todo files from PR #194 around startup pause gating, half-open recovery, retry backpressure validation, and operator docs"
---

# Startup Pause Gating and Half-Open Recovery in Messaging Circuit Breakers

The circuit breaker implementation was already using transport pause/resume hooks, but two control-plane gaps remained: late-starting consumers could bypass an already-open circuit during startup, and failed resume attempts during `Open -> HalfOpen` could be logged and swallowed instead of reopening the circuit. The same review pass also found a related configuration hole where retry backpressure accepted a `MaxPollingInterval` below the base retry interval.

## Root Cause

The transport contracts were correct in steady state but not at lifecycle boundaries:

- `ConsumerRegister` added new clients to paused groups without waiting for the pause to be applied before subscription and listening continued.
- RabbitMQ and Azure Service Bus only paused active listeners; they had no startup gate for listeners that began after the pause.
- NATS remembered subscribed topics but still created the initial subscriptions while already paused.
- `ConsumerRegister._ResumeGroupAsync` logged `ResumeAsync` failures and returned success, which prevented `CircuitBreakerStateManager` from treating the half-open probe as failed.
- `RetryProcessorOptionsValidator` validated `RetryProcessorOptions` in isolation, even though `MaxPollingInterval` must be compared against `MessagingOptions.FailedRetryInterval`.

## Working Solution

### 1. Treat pause as a startup gate, not just a steady-state toggle

New clients added to a paused group now await `PauseAsync()` before continuing startup. Transport implementations that can begin work after that point must also honor an already-paused state before their first subscribe/listen action.

```csharp
// ConsumerRegister
var innerClient = await _consumerClientFactory.CreateAsync(groupName, limit);
await handle.AddClientAsync(innerClient);
await innerClient.SubscribeAsync(topics);
await innerClient.ListeningAsync(_pollingDelay, groupCts.Token);
```

```csharp
// RabbitMQ / Azure Service Bus
await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
await _serviceBusProcessor.StartProcessingAsync(cancellationToken);
```

```csharp
// NATS
await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
```

### 2. Resume failures must propagate back to the half-open controller

`_ResumeGroupAsync` still logs per-client failures, but it now rethrows after iterating all clients. A single failure is rethrown directly; multiple failures are wrapped in `AggregateException`. That preserves diagnostics without hiding the failed half-open probe from `CircuitBreakerStateManager`.

```csharp
ConcurrentBag<Exception> failures = [];

await Task.WhenAll(
    snapshot.Select(async client =>
    {
        try
        {
            await client.ResumeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume consumer client for group '{GroupName}'.", handle.GroupName);
            failures.Add(ex);
        }
    })
);

if (failures.IsEmpty)
{
    return;
}

var failureList = failures.ToArray();
if (failureList.Length == 1) throw failureList[0];
throw new AggregateException(..., failureList);
```

### 3. Cross-option invariants belong in the FluentValidation validator

The retry processor should not silently redefine invalid configuration as the primary behavior. The validator now takes `IOptions<MessagingOptions>` and rejects `MaxPollingInterval` values lower than `FailedRetryInterval` during options validation.

```csharp
RuleFor(x => x.MaxPollingInterval)
    .GreaterThan(TimeSpan.Zero)
    .LessThanOrEqualTo(TimeSpan.FromHours(24))
    .GreaterThanOrEqualTo(_failedRetryInterval);
```

## Verification

- Transport startup tests passed for RabbitMQ, Azure Service Bus, and NATS consumer clients.
- `ConsumerRegisterTests` passed with a regression proving resume failures now propagate.
- `CircuitBreakerStateManagerTests` passed, confirming failed half-open probes still reopen cleanly.
- `CircuitBreakerOptionsTests` passed with the new cross-options retry validation.

## Prevention

- Any transport pause/resume contract should be reviewed at three boundaries: startup, steady-state, and recovery from `Open -> HalfOpen`.
- If a callback controls breaker state, logging is not enough; failures must remain observable to the state machine.
- When one option is semantically bounded by another option, validate that invariant in the options validator instead of compensating later in runtime code.
- Add transport-specific lifecycle tests whenever a new transport introduces its own subscription or processor startup path.

## Related Docs

- [Thread Safety and Resilience Patterns in .NET Messaging Circuit Breakers](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md)
