# Writing a Headless Messaging Transport Provider

This guide describes what a `Headless.Messaging.*` transport package should do when integrating a new broker with `Headless.Messaging.Core`.

## What the package is responsible for

A transport package adapts one broker to the core runtime. In this repo, the package normally owns:

- `Setup.cs` exposing `UseMyBroker(...)` on `MessagingOptions`
- `MyBrokerOptions` plus a validator
- `MyBrokerTransport : ITransport`
- `MyBrokerConsumerClientFactory : IConsumerClientFactory`
- `MyBrokerConsumerClient : IConsumerClient`
- broker-specific pools, factories, or helpers when connection reuse matters
- a package `README.md` covering broker-specific setup and limitations

The core runtime already owns serialization, outbox behavior, retries, delayed publishing, consumer invocation, circuit breaking, and diagnostics orchestration. The transport package should not reimplement those policies.

## DI and package shape

Register the broker through `MessagingOptions.RegisterExtension(...)`. A transport package should add:

- `MessageQueueMarkerService("MyBroker")`
- validated broker options
- singleton `ITransport`
- singleton `IConsumerClientFactory`
- any broker-owned singletons such as connection pools

```csharp
public static class MessagesMyBrokerSetup
{
    extension(MessagingOptions options)
    {
        public MessagingOptions UseMyBroker(Action<MyBrokerOptions> configure)
        {
            options.RegisterExtension(new MyBrokerOptionsExtension(configure));
            return options;
        }
    }

    private sealed class MyBrokerOptionsExtension(Action<MyBrokerOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("MyBroker"));
            services.Configure<MyBrokerOptions, MyBrokerOptionsValidator>(configure);
            services.AddSingleton<ITransport, MyBrokerTransport>();
            services.AddSingleton<IConsumerClientFactory, MyBrokerConsumerClientFactory>();
        }
    }
}
```

## Runtime contract

### `ITransport`

`ITransport.SendAsync(...)` receives a fully prepared `TransportMessage`.

The transport should:

- publish `message.Body` as the broker payload
- preserve `message.Headers`
- return `OperateResult.Success` on broker success
- return `OperateResult.Failed(new PublisherSentFailedException(...))` on broker failure
- let `OperationCanceledException` propagate

`BrokerAddress` feeds diagnostics, OpenTelemetry, and dashboard surfaces. It should be a sanitized operator-facing value, not a raw connection string with credentials.

### `IConsumerClientFactory`

`CreateAsync(groupName, groupConcurrent)` is called twice in practice:

- once during startup/topic discovery
- once for each live consumer thread

The factory should therefore be safe to call repeatedly, and client construction should not start background receive loops too early.

### `IConsumerClient.FetchTopicsAsync`

This is the broker-normalization and provisioning hook.

Use it for broker-specific work such as:

- creating topics, streams, queues, or subscriptions
- translating wildcard topics
- mapping friendly topic names to broker-native identifiers like ARNs

If the broker uses topic names as-is, the default pass-through behavior is enough.

### `IConsumerClient.SubscribeAsync`

This should bind the current consumer group to the resolved topics produced by `FetchTopicsAsync(...)`.

### `IConsumerClient.ListeningAsync`

This method owns the long-running receive loop.

For every delivery, the consumer client should:

- build a `TransportMessage`
- inject `Headers.Group` with the active group name
- pass a broker-specific commit token to `OnMessageCallback(message, commitToken)`

Do not swallow `OnMessageCallback` exceptions inside the transport. The framework decides whether to commit, reject, retry, or trip the circuit breaker.

### `CommitAsync` and `RejectAsync`

`CommitAsync(sender)` and `RejectAsync(sender)` must map the callback token back to broker semantics:

- ack / nack
- delete / abandon
- commit / seek
- complete / dead-letter / requeue

If the broker cannot reject, make that explicit and implement the best available no-op or requeue behavior.

### `PauseAsync` and `ResumeAsync`

These methods are used by the circuit breaker for transport-level backpressure.

They should be:

- idempotent
- safe to call concurrently
- effective at stopping new message pulls after `PauseAsync` returns

In-flight deliveries may complete naturally.

### `OnLogCallback`

Emit meaningful `MqLogType` events for:

- connection failures
- broker shutdown
- consumer registration and cancellation
- receive-loop errors

This is how transport health and restart behavior stay accurate in the core runtime.

### `DisposeAsync`

Dispose only resources owned by that client instance. Do not tear down shared pools or shared connections still used elsewhere in the package.

## Header and payload rules

Publish-side headers come from the core pipeline. The transport must round-trip at least:

- `Headers.MessageId`
- `Headers.MessageName`
- `Headers.Type`
- `Headers.CorrelationId`
- `Headers.CorrelationSequence`
- `Headers.SentTime`

It should also preserve optional headers such as:

- `Headers.CallbackName`
- `Headers.DelayTime`
- `Headers.TenantId` (multi-tenancy identifier; populated from `PublishOptions.TenantId`, exposed on `ConsumeContext.TenantId`)
- `Headers.TraceParent`
- custom application headers

Additional rules:

- `Headers.Group` is added on consume, not publish
- `Headers.TenantId` is enforced by a strict 4-case integrity policy in the core publish pipeline; transports must round-trip the value verbatim and never originate, rewrite, or strip it
- the body should be treated as raw bytes unless the broker API forces encoding/decoding
- exception details, credentials, and other secrets must not be leaked through headers or `BrokerAddress`

## What the package should not do

The transport package should not:

- reimplement serialization policy already handled by `ISerializer`
- invent its own retry policy around `OnMessageCallback`
- commit before the framework finishes processing the message
- hide broker failures by swallowing exceptions and returning success
- expose raw credentials in logs, exceptions, or `BrokerAddress`
- couple itself to one app's consumer registration conventions

## README checklist for the provider package

Each provider `README.md` should document:

- how to register the transport with `AddHeadlessMessaging(...)`
- required options and credential setup
- publish semantics, including whether broker-side scheduling exists
- consume semantics for commit, reject, redelivery, and dead-letter behavior
- ordering guarantees under broker-native rules and `ConsumerThreadCount`
- auto-provisioning done by `FetchTopicsAsync(...)` or `SubscribeAsync(...)`
- topic naming restrictions, required custom headers, and payload limits

## Good implementation signals

A provider is usually aligned with the framework when:

- direct publishing works through `ITransport` without special-case code in core
- topic provisioning is isolated to `FetchTopicsAsync(...)`
- every consumed message reaches `OnMessageCallback(...)` with a valid `Headers.Group`
- commit/reject behavior is broker-correct and symmetric with the callback token
- pause/resume works without cancelling the receive task
- health and broker failures surface through `OnLogCallback`
