---
domain: Reliability
packages: Reliability.Abstractions
---

# Reliability

> The failure policy model that Messaging and Jobs share: how many times a handler retries at once, how many times it retries after a growing delay, and which exceptions end a failure without any retry.

## Orientation

- **`Headless.Reliability.Abstractions`** holds `FailurePolicy`, `FailurePolicyBuilder`, `FailurePolicyDefinition`, and `FailurePolicyOverrides`. `Headless.Messaging.Abstractions` and `Headless.Jobs.Abstractions` reference it, so one policy type works for consumers and jobs alike.
- Write a policy as a sealed class that derives from `FailurePolicy`. A runtime builds it once into an immutable `FailurePolicyDefinition` and executes that definition.
- This guide covers the model and compares how the two runtimes apply it in [Messaging and Jobs](#messaging-and-jobs). The runtime details belong to [messaging.md](messaging.md#consumer-failure-policy) and [jobs.md](jobs.md#failure-policy).

## Agent Rules

- Derive policies from `FailurePolicy` with a public parameterless constructor and keep `Configure` deterministic: the runtime builds each policy once per handler identity and caches the result.
- Keep `FailWhen` predicates side-effect free and fast. They run on every failure, possibly concurrently, and a predicate that throws counts as a match, so the failure ends without retry.
- Pass the handler's own exception to `ShouldFail`, not a framework wrapper. `FailOn<T>()` matches `T` and every subtype, so a wrapper type hides the rule.
- Use `ShouldFail(exception, out ruleException)` in a runtime and log a non-null `ruleException`; the short overload hides a broken rule.
- Persist `GetDelayedRetryBaseDelay` where a schedule is stored, and use `GetDelayedRetryDelay` to wait. Jitter cannot be stored, and the base delay is deterministic.
- Do not build a parallel retry loop around a definition. Messaging and Jobs execute it.
- Treat a handler's own cancellation as a failure. Messaging and Jobs retry an `OperationCanceledException` raised while the runtime's token is live, such as an `HttpClient` timeout or a `CancelAfter`, within the policy's budget. Declare `FailOn<OperationCanceledException>()` to end it at once; the rule also matches `TaskCanceledException`. Once the runtime's own token is cancelled (host shutdown, a durable job cancel, lease loss), any `OperationCanceledException` that ends the attempt, whatever token it carries, never reaches the policy, is never retried, and never writes `Failed` or fires `OnExhausted`.

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

`FailurePolicyOverrides.TryParse(settings, errors, out overrides)` reads the children of a `FailurePolicy` configuration section, given as `(Key, Path, Value)` tuples so the package stays free of a configuration dependency. Keys match case-insensitively; retries parse as invariant integers and delays as invariant `TimeSpan` values. Range checks are left to `With`. Each unknown key or unparseable value adds one error naming its path, every setting is read so all problems surface at once, and any error makes the result `null`. Messaging and Jobs use it for their `FailurePolicy` configuration overrides.

## Messaging and Jobs

One policy type works for a message consumer and for a job. Both runtimes resolve it the same way, end a failure in the same `Failed` state, and report it once through `OnExhausted`. They differ in where a delayed retry waits.

### Where to set a policy

- **On the handler**, with `FailurePolicy = typeof(TPolicy)` on the attribute, when the behavior belongs to the handler's code: which exceptions can never succeed, and how long a downstream outage usually lasts.
- **On one host**, with `Tune(identity, ...)`, when a deployment needs a different policy for one handler.
- **In configuration**, under the handler's `FailurePolicy` section, to change counts and delays without a redeploy.
- **As the host default**, with `DefaultFailurePolicy`, for every handler that declares nothing.

### Resolution order

1. `Tune(identity, x => x.FailurePolicy<TPolicy>())` or `FailurePolicy(p => ...)` replaces the declared policy.
2. Otherwise the policy the attribute declares.
3. Otherwise the host's `DefaultFailurePolicy`, and without one the framework default.
4. The `FailurePolicy` configuration section then overrides the numeric values of the winner (`ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, `DelayedMaxDelay`) and keeps its fail rules. An unknown key, an unparseable value, or an invalid combination fails startup with the configuration path.

For a job, a scheduling call's `WithRetries` and `WithRetryIntervals` then override the stored retry count and intervals of that one run. Fail rules always come from the resolved policy. A Messaging publisher has no way to choose a consumer's policy.

### Runtime comparison

| | Message consumer | Job |
| --- | --- | --- |
| Declare | `[BusConsumer(id, FailurePolicy = typeof(T))]`, `[QueueConsumer(id, FailurePolicy = typeof(T))]` | `[Job(id, FailurePolicy = typeof(T))]` |
| Replace on one host | `Tune(id, c => c.FailurePolicy<T>())` | `Tune(id, j => j.FailurePolicy<T>())` |
| Configuration section | `Headless:Messaging:Consumers:{id}:FailurePolicy` | `Headless:Jobs:Jobs:{id}:FailurePolicy` |
| Host default | `setup.DefaultFailurePolicy<T>()` on `MessagingSetupBuilder` | `options.DefaultFailurePolicy<T>()` on `JobsOptionsBuilder` |
| Framework default | 2 immediate retries, then 5 delayed retries from 30 seconds capped at 15 minutes | No retries |
| Immediate retries | Back-to-back inside the first dispatch | Back-to-back inside the run |
| Delayed retries | Persisted with `NextRetryAt` set from the database clock plus the jittered delay; the retry processor picks the row up | In process, under the job's lease, waiting the stored interval |
| Exceptions that end a failure at once | Fail rules, plus `ArgumentException` (and subtypes), `NotSupportedException`, and `SubscriberNotFoundException` | Fail rules; `TerminateExecutionException` keeps its own meaning and never reaches the policy |
| Cancellation | After the consume token is cancelled (host shutdown), any cancellation writes nothing, whatever token it carries; one raised while the consume token is live, by the handler or the transactional inbox's commit work, is classified and retried like any other failure | After the run's token is cancelled, any cancellation is the executor's, whatever token it carries: durable cancel writes `Cancelled`, host shutdown and lease loss write nothing; one the handler raises while the token is live is classified and retried like any other failure |
| Terminal state | `Failed`, with no `NextRetryAt` | `Failed` |
| Terminal callback | `MessagingOptions.RetryPolicy.OnExhausted` | `JobsRetryOptions.OnExhausted` |
| Operator recovery | Re-execute from the Messaging dashboard | `IJobScheduler.RequeueAsync` / `RequeueOccurrenceAsync`, or the requeue button on the Jobs dashboard |
| Invalid policy type | HM005 build error | HF023 build error |
| Not allowed on | Every-instance consumers (HM010 at build time, startup error otherwise) | Nothing |

Messaging details: [Consumer failure policy](messaging.md#consumer-failure-policy). Jobs details: [Failure policy](jobs.md#failure-policy) and [Requeue a failed job](jobs.md#requeue-a-failed-job).

### Valid policy types

The source generators emit `static () => new TPolicy()` for a declared type, so the runtime never creates a policy by reflection. The type must derive from `FailurePolicy`, be a non-abstract class with no open type parameter (a closed generic such as `RetryTwice<Payments>` is accepted), be `public` or `internal` like every type that contains it, and have a public parameterless constructor. Anything else is HM005 in Messaging and HF023 in Jobs, and nothing is generated for that handler.

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
| `FailurePolicyDefinition` | Sealed and immutable. `ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, `DelayedMaxDelay`, `TotalAttempts`, `FailOnExceptionTypes`, `FailWhenRuleCount`, `GetDelayedRetryBaseDelay`, `GetDelayedRetryDelay`, `ShouldFail`, `With`, and `None`. `Create<TPolicy>()` builds a policy type's definition, and `Create(configure)` builds one described inline on a fresh builder. |
| `FailurePolicyOverrides` | Nullable numeric values that `With` applies over a resolved definition. `TryParse` reads them from configuration key/value settings. |

### Design constraints

- Validation runs where a value is written: each builder call throws `ArgumentOutOfRangeException` naming the bad parameter (`retries`, `initialDelay`, or `maxDelay`), and `With` throws the same way for invalid combined values. A policy whose `Configure` writes an invalid value throws from `Build()`.
- A built definition is unaffected by later changes to its builder or to the overrides object passed to `With`.
- `With` returns the same instance when the overrides set nothing.
- `GetDelayedRetryBaseDelay` and `GetDelayedRetryDelay` throw `ArgumentOutOfRangeException` for an attempt number below 1.
