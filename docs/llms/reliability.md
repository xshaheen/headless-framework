---
domain: Reliability
packages: Reliability.Abstractions
---

# Reliability

> The failure policy model that Messaging and Jobs share: how many times a handler retries at once, how many times it retries after a growing delay, and which exceptions end a failure without any retry.

## Orientation

- **`Headless.Reliability.Abstractions`** holds `FailurePolicy`, `FailurePolicyBuilder`, `FailurePolicyDefinition`, and `FailurePolicyOverrides`. `Headless.Messaging.Abstractions` and `Headless.Jobs.Abstractions` reference it, so one policy type works for consumers and jobs alike.
- Write a policy as a sealed class that derives from `FailurePolicy`. A runtime builds it once into an immutable `FailurePolicyDefinition` and executes that definition.
- This guide covers the model. How a consumer or a job names its policy, how the policy resolves against host defaults and configuration, and what happens at the terminal failure belong to [messaging.md](messaging.md) and [jobs.md](jobs.md).

## Agent Rules

- Derive policies from `FailurePolicy` with a public parameterless constructor and keep `Configure` deterministic: the runtime builds each policy once per handler identity and caches the result.
- Keep `FailWhen` predicates side-effect free and fast. They run on every failure, possibly concurrently, and a predicate that throws counts as a match, so the failure ends without retry.
- Pass the handler's own exception to `ShouldFail`, not a framework wrapper. `FailOn<T>()` matches `T` and every subtype, so a wrapper type hides the rule.
- Use `ShouldFail(exception, out ruleException)` in a runtime and log a non-null `ruleException`; the short overload hides a broken rule.
- Persist `GetDelayedRetryBaseDelay` where a schedule is stored, and use `GetDelayedRetryDelay` to wait. Jitter cannot be stored, and the base delay is deterministic.
- Do not build a parallel retry loop around a definition. Messaging and Jobs execute it.

## Core Concepts

### Attempts

A failing operation gets at most `TotalAttempts = 1 + ImmediateRetries + DelayedRetries` attempts: the first attempt, then the immediate retries back-to-back with no delay, then the delayed retries. Each tier accepts 0 to `FailurePolicyDefinition.MaxRetriesPerTier` (100) retries. A policy that sets nothing, or `FailurePolicyDefinition.None`, gets one attempt.

### Delayed backoff

Delayed retry `n` (1-based) waits `min(initialDelay × 2^(n-1), maxDelay)` before jitter. With a 30-second initial delay and a 15-minute cap, the delays are 30 s, 60 s, 120 s, 240 s, 480 s, then 900 s for every later attempt. Any attempt number is accepted and reaches the cap without overflow.

Jitter scales that delay by a uniform factor in `[0.8, 1.2)` (`FailurePolicyDefinition.JitterFraction` is 0.2), then caps the result at `maxDelay` again, so a jittered delay never exceeds the cap. Jitter spreads the retries of many failures that started together. `GetDelayedRetryDelay(attempt)` draws from `Random.Shared`; `GetDelayedRetryDelay(attempt, random)` takes a source, where `NextDouble()` returning 0.5 yields the base delay.

Both delays are at most `FailurePolicyDefinition.MaxDelayLimit` (24 hours). When delayed retries are positive, the initial delay must be positive and the maximum delay at least the initial delay.

### Fail rules

A fail rule ends the failure at once, skipping every remaining retry:

- `FailOn<TException>()` matches `TException` and any type derived from it.
- `FailWhen(predicate)` matches when the predicate returns `true` or throws.

An exception that no rule matches is retryable. Rules accumulate; a repeated `FailOn<T>()` is ignored.

### Overrides

`FailurePolicyOverrides` carries nullable `ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, and `DelayedMaxDelay`. `definition.With(overrides)` replaces only the supplied values, keeps the fail rules, and validates the combined result the way the builder does. Fail rules are code and cannot come from configuration. The properties are settable so configuration binding can populate them.

---

## Headless.Reliability.Abstractions

The failure policy model. Namespace: `Headless.Reliability`.

### Setup

```bash
dotnet add package Headless.Reliability.Abstractions
```

```csharp
using System.Net;
using Headless.Reliability;

public sealed class PaymentsFailurePolicy : FailurePolicy
{
    protected override void Configure(FailurePolicyBuilder policy) =>
        policy
            .Immediate(retries: 2)
            .Delayed(retries: 5, initialDelay: TimeSpan.FromSeconds(30), maxDelay: TimeSpan.FromMinutes(15))
            .FailOn<CardDeclinedException>()
            .FailWhen(static ex => ex is HttpRequestException { StatusCode: HttpStatusCode.BadRequest });
}

public sealed class CardDeclinedException(string message) : Exception(message);
```

A runtime builds and evaluates the definition:

```csharp
var definition = new PaymentsFailurePolicy().Build(); // TotalAttempts == 8

if (definition.ShouldFail(exception, out var ruleException))
{
    // Terminal. Log ruleException when it is not null: a FailWhen predicate threw.
}

var wait = definition.GetDelayedRetryDelay(delayedAttempt: 3); // about 120 s, ±20%
```

### API and behavior

| Type | Purpose |
| --- | --- |
| `FailurePolicy` | Abstract base. Override `protected void Configure(FailurePolicyBuilder)`; call `Build()` to get the definition. |
| `FailurePolicyBuilder` | `Immediate(retries)`, `Delayed(retries, initialDelay, maxDelay)`, `FailOn<TException>()`, `FailWhen(predicate)`, `Build()`. A second `Immediate` or `Delayed` call replaces the tier. |
| `FailurePolicyDefinition` | Sealed and immutable. `ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, `DelayedMaxDelay`, `TotalAttempts`, `FailOnExceptionTypes`, `FailWhenRuleCount`, `GetDelayedRetryBaseDelay`, `GetDelayedRetryDelay`, `ShouldFail`, `With`, and `None`. |
| `FailurePolicyOverrides` | Nullable numeric values that `With` applies over a resolved definition. |

### Design constraints

- Validation runs where a value is written: each builder call throws `ArgumentOutOfRangeException` naming the bad parameter (`retries`, `initialDelay`, or `maxDelay`), and `With` throws the same way for invalid combined values. A policy whose `Configure` writes an invalid value throws from `Build()`.
- A built definition is unaffected by later changes to its builder or to the overrides object passed to `With`.
- `With` returns the same instance when the overrides set nothing.
- `GetDelayedRetryBaseDelay` and `GetDelayedRetryDelay` throw `ArgumentOutOfRangeException` for an attempt number below 1.
