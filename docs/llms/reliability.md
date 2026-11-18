---
domain: Reliability
packages: Reliability.Abstractions
---

# Reliability

> The failure policy model that Messaging and Jobs share: how many times a handler retries at once, how many times it retries after a growing delay, and which exceptions end a failure without any retry.

## Orientation

- **`Headless.Reliability.Abstractions`** holds `FailurePolicy`, `FailurePolicyBuilder`, `FailurePolicyDefinition`, and `FailurePolicyOverrides`. `Headless.Messaging.Abstractions` and `Headless.Jobs.Abstractions` reference it, so one policy type works for consumers and jobs alike.
- Write a policy as a sealed class that derives from `FailurePolicy`. The runtime builds it once into an immutable `FailurePolicyDefinition` and executes that definition.
- This guide covers the model and compares how the two runtimes apply it in [Messaging and Jobs](#messaging-and-jobs). The runtime details are in [messaging/retries.md](messaging/retries.md#consumer-failure-policy) and [jobs/failures.md](jobs/failures.md#failure-policy).

## Agent Rules

- Derive a policy from `FailurePolicy`, give it a public parameterless constructor, and keep `Configure` deterministic. The runtime builds each policy once per handler identity and caches the result.
- Keep `FailWhen` predicates fast and free of side effects. They run on every failure, possibly concurrently. A predicate that throws counts as a match, so the failure ends without retry.
- Let Messaging and Jobs execute the definition. Do not wrap a handler in your own retry loop: the attempts multiply.
- Treat a handler's own cancellation as a failure. Messaging and Jobs retry an `OperationCanceledException` raised while the runtime's token is live, such as an `HttpClient` timeout or a `CancelAfter`, within the policy's budget. Declare `FailOn<OperationCanceledException>()` to end it at once; the rule also matches `TaskCanceledException`.
- Once the runtime's own token is cancelled (host shutdown, a durable job cancel, lease loss), any `OperationCanceledException` that ends the attempt never reaches the policy, whatever token it carries. It is never retried, never writes `Failed`, and never fires the terminal callback (`OnTerminal` in Messaging, `OnExhausted` in Jobs).

## Core Concepts

### Attempts

A failing operation gets at most `TotalAttempts = 1 + ImmediateRetries + DelayedRetries` attempts: the first attempt, then the immediate retries back to back with no delay, then the delayed retries. Each tier accepts 0 to `FailurePolicyDefinition.MaxRetriesPerTier` (100) retries. A policy that sets nothing, or `FailurePolicyDefinition.None`, gets one attempt.

### Delayed backoff

Delayed retry `n` (1-based) waits `min(initialDelay × 2^(n-1), maxDelay)` before jitter. With a 30-second initial delay and a 15-minute cap, the delays are 30 s, 60 s, 120 s, 240 s, 480 s, then 900 s for every later attempt. Any attempt number is accepted and reaches the cap without overflow.

Jitter scales that delay by a uniform factor in `[0.8, 1.2)` (`FailurePolicyDefinition.JitterFraction` is 0.2), then caps the result at `maxDelay` again, so a jittered delay never exceeds the cap. Jitter spreads the retries of many failures that started together.

Both delays are at most `FailurePolicyDefinition.MaxDelayLimit` (24 hours). When delayed retries are positive, the initial delay must be positive and the maximum delay at least the initial delay.

### Fail rules

A fail rule ends the failure at once and skips every remaining retry:

- `FailOn<TException>()` matches `TException` and any type derived from it.
- `FailWhen(predicate)` matches when the predicate returns `true` or throws.

An exception that no rule matches is retryable. Rules accumulate, and a repeated `FailOn<T>()` is ignored.

### Overrides

`FailurePolicyOverrides` carries nullable `ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, and `DelayedMaxDelay`. `definition.With(overrides)` replaces only the supplied values, keeps the fail rules, and validates the combined result the way the builder does. Fail rules are code and cannot come from configuration.

In configuration, the `FailurePolicy` section of a consumer or job takes those four keys. Keys match case-insensitively. Retries parse as invariant integers and delays as invariant `TimeSpan` values (`00:00:30`). Every setting is read, so one start reports every unknown key and every unparseable value, each naming its configuration path.

## Messaging and Jobs

One policy type works for a message consumer and for a job. Both runtimes resolve it the same way, end a failure in the same `Failed` state, and report it once through a terminal callback (`OnTerminal` in Messaging, `OnExhausted` in Jobs). They differ in where a delayed retry waits.

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

For a job, a scheduling call's `WithRetries` and `WithRetryIntervals` then override the stored retry count and intervals of that one run. Fail rules always come from the resolved policy. A Messaging publisher cannot choose a consumer's policy.

### Runtime comparison

| | Message consumer | Job |
| --- | --- | --- |
| Declare | `[BusConsumer(id, FailurePolicy = typeof(T))]`, `[QueueConsumer(id, FailurePolicy = typeof(T))]` | `[Job(id, FailurePolicy = typeof(T))]` |
| Replace on one host | `Tune(id, c => c.FailurePolicy<T>())` | `Tune(id, j => j.FailurePolicy<T>())` |
| Configuration section | `Headless:Messaging:Consumers:{id}:FailurePolicy` | `Headless:Jobs:Jobs:{id}:FailurePolicy` |
| Host default | `setup.DefaultFailurePolicy<T>()` on `MessagingSetupBuilder` | `options.DefaultFailurePolicy<T>()` on `JobsOptionsBuilder` |
| Framework default | 2 immediate retries, then 5 delayed retries from 30 seconds capped at 15 minutes | No retries |
| Immediate retries | Back to back inside the first dispatch | Back to back inside the run |
| Delayed retries | Persisted with `NextRetryAt` set from the database clock plus the jittered delay; the retry processor picks the row up | In process, under the job's lease, waiting the stored interval |
| Exceptions that end a failure at once | Fail rules, plus `ArgumentException` (and subtypes), `NotSupportedException`, and `SubscriberNotFoundException` | Fail rules; `TerminateExecutionException` keeps its own meaning and never reaches the policy |
| Cancellation | After the consume token is cancelled (host shutdown), any cancellation writes nothing, whatever token it carries. One raised while the consume token is live, by the handler or the transactional inbox's commit work, is classified and retried like any other failure | After the run's token is cancelled, any cancellation is the executor's, whatever token it carries: durable cancel writes `Cancelled`, and host shutdown and lease loss write nothing. One the handler raises while the token is live is classified and retried like any other failure |
| Terminal state | `Failed`, with no `NextRetryAt` | `Failed` |
| Terminal callback | `MessagingOptions.RetryPolicy.OnTerminal` | `JobsRetryOptions.OnExhausted` |
| Operator recovery | Re-execute from the Messaging module of the dashboard | `IJobScheduler.RequeueAsync` or `RequeueOccurrenceAsync`, or the requeue button on the Jobs dashboard |
| Invalid policy type | HM005 build error | HF023 build error |
| Not allowed on | Every-instance consumers (HM010 at build time, a startup error otherwise) | Nothing |

Messaging details: [Consumer failure policy](messaging/retries.md#consumer-failure-policy). Jobs details: [Failure policy](jobs/failures.md#failure-policy) and [Requeue a failed job](jobs/failures.md#requeue-a-failed-job).

### Valid policy types

A declared policy type must derive from `FailurePolicy`, be a non-abstract class with no open type parameter (a closed generic such as `RetryTwice<Payments>` is accepted), be `public` or `internal` like every type that contains it, and have a public parameterless constructor. Anything else is HM005 in Messaging and HF023 in Jobs, and that handler is not registered.

---

## Headless.Reliability.Abstractions

The failure policy model. Namespace: `Headless.Reliability`.

### Setup

```bash
dotnet add package Headless.Reliability.Abstractions
```

The Messaging and Jobs abstractions already reference this package. Install it directly only in a project that declares policies without referencing either.

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

Test the policy by building its definition and asserting on it:

```csharp
var definition = new PaymentsFailurePolicy().Build(); // TotalAttempts == 8

definition.ShouldFail(new CardDeclinedException("declined"), out var ruleException); // true; ruleException is null

var wait = definition.GetDelayedRetryBaseDelay(delayedAttempt: 3); // 120 s, before jitter
```

**Verify:** the `describe` verb (`dotnet MyApp.dll describe`) writes a `failurePolicy` object for each consumer and job with the resolved values after configuration overrides: `immediateRetries` and `delayedRetries`, `delayedInitialDelaySeconds` and `delayedMaxDelaySeconds` when `delayedRetries` is above zero, `failOn` (full type names, declaration order), and `failWhenRules` (a count). An invalid policy type fails the build with HM005 or HF023.

### API and behavior

| Type | Purpose |
| --- | --- |
| `FailurePolicy` | Abstract base. Override `protected void Configure(FailurePolicyBuilder)`; call `Build()` to get the definition. |
| `FailurePolicyBuilder` | `Immediate(retries)`, `Delayed(retries, initialDelay, maxDelay)`, `FailOn<TException>()`, `FailWhen(predicate)`, `Build()`. A second `Immediate` or `Delayed` call replaces the tier. |
| `FailurePolicyDefinition` | Sealed and immutable. `ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, `DelayedMaxDelay`, `TotalAttempts`, `FailOnExceptionTypes`, `FailWhenRuleCount`, `GetDelayedRetryBaseDelay`, `GetDelayedRetryDelay`, `ShouldFail`, `With`, and `None`. `Create<TPolicy>()` builds a policy type's definition, and `Create(configure)` builds one described inline on a fresh builder. |
| `FailurePolicyOverrides` | Nullable numeric values that `With` applies over a resolved definition. `TryParse(settings, errors, out overrides)` reads them from `(Key, Path, Value)` settings, adds one error per unknown key or unparseable value, and returns `null` overrides on any error. |
| `FailurePolicyDefinitionExtensions` | `definition.WriteDescription(Utf8JsonWriter)` writes the policy as the JSON object that `describe` shows. |

`ShouldFail(exception, out ruleException)` returns a non-null `ruleException` when a `FailWhen` predicate threw; the short `ShouldFail(exception)` overload hides it. Pass the handler's own exception, not a wrapper: `FailOn<T>()` matches by type, so a wrapper type hides the rule. `GetDelayedRetryBaseDelay(delayedAttempt)` is deterministic, so store it where a schedule is persisted. `GetDelayedRetryDelay(delayedAttempt)` adds jitter from `Random.Shared`, and `GetDelayedRetryDelay(delayedAttempt, random)` takes a source, where `NextDouble()` returning 0.5 yields the base delay.

### Design constraints

- Validation runs where a value is written. Each builder call throws `ArgumentOutOfRangeException` naming the bad parameter (`retries`, `initialDelay`, or `maxDelay`), and `With` throws the same way for invalid combined values. A policy whose `Configure` writes an invalid value throws from `Build()`.
- A built definition is unaffected by later changes to its builder or to the overrides object passed to `With`.
- `With` returns the same instance when the overrides set nothing.
- `GetDelayedRetryBaseDelay` and `GetDelayedRetryDelay` throw `ArgumentOutOfRangeException` for an attempt number below 1.
