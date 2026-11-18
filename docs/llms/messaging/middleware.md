---
domain: Messaging
packages:
---

# Middleware

> Part of [Messaging](../messaging.md). Add cross-cutting behavior around publish, receive, and consume, and propagate the tenant.

## Middleware

Messaging runs middleware at three stages:

| Stage | Contract | Sees |
| --- | --- | --- |
| Publish | `IPublishMiddleware<TContext>` where `TContext : PublishContext` | The typed message before serialization |
| Receive | `IReceiveMiddleware` on `ReceiveContext` | The raw envelope, after the consumer is resolved and before the contract-version check and deserialization |
| Consume | `IConsumeMiddleware<TContext>` where `TContext : ConsumeContext` | The typed message after deserialization and inbox admission |

A middleware gets one context and `Func<ValueTask> next`. Code before `await next()` runs before the inner ring. Code after it runs after the inner ring succeeds. Wrap `await next()` in `try/catch` for compensation or error policy. Return without calling `next` to short-circuit.

```csharp
public sealed class SignatureVerificationReceiveMiddleware : IReceiveMiddleware
{
    public ValueTask InvokeAsync(ReceiveContext context, Func<ValueTask> next)
    {
        if (!context.Headers.ContainsKey("x-signature"))
        {
            context.Reject("Missing signature header");
            return ValueTask.CompletedTask;
        }

        return next();
    }
}

public sealed class AuditConsumeMiddleware(ILogger<AuditConsumeMiddleware> logger)
    : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        logger.LogInformation("Consuming {MessageId}", context.MessageId);
        await next();
    }
}

public sealed class CorrelationPublishMiddleware
    : IPublishMiddleware<PublishContext<OrderPlaced>>
{
    public ValueTask InvokeAsync(PublishContext<OrderPlaced> context, Func<ValueTask> next)
    {
        context.Options = (context.Options ?? new PublishOptions()) with
        {
            CorrelationId = context.Options?.CorrelationId ?? Guid.NewGuid().ToString(),
        };

        return next();
    }
}

var messaging = builder.Services.AddHeadlessMessaging(setup => { /* ... */ });
messaging.AddReceiveMiddleware<SignatureVerificationReceiveMiddleware>();
messaging.AddBusConsumeMiddleware<AuditConsumeMiddleware>();
messaging.AddPublishMiddlewareFor<CorrelationPublishMiddleware, OrderPlaced>(MessageLane.Bus);

// One consumer only:
builder.Services.ConfigureMessaging(m => m.Tune("orders.projection", c => c.UseMiddleware<AuditConsumeMiddleware>()));
```

**Verify:** the host manifest lists per-consumer middleware under `messaging.consumers[].middleware` (see [operations.md § Host manifest](operations.md#host-manifest)); a test with `MessagingTestHarness` asserts the middleware's effect.

### Registration scopes

`AddHeadlessMessaging(...)` returns the `MessagingBuilder` that registers middleware:

- `AddBusPublishMiddleware<T>()` and `AddBusConsumeMiddleware<T>()` run on every Bus publish or consume. `AddQueuePublishMiddleware<T>()` and `AddQueueConsumeMiddleware<T>()` do the same on the Queue lane. Register a type through both to run it on both lanes.
- A builder registration runs only on its lane. Middleware added straight into DI as `IPublishMiddleware<PublishContext>` or `IConsumeMiddleware<ConsumeContext>`, without the builder, runs on both lanes.
- `AddReceiveMiddleware<T>()` runs on both lanes for every consumer.
- `AddPublishMiddlewareFor<TMiddleware, TMessage>(lane)`, `AddReceiveMiddlewareFor<TMiddleware, TMessage>(lane)`, and `AddConsumeMiddlewareFor<TMiddleware, TMessage>(lane)` run for one message type on one lane.
- `Tune(identity, c => c.UseMiddleware<TMiddleware>())` runs consume middleware for one consumer. It implements the untyped `IConsumeMiddleware<ConsumeContext>`, because one consumer may handle several messages. It runs innermost and is resolved from the delivery scope, registered as scoped when it is not already. The same type tuned twice runs once.
- **Ordering.** Each builder call returns a handle with `.WithPriority(int)`. Lower priority runs first and wraps later middleware. Ties keep registration order. The default is `0`; the built-in tenant propagation uses `-1000`. Lane-wide middleware always wraps per-message middleware, whatever the priorities.
- **Register before the host starts.** Registering middleware through the builder after messaging has started throws `InvalidOperationException`.

### Guarantees

- An exception thrown after `next()` succeeded is logged and suppressed, so the publish or consume is not retried twice.
- An `OperationCanceledException` for `context.CancellationToken` is never swallowed. After a middleware returns normally, the pipeline throws if that token is cancelled.
- **Swapping the cancellation token.** A middleware that creates its own token calls `context.SetCancellationToken(...)` before `await next()`. Downstream middleware re-reads `context.CancellationToken` at each await instead of capturing it once.

### Publish context

The delivery mode, `Delay`, and `ScheduledAt` are frozen before publish middleware runs. Middleware may change other options before `await next()`. Every change throws after the inner publisher completes. `PublishContext.IsTransactional` is `true` only when the publish writes into the caller's unit of work.

To unit-test publish middleware, construct `new PublishContext<T>(content, lane, options, defaultDeliveryMode, now, isTransactional, isStorageSupported, requireCoordination, cancellationToken)`. `requireCoordination: true` models `unit.Outbox` (always `Durable`); `false` models `IBus` or `IQueue`. The constructor rejects invalid lanes, modes, and delays, both schedules at once, `Direct` with a schedule or with `requireCoordination`, and `requireCoordination` with `isTransactional: false`. `PublishAt` is the schedule floored to microseconds. A constructed context stays mutable until `MarkCompleted()`.

## Receive Middleware

Receive middleware sees the raw envelope before deserialization, with the consumer known: `MessageType`, `ConsumerContractVersion`, `MessageName`, `ConsumerIdentity`, and `Lane`.

- **Views and edits.** `context.Headers` and `context.Body` show the envelope as later components will see it. `SetHeader(key, value)`, `RemoveHeader(key)`, and `ReplaceBody(bytes)` edit a copy. `headless-msg-id`, `headless-msg-name`, `headless-msg-consumer-identity`, `headless-exception`, and `headless-transport-address` cannot be changed.
- **Order verification before transformation.** Register byte-exact verification (an HMAC or signature check) with a lower `Priority` than any middleware that calls `ReplaceBody` or `SetHeader`, because a later component sees only the transformed envelope.

| Outcome | How | Effect |
| --- | --- | --- |
| Accept | `await next()` completes | The message is admitted to the inbox, the transport commits, and the consumer runs |
| Skip | `context.Skip(reason)` | The transport commits and the message is dropped: no storage row, no `OnTerminal`. Request/reply callers time out |
| Reject | `context.Reject(reason, cause)` | A poison row is stored, the message is dead-lettered with reason `ReceiveRejected` where the transport supports it (committed elsewhere), and `OnTerminal` fires with `PermanentFailure`. The circuit breaker is not charged |
| Cancelled | An `OperationCanceledException` for the context token | The delivery is requeued with no storage row |
| Throw | Any other exception, or returning without `next()` and without an outcome | Treated as a failure and reported to the circuit breaker |

The Skip and Reject logs name the middleware that ended the delivery. A rejected envelope is stored as a `data:` URI capped at `MessagingOptions.MaxPoisonEnvelopeBytes` (default 1 MB).

## Tenant propagation

Turn on tenant propagation through the root tenancy setup:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims()) // the ambient tenant that propagation captures
        .Messaging(messaging => messaging.PropagateTenant())
);
```

It registers two middleware on both lanes:

- `TenantPropagationPublishMiddleware` stamps the ambient `ICurrentTenant.Id` on `IBus.PublishAsync` and `IQueue.EnqueueAsync`. An explicit `PublishOptions.TenantId` or `QueueOptions.TenantId` is kept as set; set one to override the ambient tenant.
- `TenantPropagationConsumeMiddleware` runs every Bus and Queue consumer, responders included, inside the envelope tenant's scope (`ICurrentTenant.Change(...)`) under every inbox guarantee.

**Verify:** a consumer test reads `ICurrentTenant.Id` inside the handler and asserts the publisher's tenant. To require a tenant on every publish, add `RequireTenantOnPublish()`; see [publishing.md § Strict Publish Tenancy](publishing.md#strict-publish-tenancy). The trust boundary is in [multi-tenancy.md § Message Consumers](../multi-tenancy/background-work.md).
