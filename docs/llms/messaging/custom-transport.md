---
domain: Messaging
packages:
---

# Writing a transport provider

> Part of [Messaging](../messaging.md). How to adapt another broker to `Headless.Messaging` through the public transport seams, and what a transport owes the core.

## Writing a Transport Provider

Read this only when no shipped transport fits and you are writing one for another broker. A transport package adapts one broker. The core already owns serialization, the outbox, retries, delayed publishing, consumer invocation, the circuit breaker, and diagnostics, so a transport must not reimplement those policies. A transport package normally owns:

- a setup class exposing `UseMyBroker(...)` on `MessagingSetupBuilder`
- `MyBrokerOptions` plus a validator
- an `IBusTransport` and/or `IQueueTransport` implementation
- `MyBrokerConsumerClientFactory : IConsumerClientFactory`
- `MyBrokerConsumerClient : IConsumerClient`
- broker-specific pools or factories when connection reuse matters

A custom storage provider has partial parity: `IDataStorage` and its optional capabilities are public, but the transactional inbox and unit-of-work enlistment are internal. See [Writing a storage provider](custom-storage.md#writing-a-storage-provider).

## Public building blocks

A transport written outside the framework reaches parity with the shipped ones through public API only. The seams live in `Headless.Messaging.Transport`, except `MessageQueueMarkerService`, which is in `Headless.Messaging`:

| Type | Use it to |
| --- | --- |
| `MessageQueueMarkerService` | Name the transport from its `IMessagesOptionsExtension` (one instance, registered as a singleton), so the test harness and the dashboard find it |
| `IMessageMetadataRegistry`, `MessageMetadata`, `MessageRouteKey` | Read the declared routes: names, lanes, raw-payload and affinity flags, and the provider configs an application attached |
| `IMessageProviderConfigBuilder<TMessage>`, `IConsumerProviderConfigBuilder`, `ConsumerRegistryExtensions.ResolveConsumerConfig<TConfig>` | Attach a per-contract or per-consumer config from a `Use{Provider}` extension, and read the consumer config back when a client opens a subscription |
| `IProviderHeaderContributions`, `ProviderHeaderContribution`, `IProviderMissingPayloadValidation` | Optional capabilities of a per-contract config: headers computed from the payload at publish, and refusing a publish with no payload |
| `TransportEnvelopeGate`, `EnvelopeArrival`, `NativeEnvelopeFacts` | Run the check every delivery passes before the core sees it, and complete a raw-payload envelope from broker metadata |
| `ConsumerPauseGate`, `InFlightHandlerTracker` | Back `PauseAsync`/`ResumeAsync`, and drain running handlers before a client disposes what settlement needs |
| `BrokerConnectGuard`, `BrokerFailures`, `BrokerReachabilityReporter` | Classify connect and receive failures the way the core counts [broker reachability](operations.md#broker-reachability) |
| `BusNameBuilder`, `BusNameRules`, `TransportNaming` | Derive broker-legal subscription, queue, and stream names every process agrees on |
| `RedeliveryBackoff`, `DeliverByEnvelope`, `TransportBody`, `LogSanitizer` | Delay a redelivery, read the `DeliverWithin` deadline, hand a body to a client that takes only arrays, and sanitize broker text before it reaches a log |
| `ReplyListenerSupervisor`, `ReplyAddressGate`, `ReplyHandlerInvoker` | Keep a request/reply channel open across broker outages and hand replies to the core one at a time |
| `TransportMetrics`, `MessagingDiagnostics.Meter` | Record settlement failures and transport-side every-instance drops on the core's instruments, and add broker instruments to the messaging meter |

Each provider interface states its evolution policy in its XML docs. An interface grows through optional capability interfaces the core discovers by a type check, or through default interface members with a behavior-preserving fallback. Existing members never gain parameters. The helper classes are sealed or static.

A consumer client built on these pieces:

```csharp
using System.Globalization;
using Headless.Messaging.Transport;
using Headless.Threading;

// Stands in for the broker's client library.
public interface IMyBrokerSession : IAsyncDisposable
{
    Task<MyBrokerDelivery?> ReceiveAsync(CancellationToken cancellationToken);
    Task AckAsync(long tag, CancellationToken cancellationToken);
    Task NackAsync(long tag, CancellationToken cancellationToken);
}

public interface IMyBrokerConnector
{
    Task<IMyBrokerSession> ConnectAsync(string queue, CancellationToken cancellationToken);
}

public sealed record MyBrokerDelivery(long Tag, string Queue, long Offset, Dictionary<string, string?> Headers, byte[] Body);

public sealed class MyBrokerConsumerClientFactory(IMyBrokerConnector connector, IServiceProvider services)
    : IConsumerClientFactory
{
    // The broker accepts letters, digits, '-', '_', and '.', up to 120 characters.
    private static readonly BusNameRules _QueueRules = new(maxLength: 120, isAllowed: static c => c is '-' or '_' or '.');

    public async Task<IConsumerClient> CreateAsync(
        ConsumerClientRequest request,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var queue = request.Lane == MessageLane.Bus
            ? BusNameBuilder.Build(request, _QueueRules)
            : request.SubscriptionName;

        try
        {
            var session = await connector.ConnectAsync(queue, cancellationToken).ConfigureAwait(false);
            var gate = services.GetService<TransportEnvelopeGate>() ?? TransportEnvelopeGate.Strict;

            return new MyBrokerConsumerClient(request.Lane, session, gate);
        }
        catch (Exception e)
        {
            // Cancellation stays cancellation; anything else is a broker connection failure.
            throw BrokerConnectGuard.ConnectFailure(e, cancellationToken);
        }
    }
}

public sealed class MyBrokerConsumerClient(MessageLane lane, IMyBrokerSession session, TransportEnvelopeGate envelopeGate)
    : IConsumerClient
{
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly InFlightHandlerTracker _inFlight = new();
    private readonly BrokerReachabilityReporter _reachability = new();

    // A lost broker is waited for in real time, never on the app clock a test host fakes.
    private readonly ReconnectBackoff _backoff = new();

    public BrokerAddress BrokerAddress { get; } = new("MyBroker", "broker.internal:5000");

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; private set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; private set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public ValueTask SubscribeAsync(IEnumerable<string> messageNames, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _pauseGate.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);

            MyBrokerDelivery? delivery;
            try
            {
                delivery = await session.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (BrokerFailures.IsConnectivity(e))
            {
                // The session reconnects on its own; the host still learns the broker went away. A caught
                // connectivity failure must never fall through to the success report below.
                _reachability.ReportFailure(OnLogCallback, e);

                if (!await _backoff.WaitAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                continue;
            }

            _backoff.Reset();
            _reachability.ReportSuccess(OnLogCallback);

            if (delivery is null)
            {
                continue;
            }

            delivery.Headers[Headers.TransportAddress] = delivery.Queue;

            try
            {
                envelopeGate.Admit(
                    delivery.Headers,
                    new EnvelopeArrival("MyBroker", lane, ArrivalName: null),
                    delivery,
                    static d => new NativeEnvelopeFacts(
                        ProducerMessageId: null,
                        PositionId: NativeEnvelopeFacts.Position(
                            "mybroker",
                            d.Queue,
                            d.Offset.ToString(CultureInfo.InvariantCulture)
                        ),
                        StoredAt: null
                    )
                );
            }
            catch (InvalidDataException e)
            {
                // A malformed envelope never reaches a handler; settle it so the broker stops redelivering it.
                OnLogCallback?.Invoke(
                    new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = LogSanitizer.Sanitize(e.Message, 255) }
                );
                await session.AckAsync(delivery.Tag, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            _inFlight.Track(_HandleAsync(delivery));
        }
    }

    private async Task _HandleAsync(MyBrokerDelivery delivery)
    {
        try
        {
            await OnMessageCallback!(new TransportMessage(delivery.Headers, delivery.Body), delivery.Tag)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // The core settles every handler outcome itself, so a throw means nothing settled this delivery.
            OnLogCallback?.Invoke(new LogMessageEventArgs { LogType = MqLogType.ConsumeError, Reason = e.Message });
            await session.NackAsync(delivery.Tag, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default) =>
        await session.AckAsync((long)sender!, cancellationToken).ConfigureAwait(false);

    public async ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default) =>
        await session.NackAsync((long)sender!, cancellationToken).ConfigureAwait(false);

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.PauseAsync().ConfigureAwait(false);

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default) =>
        await _pauseGate.ResumeAsync().ConfigureAwait(false);

    public async ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _pauseGate.Release();

        // Handlers settle against the session, so they finish before it closes. The budget is real time.
        await _inFlight.DrainAsync(timeout, TimeProvider.System).ConfigureAwait(false);
        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _pauseGate.Release();
        await session.DisposeAsync().ConfigureAwait(false);
    }
}
```

A per-contract config that computes a header from the payload, attached from the provider's own `Use{Provider}` extension:

```csharp
using Headless.Messaging.Transport;

public sealed class MyBrokerMessageConfig<TMessage>(Func<TMessage, string?> partitionKey) : IProviderHeaderContributions
    where TMessage : class
{
    public IReadOnlyList<ProviderHeaderContribution> HeaderContributions { get; } =
        [new("mybroker-partition-key", payload => partitionKey((TMessage)payload))];
}

public static class MyBrokerMessageBuilderExtensions
{
    public static IQueueContractBuilder<TMessage> UseMyBroker<TMessage>(
        this IQueueContractBuilder<TMessage> builder,
        Func<TMessage, string?> partitionKey
    )
        where TMessage : class
    {
        ((IMessageProviderConfigBuilder<TMessage>)builder).SetMessageProviderConfig(
            new MyBrokerMessageConfig<TMessage>(partitionKey)
        );

        return builder;
    }
}
```

## Registration shape

Register the broker through `MessagingSetupBuilder.RegisterExtension(...)` with an `IMessagesOptionsExtension` that adds:

- `MessageQueueMarkerService("MyBroker")`
- validated broker options
- `MessagingProviderCapabilities` contributions declaring the supported lanes and native affinity routes
- singleton `IBusTransport` and/or `IQueueTransport` for the declared lanes
- singleton `IConsumerClientFactory`
- broker-owned singletons such as connection pools

```csharp no-compile
public static class SetupMessagesMyBroker
{
    extension(MessagingSetupBuilder setup)
    {
        public MessagingSetupBuilder UseMyBroker(Action<MyBrokerOptions> configure)
        {
            setup.RegisterExtension(new MyBrokerOptionsExtension(configure));
            return setup;
        }
    }

    private sealed class MyBrokerOptionsExtension(Action<MyBrokerOptions> configure) : IMessagesOptionsExtension
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton(new MessageQueueMarkerService("MyBroker"));
            services.Configure<MyBrokerOptions, MyBrokerOptionsValidator>(configure);
            services.AddMessagingProviderCapabilities(MessagingProviderCapabilities.Transport(
                "MyBroker", [MessageLane.Queue], supportsIndependentLaneTopology: true));
            services.AddSingleton<IQueueTransport, MyBrokerTransport>();
            services.AddSingleton<IConsumerClientFactory, MyBrokerConsumerClientFactory>();
        }
    }
}
```

**Verify:** the `describe` host manifest's `messaging.transports` names your `BrokerAddress.Name` for each declared lane, and a consumer on an undeclared lane fails startup with a `MessagingConfigurationException` naming the provider.

To support [named transports](transports.md#named-transports), add the same members to `MessagingTransportBuilder` and register through `transport.RegisterTransport(extension)`. The extension registers everything keyed by `transport.Name`, builds every broker-owned object from options named for that instance (never from the default options or a shared singleton), and marks itself with an unkeyed `MessageQueueMarkerService("MyBroker") { TransportName = name }`:

```csharp no-compile
extension(MessagingTransportBuilder transport)
{
    public MessagingSetupBuilder UseMyBroker(Action<MyBrokerOptions> configure) =>
        transport.RegisterTransport(new NamedMyBrokerOptionsExtension(transport.Name, configure));
}

private sealed class NamedMyBrokerOptionsExtension(string name, Action<MyBrokerOptions> configure)
    : IMessagesOptionsExtension
{
    private readonly string _optionsName = "Headless.Messaging.Transport." + name;

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton(new MessageQueueMarkerService("MyBroker") { TransportName = name });
        services.Configure<MyBrokerOptions, MyBrokerOptionsValidator>(configure, _optionsName);
        services.AddMessagingProviderCapabilities(name, MessagingProviderCapabilities.Transport(
            "MyBroker", [MessageLane.Queue], supportsIndependentLaneTopology: true));
        services.AddKeyedSingleton<IQueueTransport>(name, (sp, _) => new MyBrokerTransport(_Options(sp)));
        services.AddKeyedSingleton<IConsumerClientFactory>(name, (sp, _) => new MyBrokerConsumerClientFactory(_Options(sp)));
    }

    private IOptions<MyBrokerOptions> _Options(IServiceProvider sp) =>
        Options.Create(sp.GetRequiredService<IOptionsMonitor<MyBrokerOptions>>().Get(_optionsName));
}
```

A keyed `ITransportTopologyProvisioner` becomes the named transport's `topology-{name}` resource. A named transport registers no `IReplyTransport` and declares `supportsRequestReply: false`.

**Verify:** with `setup.AddTransport("partner").UseMyBroker(...)`, the `describe` host manifest lists `partner` under `messaging.transports.named`, and `messaging-broker-partner` appears among the readiness checks.

`MessagingProviderCapabilities.Transport(...)` declares lanes, `supportsIndependentLaneTopology`, `supportsEveryInstance`, `supportsSingleActiveConsumer`, `supportsRequestReply`, and `routingAffinityRoutes`. The core freezes these declarations at bootstrap and validates consumers and contracts against them before readiness. Raw DI registration is not capability evidence.

## Publishing: ITransport.SendAsync

`SendAsync(...)` receives a fully prepared `TransportMessage`.

- Publish `message.Body` as the broker payload and preserve `message.Headers`.
- Return `OperateResult.Success` when the broker accepts it. Let `OperationCanceledException` propagate.
- Observe only the `cancellationToken` the core passes, never a host-stopping token of your own. The core cancels it at `MessagingOptions.TransportPublishTimeout`, and not on host shutdown, so a send in flight when the host stops still waits for the broker's confirm.
- Return `OperateResult.BrokerUnreachable(new PublisherSentFailedException(...))` when the broker could not be reached (a lost connection, a timeout, a broker that reports itself down). It counts toward [broker reachability](operations.md#broker-reachability).
- Return `OperateResult.Failed(new PublisherSentFailedException(...))` when the broker refused this message. It does not count toward reachability.
- After `DisposeAsync`, return `OperateResult.Failed(new ObjectDisposedException(...))` without contacting the broker, and do not throw. This holds even when a shared pool owns the connection.
- `BrokerAddress` feeds diagnostics, OpenTelemetry (`messaging.system`, `server.address`), the host manifest, and the dashboard. Make it a sanitized operator-facing value, never a connection string with credentials.

## Consuming: IConsumerClientFactory and IConsumerClient

`IConsumerClientFactory.CreateAsync(ConsumerClientRequest, ...)` is called once during startup topology discovery with the host-stopping token, and once per live consumer client with a linked per-client token. Make it safe to call repeatedly, and start no receive loop in construction. Never wrap shutdown cancellation as `BrokerConnectionException`: let `OperationCanceledException` escape unchanged from `CreateAsync`, `FetchMessageNamesAsync`, and `SubscribeAsync`.

The request carries `SubscriptionName` (the consumer identity on the Bus lane, the message name on the Queue lane), `Concurrency`, `Lane`, `Kind` (`Competing` or `EveryInstance`), the host's `InstanceId`, `ProvisionsTopology`, and `SingleActiveConsumer`.

On the Bus lane, derive the broker-legal name with `BusNameBuilder.Build(...)` and a `BusNameRules` for the broker (maximum length, accepted characters, whether names must start and end with a letter or digit). It follows the rule in [Bus subscription names](transports.md#bus-subscription-names). Never hash with `string.GetHashCode()`, which differs per process.

To let environments share the broker, register `services.ApplyHeadlessPrefix<TOptions>((options, prefix) => ...)` from `Headless.Hosting` in the transport's setup and put the prefix in front of every broker name the transport derives, joined with a separator the prefix cannot contain (`.`, `_`, or `:`), before the length rules apply. Keep message names and headers unprefixed, and map a prefixed arrival back to its message name. With no prefix the callback never runs. See [Host prefix](transports.md#host-prefix).

- **`FetchMessageNamesAsync`** normalizes and provisions: create topics, streams, queues, or subscriptions, translate wildcard topics, and map names to broker identifiers such as ARNs. The default pass-through is enough when the broker uses names as-is. Pass the token through broker and topology calls. When an SDK call takes no token, await it through a cancellation-aware wait and keep the provider's own timeout.
- **`SubscribeAsync`** binds the client's subscription to the names `FetchMessageNamesAsync` resolved, with the same token rules.
- **`ListeningAsync`** owns the receive loop. For every delivery, build a `TransportMessage`, leave `Headers.ConsumerIdentity` alone (the core stamps it), and pass a broker-specific commit token to `OnMessageCallback(message, commitToken)`. The core settles every handler outcome itself, so its callback throws only when settlement failed or the core broke. Then nothing settled the delivery: log the exception, release the delivery for redelivery (reject, nack, abandon, or seek back) unless the callback already settled it, and keep receiving. If releasing fails too, fail the listener so the core rebuilds the client.
  - **It runs until its token is canceled.** The returned task is the loop's lifetime: complete it only when `cancellationToken` is canceled or the client can no longer receive. A push-based client that starts the broker's own receivers (processors, push consumers) and has nothing left to do still waits for the token, as does a client subscribed to nothing. On cancellation, stop taking new deliveries but leave the in-flight ones settleable; `ShutdownAsync`, which the core calls next, drains and closes them.
  - **An early end is a failure.** When `ListeningAsync` returns, or throws an `OperationCanceledException` from a token of its own, while the core's token is not canceled, the core marks that subscription unhealthy, logs EventId 4146 `ConsumerClientStoppedListening` (or EventId 37 for the exception), and the transport check rebuilds it. A sequential subscription that runs on one host under the active-host lock logs the same event, releases the lock, and competes for it again. Throw the exception that ended the loop, so the log says why.
- **`CommitAsync` and `RejectAsync`** map the commit token back to broker semantics: ack or nack, delete or abandon, commit or seek, complete, dead-letter, or requeue. If the broker cannot reject, implement the best available no-op or requeue explicitly.
- **`RejectAsync(sender, redeliveryDelay, cancellationToken)`** is what the core calls for a delivery it could not admit (its store failed, or its circuit's half-open probe is out). The default holds the delivery for `redeliveryDelay` in real time on `TimeProvider.System`, then calls the plain `RejectAsync`. The token only cuts the wait short on shutdown, and the reject still runs. Override it when the broker can delay redelivery itself (a NAK delay, a visibility timeout, a pending-entry idle time), so the delivery is released at once.
- **`DeadLetterAsync(sender, reason, description)`** settles a message poisoned on arrival after the core stored its poison row. The core calls it only for the delivery that stored the row. The default calls `CommitAsync`, so a broker without a dead-letter destination needs no change. Override it when the broker can keep the message aside, and attach the reason and description when the broker can carry them. Reason codes: `SubscriberNotFound`, `ReceiveRejected` (a receive middleware's `Reject`), `DeserializationFailed`, and `ReceiveFailed` (a contract-version mismatch, a receive middleware fault, or an undeclared outcome). The description is the sanitized exception message capped at 1024 characters, except for `ReceiveFailed`, which carries only the exception type, because a middleware's message can echo header values such as a token.
- **`PauseAsync` and `ResumeAsync`** serve circuit-breaker backpressure. Make them idempotent and safe to call concurrently, and stop new pulls once `PauseAsync` returns. In-flight deliveries may complete. Keep the listener alive across pause and resume. A provider may cancel an in-flight receive to reach its pause gate, but must install fresh receive state before it reopens the gate.
- **`OnLogCallback`** emits `MqLogType` events. Core attaches the callback after it creates the client, so read it at each event and never assume it is set: a client used before then, such as one a test fixture creates, must not throw or drop the event. The built-in Kafka, RabbitMQ, and Azure Service Bus clients write an event raised with no callback to their own logger as a warning.
  - `ConsumerCancelled`, `ConsumerShutdown`, `ServerConnError`, and `ConnectError` mark that client degraded. `ConsumerRegistered` clears only that client. A `ConsumerCancelled` raised while the circuit breaker paused the subscription changes nothing. Every 30 seconds the transport check rebuilds each subscription that holds a degraded client and leaves healthy ones consuming. A failure that belongs to no subscription rebuilds every subscription.
  - A client that retries a lost broker inside its own loop, instead of failing the loop, reports each failed receive as `MqLogType.BrokerUnreachable` and the first success after one as `MqLogType.BrokerReachable`. The core counts these toward reachability without rebuilding the client.
  - Emit `MqLogType.SettlementFailed` when an ack did not reach the broker, and record it with `TransportMetrics.RecordSettlementFailure`. The core logs it as a warning (EventId 4120 `TransportSettlementFailed`). Do not throw from settlement: the broker redelivers and the inbox deduplicates.
  - Other receive-error types are only logged.
- **`DisposeAsync`** disposes only what that client owns, never shared pools or connections.
- **Every-instance requests.** A transport that supports every-instance subscriptions declares `supportsEveryInstance: true` in `MessagingProviderCapabilities.Transport(...)`. The flag requires the Bus lane: setting it without `MessageLane.Bus` throws `ArgumentException`. Without it, an every-instance consumer on the transport fails startup with a `MessagingConfigurationException` naming the consumer and the provider. A request with `Kind` `EveryInstance` is a per-process subscription named from the identity and `InstanceId`. The broker must remove it after the process stops or crashes, and the core opens exactly one client for it whatever `ConsumerThreadCount` is. A transport that recovers its connection internally reports each re-established subscription through the callback the core attaches with `IConsumerClient.AttachReestablishedCallback(Func<CancellationToken, Task>?)`, so the consumer's `IOnSubscriptionEstablished` hook fires for gaps the core never saw. The default implementation is a no-op, which is correct for a client that fails its listener and lets the core rebuild it: the core raises the same signal after every rebuild. See [Every-instance Bus delivery](consumers.md#every-instance-bus-delivery).

## Provisioning: ITransportTopologyProvisioner

A transport whose broker holds shared objects implements `ITransportTopologyProvisioner` and registers it from its `Use{Provider}` setup with `services.TryAddEnumerable(ServiceDescriptor.Singleton<ITransportTopologyProvisioner, TProvisioner>())`. Messaging turns each one into a [provisioned resource](../utilities.md#resource-provisioning) of kind `Kind` (the broker name, such as `RabbitMQ`) named `topology`, so `resources provision` creates the topology and the running host only checks it ([Topology check](transports.md#topology-check)).

- **Input.** Every operation receives a `MessagingTopology` built from the registered consumers and messages: `Subscriptions` (`TopologySubscription`: `SubscriptionName`, `Lane`, `Kind`, `MessageNames`, and `SingleActiveConsumer`) and `Messages` (`TopologyMessage`: `MessageName`, `Lane`, for every registered message, consumed here or only published). Subscriptions an `IRuntimeSubscriber` adds while the host runs are never in it.
- **Operations.** `ProvisionAsync` creates what is missing. `VerifyAsync` creates nothing and throws `ResourceProvisioningException` naming `resources provision` when an object is missing. `PurgeAsync` removes queued messages and keeps the objects. `DeleteAsync` deletes what `ProvisionAsync` created that this host owns. Purge and delete act only on objects this host consumes from, never on a queue it only publishes to or an object every service shares. `GetStatusAsync` returns ordered name and value pairs. Make each operation idempotent. Return `false` from `CanPurge` or `CanDelete` when the broker cannot express the operation; the resource then reports it as unsupported.
- **Skip per-process objects.** A `ConsumerSubscriptionKind.EveryInstance` subscription's objects belong to one process, which creates them when it subscribes.
- **Consumer clients follow.** Once a transport provisions here, its clients create shared objects only when `ConsumerClientRequest.ProvisionsTopology` is `true` (a runtime subscription). Otherwise they declare nothing, not even a passive check. A client that consumes a missing object fails with `ResourceProvisioningException` naming `resources provision`.
- **Startup check.** `ITransportTopologyProvisioner.StartupCheck` defaults to `ResourceStartupCheck.GateReadiness`.

## Header and payload rules

- Round-trip at least `Headers.MessageId`, `Headers.MessageName`, `Headers.Type`, `Headers.CorrelationId`, `Headers.CorrelationSequence`, and `Headers.SentTime`. Preserve optional headers such as `Headers.CallbackName`, `Headers.DelayTime`, `Headers.DeliverBy`, `Headers.TenantId`, `Headers.TraceParent`, and custom application headers.
- On the Queue lane, preserve `Headers.RequestId`, `Headers.ReplyTo`, and `Headers.RequestDeadline`. A reply transport preserves `Headers.InReplyTo`, `Headers.ReplyStatus`, `Headers.MessageId`, `Headers.MessageName`, `Headers.ContractVersion`, `Headers.TenantId`, and `Headers.TraceParent` on every reply.
- Never set `Headers.ConsumerIdentity` (`headless-msg-consumer-identity`). The core stamps it on receipt.
- Set `Headers.TransportAddress` (`headless-transport-address`) on receipt to the native address the delivery arrived on, after reading wire headers and after any `CustomHeadersBuilder`, so neither can choose it. Never send it.
- Round-trip `Headers.TenantId` verbatim. Never originate, rewrite, or strip it.
- Treat the body as raw bytes unless the broker API forces an encoding.
- Never leak exception details, credentials, or secrets through headers or `BrokerAddress`.

## Routing-affinity contributions

`TransportMessage.RoutingAffinityKey` reads the reserved `headless-routing-affinity-key` envelope field. Do not accept it through custom headers or a provider header contribution.

A provider declares `MessagingRoutingAffinityRoute` entries in its `MessagingProviderCapabilities.Transport(...)` contribution. Each entry names one registered `(MessageLane, MessageName)` destination and a `MessagingRoutingAffinityMapping`: the native header adapter, an optional maximum key length, a printable-ASCII restriction, and other raw headers that must match. An empty list means no native affinity destinations. A contribution may snapshot inert options and registrations. It must never resolve a broker client, transport, processor, or storage to discover support.

```csharp
MessagingProviderCapabilities.Transport(
    "MyBroker",
    [MessageLane.Queue],
    supportsIndependentLaneTopology: true,
    routingAffinityRoutes:
    [
        new(MessageLane.Queue, "orders.changed", new MessagingRoutingAffinityMapping("my-native-key")),
    ]);
```

- Validate raw application headers before evaluating a selector that could overwrite them, then validate the resulting contribution again. Validate before renting producers or creating senders, including on retry dispatch.
- Do not guess support from a provider-wide flag, and do not auto-provision a different topology for an unknown keyed destination.
- For a route with no native mapping, send a keyed message as any other: the key stays in the envelope headers, which a sequential consumer orders by. Do not reject it, and deliver the `headless-routing-affinity-key` header unchanged, on redelivery too.
- Startup checks prove only local consistency. Prove the broker's actual session, FIFO, or partition topology and permissions separately, for direct publication, outbox dispatch, and redelivery.

## Single active consumer

A broker that can deliver one subscription to one consumer at a time across every process declares `supportsSingleActiveConsumer: true`. The core then asks for it on each [sequential](ordering.md#sequential-consumers) competing subscription that no routing-affinity route fully covers, instead of electing one host through the messaging lock.

- `TopologySubscription.SingleActiveConsumer` and `ConsumerClientRequest.SingleActiveConsumer` are `true` for that subscription. A provisioner creates the subscription's queue with the broker's setting, and a client that declares its own queue (`ProvisionsTopology`) does the same. Verification stays a passive existence check.
- Every host keeps its client open, and the broker picks the active consumer and fails over. Never open a second, ungated consumer on the subscription.
- The flag composes as `true` only when every transport contribution declares it. A transport that declares nothing leaves sequential subscriptions to the messaging lock.

```csharp
MessagingProviderCapabilities.Transport(
    "MyBroker",
    [MessageLane.Bus, MessageLane.Queue],
    supportsIndependentLaneTopology: true,
    supportsSingleActiveConsumer: true);
```

## Request/reply: IReplyTransport

A provider supports [request/reply](request-reply.md#requestreply) by declaring `supportsRequestReply: true` (which requires the Queue lane) and registering a singleton `IReplyTransport`. The core rejects request/reply at startup on a transport that does not declare it. The core owns correlation, deadlines, tenant checks, fault encoding, and the decision to send. The reply transport only opens a per-process channel and moves replies on it.

- `IReplyTransport.OpenListenerAsync(onReply, cancellationToken)` opens the process's reply channel and returns an `IReplyListener`. Give each listener a fresh address, normally `ReplyAddresses.Create()` (`headless.reply.` plus 32 hex digits), so a reply to a previous run never reaches it. Invoke `onReply` once per reply, one at a time. An exception it throws is logged and does not stop the listener. Do not dispose the listener from inside `onReply`.
- `IReplyListener.WaitForAddressAsync(cancellationToken)` waits until the channel can receive and returns its current address. It throws `ObjectDisposedException` once the listener is closed. While the connection is down, withhold the address. After a reconnect, keep the same address only when no other process can hold it; otherwise return a new one. Disposing the listener removes every broker object it created, and a crash must leave none behind.
- `IReplyTransport.SendAsync(address, reply, cancellationToken)` writes one reply. Throw `ArgumentException` for an address outside the reply namespace, and drop the reply without an error when nobody listens. Never store a reply or write it to a lane destination.
- `IReplyTransport.IsReplyAddress(address)` defaults to `ReplyAddresses.IsInReplyNamespace`: the `headless.reply.` prefix, a non-empty remainder of ASCII letters, digits, `.`, `-`, and `_`, and at most `ReplyAddresses.MaxLength` (200) characters. Override it only to refuse more. A request carries its own reply address, so on the responding host the address is untrusted input.

```csharp no-compile
services.AddMessagingProviderCapabilities(MessagingProviderCapabilities.Transport(
    "MyBroker", [MessageLane.Queue], supportsIndependentLaneTopology: true, supportsRequestReply: true));
services.AddSingleton<IReplyTransport, MyBrokerReplyTransport>();
```

Declare the flag only when the provider passes these scenarios: round trip, callers receiving only their own replies, responder faults, `no_responder`, timeouts that never run the responder, late replies dropped, foreign reply addresses never written, a restarted caller never receiving an old reply, tenant flowing both ways, and no reply objects left after the caller stops.

**What a transport must not do:**

- reimplement serialization handled by `IMessageSerializer`
- add its own retry policy around `OnMessageCallback`
- commit before the core finishes processing the message
- hide broker failures by swallowing exceptions and returning success
- expose credentials in logs, exceptions, or `BrokerAddress`
- couple itself to one application's consumer registration conventions
