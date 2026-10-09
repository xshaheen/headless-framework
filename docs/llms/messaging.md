---
domain: Messaging
packages: Messaging.Abstractions, Messaging.Bus.Abstractions, Messaging.Queue.Abstractions, Messaging, Messaging.Dashboard, Messaging.Dashboard.K8s, Messaging.Aws, Messaging.AzureServiceBus, Messaging.InMemory, Messaging.Storage.InMemory, Messaging.Kafka, Messaging.Nats, Messaging.Pulsar, Messaging.RabbitMq, Messaging.Redis, Messaging.SourceGenerator, Messaging.Storage.PostgreSql, Messaging.Storage.PostgreSql.EntityFramework, Messaging.Storage.SqlServer, Messaging.Storage.SqlServer.EntityFramework, Messaging.Testing
---

# Messaging

> Messaging is the framework's durable publish/consume layer: typed bus and queue APIs, explicit message registration, storage-backed retry/outbox state, provider-native transports, dashboards, telemetry, and test harness support.

## Orientation

Use `Headless.Messaging` as the composition package, then add exactly one transport provider and one storage provider for a production host. `IBus.PublishAsync` selects broadcast Bus semantics and `IQueue.EnqueueAsync` selects point-to-point Queue semantics; message types remain plain classes, records, or interfaces.

Registration has three parts. A consumer class declares itself with `[BusConsumer(identity)]` or `[QueueConsumer(identity)]` and implements `IConsume<T>` for each message it handles. The Messaging source generator emits one `MessagingModule` per assembly, and the module that owns the consumers contributes it, together with its message contracts, through `services.ConfigureMessaging(...)`. The host calls `AddHeadlessMessaging(...)` once for transport, storage, and options, and tunes consumers by identity.

```csharp
using Microsoft.EntityFrameworkCore;

// Orders assembly: message, consumer, and the module entry point.
public sealed record OrderPlaced(Guid OrderId);

[BusConsumer(Identity)]
public sealed class OrderProjection(IOrderReadModel readModel) : IConsume<OrderPlaced>
{
    public const string Identity = "orders.projection";

    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
        readModel.ApplyAsync(context.Message, cancellationToken);
}

public static class OrdersMessaging
{
    public static IServiceCollection AddOrders(this IServiceCollection services)
    {
        services.ConfigureMessaging(messaging =>
        {
            messaging.AddModule<Orders.MessagingModule>(); // generated for the Orders assembly
            messaging.Message<OrderPlaced>("orders.placed").CorrelateBy(order => order.OrderId.ToString());
        });

        return services;
    }
}

// Host: one AddHeadlessMessaging call, then the modules, in any order.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("App"))
);

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(options =>
    {
        options.HostName = "localhost";
        options.UserName = builder.Configuration["RabbitMq:UserName"]!;
        options.Password = builder.Configuration["RabbitMq:Password"]!;
    });
    // EF-backed storage declares the Transactional inbox guarantee that durable consumers require by default.
    setup.UseEntityFramework<AppDbContext>();

    setup.Tune(OrderProjection.Identity, consumer => consumer
        .Concurrency(4)
        .UseRabbitMq(rabbit => rabbit.PrefetchCount(20)));
});

builder.Services.AddOrders();
```

This is the production default: `Headless.Messaging.RabbitMq` for transport and `Headless.Messaging.Storage.PostgreSql.EntityFramework` for storage over the application's `DbContext`. The consumer identity (`orders.projection`) is the consumer's durable name: it is the Bus subscription name and keys its inbox rows, circuit breaker, metrics, and tuning. The raw `UsePostgreSql(...)` and `UseSqlServer(...)` storages declare only the `Durable` inbox guarantee, so a host with durable consumers on them must opt down through `setup.Options.MinimumInboxGuarantee`.

## Agent Rules

- **App install pattern**: install `Messaging` + exactly one transport + exactly one storage. Bootstrap fails when zero or multiple storage providers are configured. Core brings shared/bus/queue abstractions transitively for applications.
- **One outbox per database, not one storage per database**: when units of work run on several databases, keep one primary storage and add `setup.AddOutbox().UseEntityFramework<TContext>()` (or `UsePostgreSql(...)` / `UseSqlServer(...)`) for each other database. See [Additional outboxes](#additional-outboxes).
- **Library contract pattern**: install `Messaging.Abstractions`, `Messaging.Bus.Abstractions`, or `Messaging.Queue.Abstractions` directly when a library exposes envelopes or publisher interfaces, declares message contracts, or contributes consumers without bootstrapping Core. `ConfigureMessaging`, `Message<T>`, `AddModule<T>`, and `IMessagingModule` all live in `Messaging.Abstractions`; only `Tune` needs `Messaging`. A library that declares consumers still needs the Messaging source generator at build time (the `Messaging` package carries it; in this repository it is a `ProjectReference` with `OutputItemType="Analyzer"`), and the generated module ships compiled into the library. A library never calls `AddHeadlessMessaging`.
- **Use `InMemory` + `InMemoryStorage` only for dev/testing**, never in production. Data is lost on restart.
- **OpenTelemetry is native to `Messaging`** (no satellite package). Subscribe traces/metrics with `AddMessagingInstrumentation()` on the `TracerProviderBuilder`/`MeterProviderBuilder`, and configure enrichers/suppression via `setup.Instrumentation` inside `AddHeadlessMessaging(...)`.
- **Add `Messaging.Testing`** in test projects for integration testing with awaitable assertions. Use `AddMessagingTestHarness()` to decorate an existing host's DI container (WebApplicationFactory, IHost), or `MessagingTestHarness.CreateAsync()` for standalone harness.
- **Add `Messaging.Dashboard`** when monitoring UI is needed; it exposes operational message actions and requires an explicit authentication choice (the host fails to start otherwise), so configure `WithBasicAuth`, `WithApiKey`, `WithHostAuthentication`, or `WithCustomAuth` — and `SetCorsOrigins` if the SPA is served cross-origin — before production exposure.
- **Consumers declare themselves with one lane attribute**: put exactly one of `[BusConsumer("owner.name")]` or `[QueueConsumer("owner.name")]` on a class that implements `IConsume<T>`. The messages it handles are exactly the `IConsume<T>` interfaces it implements; one class may handle several. `[BusConsumer]` also takes `EveryInstance = true` (see [Every-instance Bus delivery](#every-instance-bus-delivery)). No fluent call declares a consumer, and nothing registers a consumer by scanning assemblies at runtime.
- **The consumer identity is the consumer's durable name**: `owner.name` form, at most 200 characters, first segment naming the owning module. On the Bus lane it is the broker subscription name, so processes that register the same identity compete for each message wherever the module runs. It also keys the consumer's inbox rows, circuit breaker, metrics, `Tune`, configuration, and `ConsumeOnly`. Renaming it starts a new consumer with empty inbox history. Intentional reprocessing uses an explicit linked inbox generation; changing a schema version is not a dedupe-reset mechanism.
- **A message has at most one Queue consumer**: the Queue lane is point-to-point and its destination is keyed by the message name. A second Queue consumer for one message fails the build (HM004) within an assembly and fails startup across assemblies. Use `[BusConsumer]` for fan-out.
- **Modules contribute with `services.ConfigureMessaging(...)`, never a second `AddHeadlessMessaging`**: the host calls `AddHeadlessMessaging(...)` once for transports, storage, and options. A module calls `services.ConfigureMessaging(m => { m.AddModule<X.MessagingModule>(); m.Message<T>(...); })` from its own `Add{Module}` entry point, before or after the host's call. Nothing registers until some call adds the generated module; adding it twice registers it once. Every contribution and `WithMessageNameMapping` call is recorded as a descriptor and folded once per service provider, when messaging first builds its consumer registry: at startup, or at an earlier publish that needs a message name. Identical contributions merge there; one identity on two consumer classes in the same lane, a conflicting contract, or one type mapped to two names fails at that point naming both sources. `ConfigureMessaging` is the one way to contribute: framework packages (HybridCache, distributed locks, Settings, Features, Permissions) use it from `Messaging.Abstractions`, and `Tune(identity, ...)` is a `Messaging` extension on the same builder. In a host that never calls `AddHeadlessMessaging`, contributions stay inert.
- **Declare a message contract once, for both lanes**: `m.Message<T>(name, version = "1")` sets the logical name and schema version that publishing and consuming resolve `T` to on both lanes. Chain `.CorrelateBy(...)`, `.OnBus(b => ...)`, and `.OnQueue(q => ...)` for correlation, routing affinity, delivery mode, and provider settings. The message type needs no attribute and no Headless reference. Identical declarations from several modules merge; a different name, version, correlation selector, or lane setting fails startup naming both. Selectors compare by delegate, so share one declaration method rather than writing the same lambda twice.
- **Never put a secret in a message payload**: a payload is durable, not transient. The serialized body is stored in the published and received tables, kept there for the retention window (`SucceedMessageExpiredAfter` defaults to one day, `FailedMessageExpiredAfter` to 15 days, inbox generations to 30 days), re-sent or re-executed by retries and operator actions, copied to the broker, and shown in full by the dashboard. Put only a reference in the message, such as the user ID and the purpose (`new PasswordSetupRequested(userId, Purpose.Invitation)`), and have the consumer resolve or generate the secret, such as a one-time code or a set-password token, right before it calls the sender, so the secret never leaves that consumer's process. This applies to passwords, one-time codes, reset or invitation tokens, API keys, and connection strings. Messaging has no payload-encryption feature; do not build one for this.
- **Framework messages carry stable `headless.*` names**: every message a Headless package publishes or consumes is declared by that package as `headless.<domain>.<event>`, version `1`: through `ConfigureMessaging(m => m.Message<T>(...))`, which keeps the packages on the messaging abstractions (HybridCache, distributed locks, Settings, Features, Permissions). The name is a `MessageName` constant on the message type. Host conventions (`UseConventions` prefix, suffix, kebab-case) never rename them, so services with different conventions interoperate on one broker; only `MessagingOptions.MessageNamePrefix` applies, as it does to every name. An application consumer of a framework message declares only its consumer, never a second `Message<T>` for it. The framework messages are `CacheInvalidationMessage` (`headless.caching.hybrid.invalidation`), `DistributedLockReleased` (`headless.locks.released`), `SettingChangedMessage` (`headless.settings.changed`), `FeatureChangedMessage` (`headless.features.changed`), `PermissionGrantChangedMessage` (`headless.permissions.grant-changed`), and `DynamicPermissionDefinitionsChanged` (`headless.permissions.definitions-changed`).
- **Host controls are keyed by identity**: `Tune(identity, c => ...)` changes a declared consumer's deployment settings (concurrency, inbox retention, circuit breaker, failure policy, middleware, provider consumer settings); `Headless:Messaging:Consumers:{identity}` configuration applies after every `Tune`; `ConsumeOnly(...)` limits which competing consumers a host runs. An identity no registered consumer declares fails startup.
- **The same message can use both lanes**: one contract names the message on both lanes. A Bus consumer and a Queue consumer of the same message have independent subscriptions, inbox rows, middleware, circuits, and retry/backpressure state. Every built-in dual-lane transport declares independent physical lane topology. Startup validates contract routes only on lanes the transport carries, so Kafka (Queue-only) accepts every contract; a Bus consumer on Kafka still fails startup, and a Bus publish on Kafka fails when attempted.
- **Runtime handlers are first-class**: Use `IRuntimeSubscriber` for ephemeral broker-attached delegates on the Bus lane. They share scoped DI, middleware, diagnostics, retry, and correlation semantics with class handlers; a competing runtime subscription retries by the host's default failure policy. `RuntimeSubscriptionOptions.Identity` is the subscription's consumer identity and Bus subscription name.
- **The publisher verb selects the lane**: Use `IBus.PublishAsync` for broadcast Bus delivery and `IQueue.EnqueueAsync` for point-to-point Queue delivery.
- **Use typed routing affinity**: set `RoutingAffinityKey`; reserve `headless-routing-affinity-key`. Validate registered route support from frozen capabilities before startup effects, and typed/native conflicts before durable writes. Provider session/FIFO topology still requires broker evidence.
- **The receiver decides enlistment; the mode decides durability**: `IBus`/`IQueue` are autonomous — their rows are standalone and survive the caller's rollback, whatever unit of work is active. `unit.Outbox` is enlisted — its row is written inside that unit's transaction and discarded with it. Pick by which one you call; no option, per-type policy, or host default moves a publish between them. `DeliveryMode` (`Durable` default, `Direct`) applies to the autonomous surface only and is set per call (`PublishOptions.DeliveryMode` / `QueueOptions.DeliveryMode`), per type (`WithDeliveryMode`), or per host (`MessagingOptions.DefaultDeliveryMode`); `Direct` bypasses storage and rejects `Delay`/`ScheduledAt`. An enlisted publish is durable by construction and consults none of the three. See [Delivery Modes](#delivery-modes).
- **Provider behavior is capability-gated**: immutable transport, storage, and coordination descriptors declare lanes, delayed scheduling, and physical lane-topology support. Bootstrap freezes and validates them before readiness or resolving provider implementations; direct and outbox calls reject unsupported combinations before middleware, storage writes, client creation, or transport I/O. Raw transport DI registration is not capability evidence.
- **Durable inbox guarantees fail closed**: durable consumers require `MessagingOptions.MinimumInboxGuarantee`, which defaults to `Transactional`. Selecting `Durable` is an explicit opt-down when duplicate suppression may commit separately from application state; selecting `ProcessLocal` is reserved for process-local development storage. Bootstrap validates the storage provider's declared inbox guarantee before subscription creation or retry pickup.
- **The lane discriminator remains wire-compatible**: public/runtime APIs use `MessageLane`, while storage columns use the `IntentType` name and the `headless-intent` header retains its stable literal and `0`/`1` values. Retry drainers dispatch Bus rows through `IBusTransport` and Queue rows through `IQueueTransport`. A persisted row whose value has no matching capability fails terminally; undefined values never default to Bus.
- **Do NOT use raw transport client libraries** (e.g., `RabbitMQ.Client`, `Confluent.Kafka`) directly -- always use the `Headless.Messaging` abstraction layer. One exception: an app on NATS that also uses NATS directly (key-value store, object store) registers its own connection and hands it to Headless with `NatsMessagingOptions.UseConnection(...)` instead of opening a second one. See [Headless.Messaging.Nats](#headlessmessagingnats).
- **Ordering depends on transport**: Kafka orders by partition key. Azure Service Bus orders by session. RabbitMQ has no ordering with multiple consumers. Set `ConsumerThreadCount = 1` and leave the consumer's `Concurrency` at 1 for strict ordering.
- **RabbitMQ credentials**: The framework rejects default `guest`/`guest` credentials. Always configure explicit username/password.
- **AWS SQS redrive is external**: Configure a dead-letter queue and redrive policy with a bounded receive count for handler failures. Headless terminally deletes malformed transport envelopes to prevent requeue storms and does not provision redrive infrastructure.
- **Message-name mapping**: `m.Message<TMessage>("message.name")` is the normal way to name a message. `setup.WithMessageNameMapping<TMessage>("message.name")` is a type-global name mapping without a version or lane settings; with neither, the name comes from `UseConventions(...)` (the type name by default).
- **Fail-fast defaults**: Duplicate consumer identities and duplicate runtime subscriptions are rejected by default. Anonymous runtime delegates must set `RuntimeSubscriptionOptions.HandlerId`.
- **Telemetry parity**: Existing diagnostic listener and metric names stay stable across direct publish, outbox publish, and runtime subscriptions.
- **Inbox telemetry is bounded**: inbox counters use registered consumer identity plus finite lane, outcome, tier, and provider dimensions. Message/replay IDs, payloads, and headers are never metric labels. `setup.Instrumentation.IncludeTenantIdInMetricTags` is an explicit, default-off cardinality opt-in; the dimension it adds is named by `TenantTelemetryOptions.AttributeName` (`tenant.id`).
- **Retention resets identity after purge or expiry**: terminal generations are retained for 30 days by default; configure it per consumer with `Tune(identity, c => c.InboxRetention(...))` or `Headless:Messaging:Consumers:{identity}:InboxRetention`. Direct admission suppresses duplicates while its root is retained. Once that root expires or is purged, readmission creates a fresh lifecycle, even if older replay descendants remain held. Replay generation numbers are local to their lifecycle; explicit admission generations remain independent. Holds, mutations, and operation receipts target immutable generation incarnations. Relational inbox schema v4 requires lifecycle identity and separate admission/replay uniqueness; startup rejects retained older inbox rows whose lifecycle identity cannot be reconstructed safely.
- **Poison inbox retention**: recovery of an unreadable inbox envelope records a terminal failure and clears the attempt fence in the claim transaction. Terminal retention starts from the database clock using the row's persisted retention duration. Terminal redeliveries are suppressed without deserializing or replacing the retained payload; expiry then allows fresh admission.
- Recover missing registrations with the exact consumer identity, logical contract name/version, and lane. Known orphans use independent probe capacity and consume no handler failure retries during deferral. They do not expire automatically; holds do not pause recovery. Unclaimed orphans allow Hold/ReleaseHold and unheld Purge, while live claims block these actions and ForceReprocess remains terminal-only.
- Configure all four inbox history residence durations before enabling collection if existing evidence needs longer retention. Receipt replay and conflict detection last only while the receipt exists; deletion permits a new evaluation of the same operation ID. Audit references can extend receipt lifetime, but history deletion never releases a hold. History ages from original timestamps using the provider clock, and changing retention affects existing records.
- **SQL Server pooled isolation**: monitoring, expiry cleanup, and delayed scheduling preserve skip-locked behavior with `READ_COMMITTED_SNAPSHOT` on or off, even on a pooled session another caller left at a stricter isolation level. Every transaction the storage opens, inbox admission included, sets READ COMMITTED itself.
- **Consumer lifecycle semantics**: consumers need no DI registration. Each delivery builds the consumer class from the delivery's scope through generated typed dispatch (no reflection, no compiled expressions): `GetService<T>()` first, so an application's own registration of the class, or a decorator around it, is used with its lifetime, then `ActivatorUtilities.CreateInstance<T>` when the class is not registered. The dispatcher disposes only an instance it constructed; a container-resolved instance belongs to its scope or container. `IConsumerLifecycle` runs per delivery on that instance. Do not treat it as application startup or shutdown. A registered singleton consumer is shared by concurrent deliveries, so it must be thread-safe.
- **Consumer startup is host-cancellable**: consumer factory creation, metadata provisioning, and subscription receive the host-stopping token. Provider implementations preserve `OperationCanceledException`; do not wrap shutdown cancellation as a broker failure.
- **Core handles outbox automatically** when paired with EF Core -- messages are stored in database before being dispatched to transport.
- **The EF adapter packages** (`Headless.Messaging.Storage.PostgreSql.EntityFramework` / `.SqlServer.EntityFramework`) let an enlisted publish join a unit of work open on `TContext`: `setup.UseEntityFramework<TContext>()` registers `Headless.UnitOfWork` (`AddEntityFrameworkUnitOfWork()`), but no interceptor opens that unit of work for the caller. Open it with `factory.RunAsync(db, ...)` (`Headless.UnitOfWork.EntityFramework`) or a manual `BeginAsync`/`CompleteAsync` pair, then publish through that unit's `Outbox`; an `IBus.PublishAsync` inside the same block still stores a standalone durable row that survives a rollback. `setup.UseEntityFramework<TContext>(o => o.EnableTransactionalInbox = false)` (default `true`) is a separate receive-side switch. It disables the EF inbox-transaction runner and the `Transactional` inbox-capability promotion, so the storage declares `DurableDedupeOnly`, consumers run outside any transaction with a null `ConsumeContext.UnitOfWork`, and durable consumers then need `RequiredInboxCapability = DurableDedupeOnly` to pass startup validation. It does not change the publish side: `unit.Outbox` still enlists in a unit you open. The raw storage packages expose only `UsePostgreSql` / `UseSqlServer` and have no EF or `Headless.UnitOfWork` dependency.
- **Dashboard.K8s requires RBAC** permissions to read Services in the configured Kubernetes namespace.
- **Callbacks publish a response that nobody awaits**: Set `CallbackName` on `PublishOptions` (Bus) **or** `QueueOptions` (Queue). The response always publishes through the durable Bus path, including for a Queue-originated request; Queue remains origin metadata and exactly one Bus response is produced for a single Queue delivery. `SetResponse<TResponse>` preserves the declared response contract for typed middleware and the concrete payload value/type. A callback is fire-and-forget and at-least-once, so response consumers must be idempotent. A Bus request still fans out and each subscriber may emit its own response. When the caller must wait for the answer, use [Request/reply](#requestreply) instead; [Request/reply versus callbacks](#requestreply-versus-callbacks) compares the two.
- **Request/reply awaits one answer on the Queue lane**: the calling host calls `setup.AddRequestReply()` and sends with `IRequestClient.RequestAsync<TRequest, TResponse>`; the responder is a `[QueueConsumer]` class that implements `IRespond<TRequest, TResponse>`. The caller's continuation is at most once and a timeout is ambiguous, so make responders idempotent. Return expected business outcomes such as "not found" in `TResponse`; a thrown exception becomes a `handler_failed` fault after inline retries. Only InMemory, RabbitMQ, NATS, and Redis support it; a host on any other transport that sends requests or declares a responder fails startup. See [Request/reply](#requestreply).
- **Strict publish tenancy is opt-in**: Use `builder.AddHeadlessTenancy(tenancy => tenancy.Messaging(m => m.PropagateTenant().RequireTenantOnPublish()))`. The previous `MessagingBuilder.AddTenantPropagation()` extension has been removed; the root tenancy seam is the single composition point. When neither `PublishOptions.TenantId` nor ambient `ICurrentTenant` is set, the publish wrapper throws `Headless.MultiTenancy.MissingTenantContextException`. See [Strict Publish Tenancy](#strict-publish-tenancy) and the multi-tenancy doc's [Message Consumers](multi-tenancy.md#message-consumers) section.
- **A consumer retries by its failure policy**: declare one with `[BusConsumer(id, FailurePolicy = typeof(TPolicy))]` or `[QueueConsumer(id, FailurePolicy = typeof(TPolicy))]`, replace it on a host with `Tune(id, c => c.FailurePolicy<TPolicy>())`, adjust its numbers under `Headless:Messaging:Consumers:{id}:FailurePolicy`, and set the host default with `setup.DefaultFailurePolicy<TPolicy>()`. Without any of these a competing consumer retries 2 times at once, then 5 times from 30 seconds capped at 15 minutes. See [Consumer failure policy](#consumer-failure-policy) and the shared model in [reliability.md](reliability.md).
- **`MessagingOptions.RetryPolicy.RetryStrategy` and `MaxPersistedRetries` govern publishing only**. Changing them does not change how a consumer retries. `RetryPolicy.OnExhausted` fires once for each terminal consume failure: the policy's budget is spent, a fail rule or the built-in permanent set matches, the payload fails to deserialize, the consumer is no longer registered, or the message is poisoned on arrival.
- **An every-instance consumer takes no failure policy**: declaring one is HM010, and tuning or configuring one fails startup. Its deliveries are at most once and never stored.
- **Retry pressure is quadrant-isolated**: Published-Bus, Published-Queue, Received-Bus, and Received-Queue own independent atomic claims, workers, lock resources, counters, failure state, cadence, and adaptive interval. `IRetryProcessorMonitor` remains an aggregate compatibility projection (maximum interval, backed off when any quadrant is backed off, reset all four); that aggregate never drives runtime scheduling or lock TTL.
- **Distributed lock**: see [Distributed Lock Integration](#distributed-lock-integration) for when to enable, when to skip, and the two-layer model (per-row `LockedUntil` lease + coarse-grained distributed lock).
- **Never write framework metadata through provider hatches**. For publish options, use typed properties; raw `Headers.TenantId` is accepted only by the tenant-integrity path and should not be authored directly.
- **Treat provider hatches as physical broker routing/configuration**. Message-side hatches live only on the lane builders of a contract: `.OnBus(b => b.UseNats(...) / .UseAzureServiceBus(...) / .UseAws(...))` and `.OnQueue(q => q.UseNats(...) / .UseAzureServiceBus(...) / .UseAws(...) / .UseKafka(...))`. Consumer-side hatches live only on `Tune(identity, c => ...)`: `c.UseRabbitMq(...)`, `c.UseKafka(...)`, and `c.UseNats(...)`.
- **Kafka, RabbitMQ, and NATS expose consumer-side hatches**. AWS and Azure Service Bus expose message-side hatches only.
- **Keep this canonical guide aligned with public messaging behavior.** Package READMEs remain small discovery pages and do not mirror this reference.

## Core Concepts

- **Transactional outbox (atomic publish) — requires an explicit unit of work and an enlisted publish**: install `Headless.Messaging.Storage.PostgreSql.EntityFramework` or `Headless.Messaging.Storage.SqlServer.EntityFramework`, then select `setup.UseEntityFramework<TContext>()`. Open the unit of work on that context with `factory.RunAsync(db, ...)` or a manual `BeginAsync`/`CompleteAsync` pair, then call `unit.Outbox.PublishAsync(...)`: that writes the outbox row in the SAME DB transaction and discards it on rollback. Nothing else does — `IBus.PublishAsync` inside the same block writes a standalone row that survives the rollback. The adapter auto-registers `Headless.UnitOfWork` (`AddEntityFrameworkUnitOfWork()`, idempotent); unlike the deleted commit-coordination interceptor, nothing opens that unit of work automatically — the developer opens it on the line they choose. A `HeadlessDbContext`'s own save pipeline separately enlists a unit of work around every `SaveChangesAsync()` call for its domain/integration-event dispatch; a plain `DbContext` gets no such automatic wrapping. The raw ADO.NET packages remain EF-free and expose only connection/data-source setup. This is an atomicity guarantee for the write, not exactly-once delivery.
- **Delivery semantics — at-least-once, consumer idempotency required**: the framework never promises exactly-once. The commit-edge drain and the relay sweep can both deliver the same message in a narrow window (the `LockedUntil` lease and the Succeeded/Failed terminal-row guard minimize but do not eliminate duplicates), and a crash between broker accept and the success-mark write redelivers. Consumers must be idempotent — dedupe by business key or message id.
- **Transactional inbox scope**: the transactional tier atomically commits the current fenced inbox outcome, compatible enlisted application state, and the messages the handler publishes through `context.UnitOfWork.Outbox`. Each Messaging attempt owns one DI scope shared by the EF runner, consume middleware, and handler, with the configured scoped `TContext` alive through commit or rollback. The runner saves tracked changes after the handler returns; explicit handler saves roll back if inbox completion rejects the attempt fence. A subsequent Messaging attempt gets a fresh scope. This does not make handler entry, `Direct`, or external/non-enlisted effects exactly once.
- **Publishing from a consumer**: inside a consumer, publish through the attempt's unit, `context.GetRequiredUnitOfWork().Outbox.PublishAsync(...)` or `.EnqueueAsync(...)`, so the outgoing row commits atomically with the inbox row and rolls back with a failed attempt. `IBus` and `IQueue` never join that unit: they publish immediately, outside the inbox transaction (a `Durable` publish writes its own standalone row, a `Direct` one goes straight to the transport). An `IBus` publish from a handler that then fails is not rolled back, and the retried attempt publishes it again, so downstream consumers see a duplicate. `GetRequiredUnitOfWork()` returns a non-null `IUnitOfWork` on every attempt of a durable consumer under the `Transactional` inbox guarantee (the default `MinimumInboxGuarantee`) and throws `InvalidOperationException` otherwise, naming the setting that removed the unit, so a consumer that depends on atomic publishing fails on its first message instead of silently publishing outside the transaction. On a weaker tier, and for an every-instance consumer, `ConsumeContext.UnitOfWork` is null; a handler that needs an atomic publish there opens its own unit of work.
- **Transactional inbox retries**: EF execution strategies may retry transaction setup before handler entry. Every failure after entry, including save, commit, rollback, and scope/transaction disposal, returns to Messaging's fenced retry path rather than replaying the handler inside the reserved attempt. The adapter still probes ambiguous commit outcomes to recognize a durable commit.
- **Message lane**: Bus is broadcast/pub-sub and Queue is point-to-point. The publisher verb selects the lane (`IBus.PublishAsync` or `IQueue.EnqueueAsync`) and the consumer attribute selects the consumer's lane (`[BusConsumer]` or `[QueueConsumer]`). On the Bus lane every consumer identity gets one copy, shared by the processes that register it; on the Queue lane a message has one consumer and one destination keyed by the message name. Monitoring, dashboard JSON, testing, and runtime APIs use `MessageLane`; only intentional compatibility boundaries retain the `IntentType` database column, `headless-intent` header, and stable `0`/`1` values. Received-message identity includes the lane so the two paths do not collapse into one storage row.
- **Consumer identity**: the `owner.name` string on the consumer attribute (at most 200 characters, `ConsumerMetadata.ConsumerIdentityMaxLength`). It is independent of the CLR type name, so a consumer class can be renamed or moved without losing its inbox history. On the Bus lane it is the broker subscription name; `ConsumerMetadata.SubscriptionName` is the identity on the Bus lane and the message name on the Queue lane. A consumer is keyed by lane, identity, message name, and contract version, so one identity covers every message its class handles.
- **Envelope**: All transport messages carry framework headers such as message id, message-contract version, root correlation id, optional immediate causation id, message name, type, sent time, intent, and optional tenant id.
- **Reserved headers**: `MessageId`, `ContractVersion`, `CorrelationId`, `CausationId`, `CorrelationSequence`, `CallbackName`, `MessageName`, `Type`, `SentTime`, `DelayTime`, `Intent`, and the request/reply headers `RequestId` (`headless-request-id`), `ReplyTo` (`headless-reply-to`), `RequestDeadline` (`headless-request-deadline`), `InReplyTo` (`headless-in-reply-to`), and `ReplyStatus` (`headless-reply-status`), and `TransportAddress` (`headless-transport-address`) are rejected in custom publish headers, `RequestOptions.Headers`, and provider contributions. `TenantId` is also framework-owned; provider contributions cannot write it, while raw publish headers are handled by the stricter tenant-integrity policy for compatibility.
- **Header validation**: custom header names, custom header values, and framework/provider-stamped header values all reject control characters before publish. This includes explicit `MessageId`, `CorrelationId`, `CallbackName`, and typed `TenantId`.
- **Explicit message names**: `PublishOptions.MessageName` follows the same validator as registered message mappings. Invalid dot shapes and invalid characters are rejected before publish.
- **Contract version**: `Message<T>(name, version)` is the normal authority and defaults to version `"1"`. `PublishOptions.ContractVersion` is an explicit per-send override for controlled compatibility work. Consumers validate the header before deserialization, expose it through `ConsumeContext.ContractVersion`, and treat a missing header as version `"1"` for legacy or external producers.
- **Transport address**: `ConsumeContext.TransportAddress` is the native address the delivery arrived on: the NATS subject (shard token included), the Kafka or Pulsar topic, the RabbitMQ routing key, the Azure Service Bus entity path, the Amazon SQS queue URL, or the Redis stream key. Read routing tokens such as a tenant or a run identifier from it instead of the payload. It is `null` on the in-memory transport. Each broker consumer client stamps `headless-transport-address` on receipt and overwrites any wire value, so a producer cannot choose it; it is stored with the received message, so a retry from storage sees the original address.
- **Correlation and causation**: `PublishOptions.CorrelationId` wins. If absent, the contract's `CorrelateBy(...)` selector runs against the payload. If absent, publishes inside a consumer preserve ambient `ConsumeContext.CorrelationId`. If absent, the message id becomes the root correlation id. `PublishOptions.CausationId` wins for the immediate parent; otherwise an ambient consume context contributes its current message id. Consumers read it from `ConsumeContext.CausationId`.
- **Tenant integrity**: use `MessageOptions.TenantId` or ambient tenancy. Do not write `Headers.TenantId` directly.
- **Captured business context**: `MessageOptions.SuppressAmbientBusinessContext = true` disables ambient consume correlation/causation and tenant fallback when forwarding an emission snapshot. Explicit options and registered contract/selector resolution remain authoritative; `Activity` trace propagation is unchanged. A captured null tenant still fails when `TenantContextRequired` is enabled. The EF bridge sets this option and maps the captured occurrence ID to `MessageId`; it never allocates a replacement message identity at drain.
- **Provider config bag**: provider packages attach opaque config objects keyed by config type. Message configs belong to one lane's route of a contract (`OnBus` or `OnQueue`); consumer configs come from `Tune`, where a later `Tune` for the same identity and config type replaces the earlier one. Two contract declarations with provider settings merge only when the settings are equal, which for a selector means the same delegate instance.
- **Declared-contract authority**: the declared contract selects the logical name and typed middleware; assignable-type fallback does not silently bind a concrete payload to another registration. The concrete payload or callback-response type is preserved separately for serialization and typed values. Explicit publish options still override their corresponding envelope fields.
- **Provider header contributions**: message-side provider hatches compute typed payload values before the payload is erased to bytes. Core validates contributed header names and values, then transports map those headers to native broker fields.
- **Azure Service Bus sessions**: when sessions are enabled and a publish config sets `PartitionKey` without a `SessionId`, the provider falls back to that partition key as the session id before it falls back to the framework message id.
- **NATS shard coverage**: a consumer of a message whose contract, declared in this host, uses `SubjectShard(...)` filters on the `{subject}.>` shard wildcard automatically. Unsharded consumers do not subscribe to the wildcard. When the producer shards a message that this host declares without `SubjectShard(...)`, add `Tune(identity, c => c.UseNats(n => n.Sharded()))`: NATS delivers zero messages with no error to a filter subject that matches no shard subject.
- **Ambient consume context**: `IConsumeContextAccessor` is AsyncLocal-backed and restored in a `finally` block after each consume pipeline execution.

### Delivery Modes

Two independent questions, answered by two different things. **Durability** — captured in storage before dispatch, or sent straight to the transport — is `DeliveryMode`, an option. **Enlistment** — whether the durable row lives inside the caller's transaction — is the receiver you call, not an option: `IBus`/`IQueue` never enlist, `unit.Outbox` always does. Messaging has no enlistment knob: no per-call value, no per-type policy, and no host default can move a publish from one receiver to the other. The framework resolves the path before any effect and refuses rather than granting a silently weaker guarantee. Every throw below happens before storage or transport effects:

| Receiver | A unit of work the storage can join | A unit of work the storage cannot join | No unit of work |
|---|---|---|---|
| `IBus` / `IQueue`, `Durable` (default) | standalone durable row — the unit is never consulted | standalone durable row | standalone durable row, dispatched by the relay |
| `IBus` / `IQueue`, `Direct` | straight to the transport, no storage | same | same |
| `unit.Outbox` (always durable) | row inside that unit's transaction, dispatched after commit, discarded on rollback | **throws** before any effect | not reachable — the unit is the receiver |

- **Precedence (`DeliveryMode`)**: per-call `PublishOptions.DeliveryMode` / `QueueOptions.DeliveryMode`, then the per-type, per-lane policy declared with `WithDeliveryMode(...)` on the contract's lane builder (`m.Message<T>(...).OnBus(b => b.WithDeliveryMode(...))` / `.OnQueue(q => q.WithDeliveryMode(...))`), then `MessagingOptions.DefaultDeliveryMode` (`Durable`). `Direct` bypasses storage and rejects a per-call `Delay` or `ScheduledAt`, because scheduling requires storage.
- **An enlisted publish consults none of that.** `OutboxOptions` carries no delivery mode, and the resolver fixes the mode to `Durable` before reading the per-type policy or the host default — durable capture is the mechanism the row joins the transaction through. This is deliberate: under the previous model a type pinned `Direct` with `WithDeliveryMode` made every coordinated publish of that type fail, because `Direct` and enlistment are contradictory. Delay and schedule still work, and `Direct` delivery combined with a coordination requirement throws ("Direct delivery cannot be coordinated with a unit of work; durable delivery is required to write inside its transaction") — a state only framework-internal callers can construct.
- **Whether a storage can join a unit is the storage's own answer.** The relational storages join only an `IRelationalUnitOfWorkResource` whose transaction is live and on the same database, decided by the shared `RelationalDatabaseIdentity` check the Jobs store uses too (see [unit-of-work.md § Database identity](unit-of-work.md#database-identity-and-several-databases)). In-memory storage joins any active unit, including a resource-less one opened with `IUnitOfWorkFactory.BeginAsync()` (test hosts), through its buffered-promotion seam. With [additional outboxes](#additional-outboxes), `unit.Outbox` asks every outbox and writes to the one whose database matches the unit; a unit that matches none is refused with the `Database` mismatch. A relational storage against a resource-less unit, a unit on another database, a completed transaction, or another provider's resource cannot join, and an enlisted publish throws naming the mismatch: `Publishing 'OrderPlaced' cannot join the active unit of work ({Mismatch}): {detail}. {advice}` — for example "the active unit of work exposes no relational resource for the messaging storage to write into. Begin the unit of work over a relational resource for the messaging database, or publish without coordination."
- **The refusal belongs to the enlisted surface only.** `IBus`/`IQueue` hand the publisher no unit at all, so there is nothing for a storage to reject: a durable autonomous publish always writes standalone, including inside a unit of work whose transaction the storage could have joined. The mismatch throw above is reachable only through `unit.Outbox`.
- **There is no startup gate for enlistment**; the failure is always a per-call refusal. `IUnitOfWorkFactory` always exists (`AddUnitOfWork()` is idempotent and is called by `AddHeadlessMessaging`, `AddHeadlessJobs`, `AddHeadlessDbContextServices`, and the three `Headless.UnitOfWork.*` provider setups). The only startup validation in this area is unchanged: durable consumers still require `MessagingOptions.MinimumInboxGuarantee` (default `Transactional`) from the configured storage.
- **Migration note — the "never publish outside a transaction" guardrail is gone.** A host that set `MessagingOptions.DefaultEnlistment = TransactionEnlistment.Required`, or registered a type `WithEnlistment(TransactionEnlistment.Required)`, used it to make an un-enlisted publish fail loudly. Both members are deleted, so that host gets a compile error, not a silent behavior change. Nothing replaces it as a host-wide setting: the guarantee is now structural per call site. Reading `unit.Outbox.PublishAsync(...)` proves enlistment at the line, and an `IBus.PublishAsync` in code that must be transactional is a review finding rather than a runtime throw. For a build-time guard, reference `Headless.UnitOfWork.Analyzers`: [HF2001](unit-of-work.md#hf2001) reports an `IBus.PublishAsync` or `IQueue.EnqueueAsync` made while a unit of work is in scope, names `unit.Outbox` on that unit, and its code fix rewrites the `(content, cancellationToken)` call. It is a suggestion by default; make it fail the build with `dotnet_diagnostic.HF2001.severity = error` in `.editorconfig` (see [Severity and suppression](unit-of-work.md#severity-and-suppression)). It reports nothing where no unit is in scope, so an assembly that must never publish autonomously at all bans the `IBus`/`IQueue` *types* instead, rather than the package reference: the lane abstractions are on the compile surface of anything that references Messaging. With `Microsoft.CodeAnalysis.BannedApiAnalyzers` referenced, a `BannedSymbols.txt` added as an `AdditionalFiles` item in that project needs exactly these two lines:

  ```text
  T:Headless.Messaging.IBus;Publish through unit.Outbox in this assembly — IBus never joins the transaction.
  T:Headless.Messaging.IQueue;Enqueue through unit.Outbox in this assembly — IQueue never joins the transaction.
  ```
- **An enlisted publish ends execution-strategy replay only where a replay would not re-run it.** Replay re-runs the block that *owns* the unit. Inside `factory.RunAsync(db, …)` (owned mode) that block is yours: a transient failure anywhere in it replays the whole block with a fresh transaction and unit, the first attempt's row rolls back with its transaction, and the replayed block publishes again — so an enlisted publish there leaves the unit replayable, and a `RunAsync` block that publishes stays retriable under `EnableRetryOnFailure`. The exception is the `HeadlessDbContext` save pipeline's *own* save (no caller transaction): it enlists in observed mode, retains the completed domain-event drain across the strategy's replays, and so replays without re-running the handler that published. An enlisted publish into an observed-mode unit therefore calls `IUnitOfWork.PreventRetry()` before it writes, and that save is surfaced without replay; a fresh context and aggregate graph are needed to retry it. A `SaveChangesAsync` inside your own block that dispatched domain or integration events ends replay as well: its success clears the aggregate's events, so a replayed block would re-insert the aggregate with nothing left to dispatch and commit it without the handlers' rows; the save calls `PreventRetry()` before clearing them.
- **The entity-emitted integration-event path is exempt even there.** `PreventRetry()` is also skipped when the publish carries `MessageOptions.IsRetainedForTransactionReplay`, which is `internal` and set only by `OutboxIntegrationEventDispatcher` — the bridge that publishes the integration events an entity emitted during `SaveChangesAsync`. The save pipeline retains those captured occurrences and re-publishes them on a replayed attempt, so a pipeline-owned save whose only enlisted publishes came from emitted integration events stays retriable. Application code cannot set the flag. A pipeline-owned save that mixes both — emitted integration events plus a `unit.Outbox` publish from a domain-event handler — is not retriable, because the handler's publish marked the unit.
- **The default costs one storage write.** A `Durable` autonomous publish is stored first and dispatched by the relay, so high-rate events pay a storage write and a relay hop per message. Opt out per type and lane with `.OnBus(b => b.WithDeliveryMode(DeliveryMode.Direct))` (or `.OnQueue(...)`) on the contract, or per host with `DefaultDeliveryMode = DeliveryMode.Direct`; `Direct` gives up durability and atomicity and returns a receipt with no `StorageId`.
- **Storage is mandatory**, so "no storage" is a configuration error raised by startup validation rather than another matrix column.
- **Telemetry**: `headless.messaging.delivery.requested` and `headless.messaging.delivery.resolved` emit `durable` or `direct` only, and the `headless-delivery-requested` / `headless-delivery-resolved` headers carry the same names. Whether the row was written inside a transaction travels separately, in the framework-reserved `headless-delivery-coordinated` header, as the literal `"true"` or `"false"`. It is a three-state fact: an **absent** header means unrecorded, never "not coordinated" — rows written before this header existed read as unknown. The dashboard's message detail shows it as "Transaction coordinated" (`Yes` / `No` / `Not recorded`), `MessageView.IsCoordinated` is the monitoring projection, and `RecordedMessage.IsCoordinated` exposes it to tests. All three are `bool?`.
- **Jobs follows the same rule.** `TransactionEnlistment` is gone from Jobs too: `unit.Jobs` enlists and the injected `IJobScheduler` never does. See [Enlisted Enqueue](jobs.md#enlisted-enqueue-atomic-enqueue).
- **`IBus` and `IQueue` are autonomous singletons** (`TryAddSingleton`): they are handed no unit and look for none, so a publish made while a unit of work is open still writes a standalone durable row, and any singleton or hosted service can take them directly. The framework-internal singletons that publish through them (`HybridCache`, `DistributedLock`, `DistributedReadWriteLock`, `DistributedSemaphoreProvider`) resolve the registered service and keep requesting `Direct` explicitly.

Two unit-of-work behaviors are documented, not defects. `IUnitOfWork.OnCompleted` registrations are savepoint-blind: a registration made inside a savepoint that is later rolled back still runs when the outer transaction commits, even though its row was discarded — publish after the last partial rollback. Under EF's execution strategy, `IUnitOfWorkFactory.RunAsync(db, …)` replays the whole operation, publishes included, for a failure before the commit starts; once the commit has started, or after `IUnitOfWork.PreventRetry()`, the fault surfaces without replay. See [Unit of Work](unit-of-work.md).

## Choosing a Provider

| Provider | Use when | Avoid when | Trade-off |
| --- | --- | --- | --- |
| InMemory | Local development, unit tests, demos | Production durability or multi-process delivery | No external dependency, no durability |
| RabbitMQ | General-purpose broker, routing keys, queue semantics | Strict partitioned ordering across large streams | Mature routing model, topology must match custom routing keys |
| Kafka | Ordered partitions, high-throughput streams | Broadcast Bus semantics through this package | Queue-only provider; partition key has no framework length cap |
| Azure Service Bus | Azure-hosted topics/queues, sessions, managed operations | Non-Azure deployments | PartitionKey is limited to 128 chars and must match SessionId when sessions are enabled |
| AWS SNS/SQS | AWS-native pub-sub and queue workloads | Non-AWS deployments | FIFO entities use MessageGroupId and deduplication ids |
| NATS | Subject-based routing, lightweight broker, JetStream | Complex per-consumer storage-specific routing | Subject shards must be a single safe token |
| Pulsar | Pulsar-native durable transport with shared subscriptions | Projects not already on Pulsar | Requires Pulsar topic and subscription provisioning |
| Redis | Redis Streams transport | Workloads requiring a broker-native dead-letter queue | Durable streams, pending-entry reclaim after 60 s, and age-bounded streams (7 days by default) |

## Provider Capabilities

| Provider | Bus | Queue | Same-name lane isolation | Message hatch (`OnBus`/`OnQueue`) | Consumer hatch (`Tune`) | Request/reply |
| --- | --- | --- | --- | --- | --- | --- |
| AWS | SNS topic to one SQS queue per consumer identity | Direct SQS destination | Yes | `UseAws(a => a.MessageGroupId(...))` | None | No; startup fails |
| Azure Service Bus | Topic/subscription per consumer identity | Queue | Yes | `UseAzureServiceBus(a => a.PartitionKey(...))` | None | Not yet; startup fails |
| InMemory | One copy per consumer identity | One owned copy | Yes | None | None | In-process reply channel |
| Kafka | No | Topic; Kafka consumer group named after the message | Not applicable; Queue-only | `OnQueue` only: `UseKafka(k => k.PartitionBy(...))` | `UseKafka(k => k.WithIsolationLevel(...))` | No; startup fails |
| NATS | Interest-retained lane stream, one durable per consumer identity | Work-queue-retained lane stream | Yes | `UseNats(n => n.SubjectShard(...))` | `UseNats(n => n.Sharded())` | Core NATS subject outside JetStream |
| Pulsar | Lane topic + identity subscription | Lane topic + owned subscription | Yes | None | None | Not yet; startup fails |
| RabbitMQ | Lane topic exchange, one queue per consumer identity | Lane direct exchange | Yes | None | `UseRabbitMq(r => r.PrefetchCount(...))` | Exclusive reply queue |
| Redis | Lane Redis Stream + Redis consumer group named after the identity | Lane Redis Stream + Redis consumer group named after the message | Yes | None | None | Pub/sub channel |

The shipped immutable descriptors are the runtime authority, not this table or raw DI shape. The dashboard `/api/meta` projection exposes those descriptors and their lanes. Executable provider conformance tests prove consumer-identity fan-out, replica competition, Queue ownership, and same-name isolation at each provider's supported tier. Kafka is intentionally Queue-only: a Bus consumer fails startup before provider or storage side effects, and a Bus publish fails when attempted. The request/reply column is the reply channel each provider uses; see [Request/reply](#requestreply) for its rules.

### Bus subscription names

On the Bus lane a consumer identity is the broker subscription name. Every provider derives it through one shared rule: an identity the broker accepts is used unchanged, and any other identity becomes a readable prefix, with rejected characters replaced by `-`, followed by `-` and the first 12 hex characters of the identity's SHA-256. The name is the same in every process, so replicas that register one identity share one subscription, and two identities that normalize to the same prefix still get different names. Queue destinations stay keyed by the message name; on Kafka the Queue consumer group is the message name, derived by the same rule.

| Provider | Bus subscription | Limit and characters kept | Namespace isolation |
| --- | --- | --- | --- |
| AWS | SQS queue `bus-{identity}` (an identity ending `.fifo` gets a FIFO queue) | 80 characters including `bus-` and `.fifo`; letters, digits, `-`, `_` | Account and region |
| Azure Service Bus | Subscription `{identity}` on the Bus topic | 50 characters; letters, digits, `.`, `-`, `_`; starts and ends with a letter or digit | Namespace and `TopicPath` |
| InMemory | `{identity}`, unchanged | None | The process |
| Kafka (Queue only) | Consumer group `{message-name}` | 249 characters; letters, digits, `.`, `-`, `_` (the topic rules) | Topic names, through `MessageNamePrefix` |
| NATS | Durable `bus-{identity}-{subject}` per subject | 255 characters; printable ASCII except `.`, `*`, `>`, `/`, `\` | Account; streams follow the message names |
| Pulsar | Subscription `headless-bus-{identity}` on each topic | 255 characters; letters, digits, `-`, `=`, `:`, `.`, `_` | Tenant and namespace in the topic name |
| RabbitMQ | Queue `bus.{identity}` | 255 characters; letters, digits, `.`, `-`, `_` | Virtual host |
| Redis | Consumer group `{identity}` on each stream | No limit; printable ASCII without spaces | Stream keys follow the message names; database or ACL |

Broker-backed providers keep names ASCII and replace whitespace and control characters even where the broker allows them. No host-level prefix is added: the identity alone names the subscription, so a module keeps its subscriptions when it moves to another process. Separate systems or environments on one broker are isolated by the broker's namespace in the last column, or by distinct identities.

**Routing affinity:** register the logical destination, optionally require support with `RequireRoutingAffinity()`, and supply one `RoutingAffinityKey` on publish/enqueue options. Required-route checks run before startup clients/processors; per-call validation runs before persistence and transport effects. Inert option snapshots establish local support, not remote broker-topology proof. Unknown keyed overrides are rejected even if the provider could auto-create an unkeyed destination.

| Provider | Supported configured destination | Native key | Key bounds / prerequisites |
| --- | --- | --- | --- |
| Kafka | Queue topic | UTF-8 string key | Nonempty, no controls; verified deterministic partitioner and fixed partition count |
| Pulsar | Bus or Queue topic | Native message key | Nonempty, no controls; built-in key hashing and fixed topology |
| Azure Service Bus | Session-enabled Bus subscription or Queue | `SessionId` | At most 128 UTF-16 code units; matching raw `SessionId` / `PartitionKey` |
| AWS | FIFO SNS topic or SQS queue | `MessageGroupId` | `.fifo` destination; 1–128 ASCII characters `!`–`~`, without spaces |
| NATS, RabbitMQ, Redis, InMemory | Unsupported in current topology | None | Required declarations and keyed requests reject deterministically |

Affinity does not promise total FIFO, nonconcurrent same-key handling, independent partitions for different keys, or stable placement after topology changes. Redelivery remains at-least-once.

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UseKafka("localhost:9092");
    setup.UseInMemoryStorage();
});

services.ConfigureMessaging(messaging =>
    messaging.Message<OrderChanged>("orders.changed").OnQueue(queue => queue.RequireRoutingAffinity())
);

await queue.EnqueueAsync(order, new QueueOptions
{
    RoutingAffinityKey = order.OrderId.ToString(),
    DeliveryMode = DeliveryMode.Durable,
}, cancellationToken);
```

The example uses process-local outbox storage for development; choose PostgreSQL or SQL Server for persistence across process restarts.

Stored keyed outbox rows are revalidated against the current frozen destination mapping before attempt reservation or native client resolution. Normal retry pickup may already hold a storage lease at this point. A deployment that removes or invalidates their mapping rejects dispatch until the operator restores a supported configuration. Unkeyed messages have no routing affinity requirement.

**Affinity configuration:** use the typed `RoutingAffinityKey` option for portable routing intent. Native provider adapters must have exactly matching values when both are supplied. Verify the destination session/FIFO/partition configuration before using a key. Keep partition topology and hashing stable when placement matters; Headless does not promise placement across a topology change. Third-party providers declare immutable `MessagingRoutingAffinityRoute` mappings in their capability contribution and validate native adapters before client I/O.


### Every-instance Bus delivery

A competing Bus subscription gives one copy per consumer identity, shared by every process that registers it. A consumer that holds per-process state, such as an in-memory cache or a lock waiter, needs every process to receive every message instead. Declare it every-instance:

```csharp
[BusConsumer("pricing.price-cache", EveryInstance = true)]
public sealed class PriceCache(IPriceCacheStore store) : IConsume<PriceChanged>, IOnSubscriptionEstablished
{
    public ValueTask ConsumeAsync(ConsumeContext<PriceChanged> context, CancellationToken cancellationToken) =>
        store.EvictAsync(context.Message.Sku, cancellationToken);

    // Messages published while this process was not subscribed never arrive, so drop what may be stale.
    public ValueTask OnSubscriptionEstablishedAsync(SubscriptionEstablishedContext context, CancellationToken cancellationToken) =>
        context.IsReconnect ? store.ClearAsync(cancellationToken) : ValueTask.CompletedTask;
}
```

A runtime subscription sets `RuntimeSubscriptionOptions.EveryInstance = true`.

- **Subscription kind**: each process opens a subscription of its own, named from the identity and the host's `MessagingInstanceId` (a GUID generated once per host start, never the host name). The subscription exists only while the process holds it; the broker removes it after the process stops or crashes, within the bound in the matrix below.
- **One client per process**: an every-instance identity gets exactly one consumer client whatever `ConsumerThreadCount` is, because a second client would be a second subscription and deliver every message twice in one process. The identity's `Concurrency` still applies inside that client.
- **At most once, no backlog**: a process receives only what is published while it is subscribed. Nothing is stored for the delivery: no inbox row or admission, no reservation or lease, no retry pipeline, no circuit breaker, and no dashboard row. Receive middleware, the contract-version check, and deserialization still run, and the consumer runs in a fresh scope with the consume middleware, then the message is committed.
- **Failures are logged and committed**: a consumer exception, a receive-stage reject, a message no consumer on the subscription handles, and a fault outside the consumer (in the core or the transport client) are logged, counted in `messaging.every_instance.deliveries`, and committed. None is requeued, because a per-process subscription has no one else to redeliver to and a redelivery would fault the same way; `RetryPolicy.OnExhausted` is not called. The only reject is a delivery stopped by host shutdown or a client rebuild.
- **Delivery outcomes**: `messaging.every_instance.outcome` is one of four values. `succeeded`: the consumer returned. `failed`: the consumer threw (`error.type` = the exception type). `dropped`: the message never reached the consumer or faulted outside it (`error.type` = the exception type when there is one, or `overflow` for a NATS subscription channel that discarded the message). `skipped`: receive middleware skipped it.
- **Reconnect signal**: a consumer that implements `IOnSubscriptionEstablished` is called once its subscription receives: at host start (`IsReconnect = false`, `Generation = 1`), after every rebuild of its subscription's clients (a broker failure rebuilds every subscription), and after the transport re-establishes the subscription on its own. Attaching or detaching a runtime subscription rebuilds only the subscription groups it changes, so an unrelated every-instance consumer keeps its clients and its hook does not run. `IsReconnect = true` means messages may have been missed; a mirror of state should flush or reload. The hook runs in its own scope on an instance built the way a delivery builds it: the container's registration of the class when there is one, otherwise a new instance that is disposed afterwards. A failing hook is logged without stopping the subscription. Hooks of one subscription run one at a time in establishment order. Host startup waits for the first hooks; a rebuild does not wait and holds no lock while they run, so a hook may attach or detach a runtime subscription. Each call is bounded by `MessagingOptions.SubscriptionEstablishedTimeout` (default 30 seconds, `> 0`, `<= 5m`): on expiry the hook's token is canceled, a warning is logged, and startup and later establishments go on without it. Runtime subscriptions have no hook.
- **Startup rules**: an every-instance consumer on a transport without every-instance support fails startup before any consumer client or broker object is created, with a `MessagingConfigurationException` that names the consumer and the provider; a runtime subscription is checked the same way before it attaches. A `FailurePolicy` declared on an every-instance consumer is HM010 at build time, and a hand-written module that declares one fails startup. Tuning an inbox retention, a circuit breaker, or a failure policy onto an every-instance consumer, through `Tune` or configuration, fails startup, because none of them means anything for a per-process, at-most-once subscription. `EveryInstance` exists only on `[BusConsumer]`, so the Queue lane cannot express it. A host whose only consumers are every-instance does not need an inbox guarantee from storage.
- **`ConsumeOnly` does not apply**: a host started with `ConsumeOnly` still starts every every-instance consumer, because each process must keep its own state current. A `ConsumeOnly` entry that matches only every-instance consumers fails startup, since it would have no effect. Runtime subscriptions are not filtered either.
- **Choose it for derived state only**: every-instance delivery refreshes what a process can rebuild. Work that must happen once, or must not be lost, stays on a competing consumer.

| Provider | Every-instance primitive | Left behind after a crash |
| --- | --- | --- |
| InMemory | A subscription per identity and instance id | Nothing |
| NATS | Core subscription on the Bus subject, not a JetStream consumer; a subject that a sharded identity's `name.>` wildcard already covers is not subscribed again, so one publish arrives once. The client reconnects on its own and reports the recovery with the reconnect signal. A consumer slower than the publish rate overflows the subscription's pending channel (`NatsOpts.SubPendingChannelCapacity`, 1024 by default), which drops the newest messages: each drop counts as `dropped` with `error.type` = `overflow`, and a burst of drops raises the reconnect signal once, after a one-second quiet window | Nothing |
| RabbitMQ | Server-named exclusive, non-durable queue bound to the Bus exchange, on a connection of its own without automatic recovery. The queue's message TTL is a fixed 60 seconds, not `QueueArguments.MessageTTL`, so a stalled or paused queue never hands the process older per-process state. A lost channel or a consumer the broker cancels (an operator deleted the queue) fails the listener, and the core rebuilds the client with a new queue and raises the reconnect signal | Nothing |
| Redis | Group-less polled stream read from each stream's tail at subscribe time, then from the last id read | Nothing |
| Pulsar | Non-durable, exclusive subscription starting at the latest message; Pulsar.Client reconnects on its own, and the transport reports each recovery within about a second | Nothing |
| Azure Service Bus | Subscription named from the identity and instance id with `AutoDeleteOnIdle` of 5 minutes, created on subscribe, kept across client rebuilds, and deleted when the host disposes its services after a graceful stop, each delete bounded by its own 30-second timeout; recreated if Azure deleted it during a long disconnect, and the reconnect signal is raised only when the subscription was actually recreated. Needs `AutoProvision` and Manage rights: with `AutoProvision` off, startup fails | The subscription, for up to 5 minutes |
| AWS SNS/SQS | Not supported: no idle auto-delete, so a crash would leak the queue and its subscription | Startup fails |
| Kafka | Not applicable: Kafka has no Bus lane | Not applicable |

Transports declare support with `MessagingProviderCapabilities.Transport(..., supportsEveryInstance: true)`. A transport that recovers its connection internally reports each re-established every-instance subscription through the callback attached with `IConsumerClient.AttachReestablishedCallback`, so the consumer hook fires for gaps the core never saw.

### Provider topology and operational requirements

| Provider | Topology | Conformance boundary | Operational requirement |
| --- | --- | --- | --- |
| AWS | Bus consumer identities own distinct SQS queues subscribed to SNS; Queue sends directly to SQS | LocalStack fan-out, competition, isolation, policy shape, and malformed deletion | Grant the scoped runtime and provisioning actions below |
| Azure Service Bus | Native topics/subscriptions for Bus and queues for Queue | Credential-gated real namespace conformance | Supply a namespace with the required permissions and session configuration |
| InMemory | Process-local channels | Shared in-process conformance | Restart loses all state |
| Kafka | Queue-only topics; one Kafka consumer group per message | Ownership, startup rejection, and bounded poison-offset advancement | A Bus consumer fails startup; configure partitions for the workload |
| NATS | Lane-qualified subjects, streams, retention, and durables | Identity/replica isolation and malformed terminal ACK | Grant stream and consumer provisioning permissions |
| Pulsar | Lane-qualified topics and Bus subscriptions | Identity/replica isolation and malformed terminal ACK | Grant topic and subscription creation permissions |
| RabbitMQ | Lane-qualified exchanges, routing keys, and owned queues | Identity/replica isolation and malformed terminal reject | Grant exchange and queue provisioning permissions |
| Redis | Lane-qualified Streams for both lanes | Routing, ownership, settlement, and poison handling | Size Redis memory for `StreamMaxAge` of traffic; the provider creates the Redis consumer groups and trims the streams |

#### AWS least-privilege handoff

`AmazonSqsMessagingOptions.Credentials` (or the AWS SDK default credential chain) supplies one identity to runtime and topology calls; the provider has no separate provisioning credential or disable-auto-provision switch. Grant only the rows used by each deployed workload:

| Workload / owner | Runtime actions | Provisioning and discovery actions | Resource scope |
| --- | --- | --- | --- |
| Bus publisher workload role | `sns:ListTopics`, `sns:Publish` | `sns:CreateTopic` when a `bus-*` topic is absent | `sns:ListTopics` requires `Resource: "*"`; scope create/publish to `arn:${Partition}:sns:${Region}:${Account}:bus-*` |
| Queue publisher workload role | `sqs:SendMessage` | `sqs:CreateQueue` on first use in each process, including for a pre-created queue because the provider uses the idempotent create call to resolve its URL | Scope both actions to `arn:${Partition}:sqs:${Region}:${Account}:queue-*` |
| Bus consumer workload role | `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility` | `sns:CreateTopic`, `sqs:CreateQueue`, `sqs:GetQueueAttributes`, `sqs:SetQueueAttributes`, `sns:Subscribe` | Scope SNS actions to the exact generated `arn:${Partition}:sns:${Region}:${Account}:bus-*` topics and SQS actions to the exact generated `arn:${Partition}:sqs:${Region}:${Account}:bus-*` consumer-identity queues |
| Queue consumer workload role | `sqs:ReceiveMessage`, `sqs:DeleteMessage`, `sqs:ChangeMessageVisibility` | `sqs:CreateQueue` on startup | Scope all actions to the consumer-owned `arn:${Partition}:sqs:${Region}:${Account}:queue-*` destinations |
| SNS service principal; queue resource-policy owner is the Bus consumer deployment | `sqs:SendMessage` | None | The provider writes the Bus queue policy for principal `sns.amazonaws.com`, resource = that consumer-identity queue ARN, and `aws:SourceArn` = the subscribing `bus-*` topic ARN |

The deployment owner owns the workload-role policies and the provider-created queue resource policy. Consumer topology failures from AWS surface as `AWS_MESSAGING_PROVISIONING_DENIED` with the lane, subscription name, AWS error code, and the aggregate action set for that stage; use the denied AWS API operation to identify the exact missing action. Publisher denials return a failed `OperateResult` with the AWS exception message and retain the service exception as the inner exception, while receive denials are logged and retried with backoff. Do not grant delete, wildcard SNS/SQS administration, or unrelated IAM actions: the current transport does not call them.

Conformance tests exercise the provider behavior; a deployment must still configure its own credentials, resource permissions, retention, and restart policy.

### Messaging package composition

Use matching versions of the `Headless.Messaging.*` packages. The package-family probe verifies the complete current package graph and its public API:

- Publish autonomously through `IBus.PublishAsync(...)` and enqueue through `IQueue.EnqueueAsync(...)`. Both inherit the per-type `WithDeliveryMode` policy and then `MessagingOptions.DefaultDeliveryMode` (`Durable` by default) when the per-call mode is unset, including omitted, null, metadata-only, and fluent options. An explicit `Durable` or `Direct` overrides both. To write the row inside the caller's transaction, publish through `unit.Outbox.PublishAsync(...)` / `unit.Outbox.EnqueueAsync(...)` instead; that surface is always durable and inherits no mode.
- `PublishAsync` and `EnqueueAsync`, including callback overloads, now return `Task<PublishReceipt>`. Existing `await` statements and `Func<Task>` adapters can ignore the result because `Task<PublishReceipt>` derives from `Task`. Custom `IBus`/`IQueue` implementations and test doubles must update their return signatures and supply a receipt; recompile consumers for this binary API break. Use `var receipt = await bus.PublishAsync(message, cancellationToken);` to retain the returned identity.

Omit an unused cancellation token, or pass `default` or `cancellationToken: default` to select the existing token overload. Supplying `default` followed by a cancellation token selects the options overload. Use `options:` and `configure:` to make record and callback intent explicit.
- Declare consumers with `[BusConsumer]` or `[QueueConsumer]` and contribute the generated module with `AddModule<…MessagingModule>()`; public APIs use `MessageLane`.
- Dashboard and monitoring JSON expose `lane`, `requestedDeliveryMode`, `resolvedDeliveryMode`, and the nullable `isCoordinated`. Storage uses the `IntentType` column and the `headless-intent` header with `Bus = 0` and `Queue = 1`.
- `Delay` and `ScheduledAt` are mutually exclusive one-shot schedules. Both require storage, so they work on every durable publish, enlisted or not, and `Direct` is rejected before side effects. `ScheduledAt` accepts past instants and normalizes eligibility to UTC microseconds. Dispatch is best-effort after that not-before instant, with no upper latency bound. The relative-delay header travels with the message; transports do not interpret it. Absolute-only schedules omit that header.
- Keep `PublishReceipt.StorageId` to revoke a schedule through `IMessageRevoker.RevokeAsync` after the publishing transaction commits. Token cancellation cancels the current request; it does not revoke an accepted message. `Revoked` means deletion won before reservation, `NotFound` means no matching row in the configured storage version, and `AttemptReserved` means dispatch, terminal, or retry state prevented deletion. The last outcome is not proof of delivery. Unscheduled rows with initial-dispatch grace are also ineligible. A claim alone does not prevent revocation. Deletion retains no audit record and has no tenant filter; applications must authorize access to handles. Providers without `IMessageRevocationStorage` throw a provider-naming `NotSupportedException`.
- Redis uses Streams for both lanes. AWS Bus consumer identities, RabbitMQ, NATS JetStream, and Pulsar use the physical topologies above.

Messaging provides not-before delivery, revocable until reservation. Keyed identity, replace, reschedule, tenant scoping, and transactional business deadlines belong to Jobs. See [Enlisted Enqueue (Atomic Enqueue)](jobs.md#enlisted-enqueue-atomic-enqueue), which commits an order, a message, and a reminder job together.

### Registration Overloads

Every transport `Use{Provider}(...)` (except `UseInMemory()`, which has no options) exposes the standard overload trio alongside any scalar-convenience form:

- `Use{Provider}(IConfiguration config)` — binds and validates the options from a configuration section.
- `Use{Provider}(Action<TOptions> configure)` — imperative configuration.
- `Use{Provider}(Action<TOptions, IServiceProvider> configure)` — imperative configuration with access to the resolved service provider (for example to pull a secret, connection string, or credential from DI while configuring).

Options are validated on start through their FluentValidation validators. Each provider keeps the `{ProviderToken}MessagingOptions` naming shape (`AmazonSqsMessagingOptions`, `AzureServiceBusMessagingOptions`, `KafkaMessagingOptions`, `NatsMessagingOptions`, `PulsarMessagingOptions`, `RabbitMqMessagingOptions`, `RedisMessagingOptions`).

### Storage Providers

| Provider          | Outbox + persisted retry storage | Table names               | Schema                         |
|-------------------|----------------------------------|---------------------------|--------------------------------|
| `PostgreSql`      | yes (`IDataStorage`)             | `IStorageTableNames`      | schema runner steps            |
| `SqlServer`       | yes (`IDataStorage`)             | `IStorageTableNames`      | schema runner steps            |
| `InMemoryStorage` | yes (`IDataStorage`, in-memory)  | `IStorageTableNames`      | none (process memory)          |

How to read each column:

- **Outbox + persisted retry storage** — the framework's combined storage contract. There is no separate `IRetryStorage` or `ISubscriptionStorage` abstraction; outbox writes and persisted-retry pickups go through the same `IDataStorage` implementation. The brainstorm proposed a "Subscriptions" column; the live code does not expose a subscription-tracking storage seam, so the column was dropped during planning rather than padded with "n/a" values.
- **Table names** — `IStorageTableNames` resolves the quoted, schema-qualified published and received table names (`"headless"."messaging_published"`, `[headless].[MessagingPublished]`). It does not create anything.
- **One relational storage** — `PostgreSql` and `SqlServer` run the same storage, `RelationalDataStorage` in `Headless.Messaging`, written once over the SQL kit's `ISqlDialect` ([sql.md § Store statement kit](sql.md#store-statement-kit-for-provider-authors)). A provider package supplies only its options, its schema contribution, and its registration, so the two engines cannot drift apart in behavior: a fix to a statement is a fix on both.
- **Schema** — the relational providers contribute their DDL to the [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts), which applies it in `StartingAsync`, before the messaging bootstrapper starts. A host that never starts (a test, a console tool that calls `IBootstrapper.BootstrapAsync` by hand) must apply the runner itself: `await provider.GetRequiredService<SchemaRunner>().ApplyAsync()`.
- **Storage row IDs** — `MediumMessage.StorageId`, monitoring APIs, dashboard routes, and bulk storage actions use `Guid`. Storage providers generate row IDs through provider-keyed `IGuidGenerator` strategies, not database defaults. PostgreSQL creates `UUID` `Id` columns and resolves the `Version7` strategy; SQL Server creates `uniqueidentifier` `Id` columns, and resolves the `SqlServer` comb strategy. SQL Server passes ID and owner lists as one JSON parameter read with `OPENJSON`, so the schema creates no table types.
- **Retry row owners** — persisted published and received rows include nullable `Owner` (`node@incarnation`). It is stamped only when a Coordination membership identity is active and is cleared when `LockedUntil` is cleared.

Internal-wiring asymmetries (for example, `Headless.Messaging.Storage.SqlServer` additionally registers `DiagnosticProcessorObserver` and a `DiagnosticRegister` background server for SQL Server-specific telemetry that PostgreSql does not need) are deliberately not surfaced as matrix columns — they are implementation details, not chooser-relevant capabilities.

### Storage schema

Messaging owns the database naming, so the schema is one feature-level setting rather than a copy per
provider. Configure it on `MessagingStorageOptions` through the setup builder; `PostgreSqlOptions`,
`SqlServerOptions`, and both EF options types no longer carry a `Schema` member.

```csharp
services.AddHeadlessMessaging(setup =>
{
    // ... transport registration ...
    setup.ConfigureStorage(storage => storage.Schema = "outbox"); // default: "headless"
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("Messaging")!);
});
```

The default schema is `headless`, which every Headless feature shares; see [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

- **One setting, per-provider validation.** Whichever storage provider is registered validates the value
  once at startup against its own dialect's identifier rules — PostgreSQL's unquoted-identifier rules
  (63 chars) or SQL Server's regular-identifier rules (128 chars). An invalid schema fails host startup
  with an `OptionsValidationException` instead of a later DDL error.
- **Configuration binding.** `ConfigureStorage` also takes an `IConfiguration`, so the schema can come
  from `appsettings.json`: `setup.ConfigureStorage(builder.Configuration.GetSection("Headless:Messaging:Storage"))`.
  Pass the section itself, not the configuration root — its keys map to the option's properties. Provider
  `Use…(IConfiguration)` overloads bind only their own options and never the schema. Both `ConfigureStorage`
  overloads register in call order, so the last one applied wins.
- **EF-context storage paths** read the same setting: `setup.UseEntityFramework<TContext>()` takes no
  schema of its own, so pair it with `ConfigureStorage` when the tables do not live in `headless`.

Table names are not configurable; each provider creates its own fixed set inside the configured schema, prefixed with the feature so they coexist with other features in one schema:

| PostgreSQL | SQL Server | Holds |
| --- | --- | --- |
| `messaging_published` | `MessagingPublished` | Outbox rows |
| `messaging_received` | `MessagingReceived` | Inbox rows |
| `messaging_inbox_operation_receipts` | `MessagingInboxOperationReceipts` | Operator and cleanup receipts |
| `messaging_inbox_audit` | `MessagingInboxAudit` | Operator and cleanup audit |

Columns, indexes, and constraints follow the same split: snake_case on PostgreSQL (`status_name`, `next_retry_at`, `idx_messaging_received_status_name_added`) and PascalCase on SQL Server (`StatusName`, `NextRetryAt`). Raw SQL against the PostgreSQL tables needs no identifier quoting. A received row belongs to one consumer: both providers key it by message ID plus `consumer_identity` / `ConsumerIdentity`, so a message delivered to two consumers keeps one row per consumer, and the dashboard filters received rows by that identity.

### Additional outboxes

A host has one primary storage. When units of work run on other databases too, for example one `DbContext` per
module, register an additional outbox for each of those databases so a unit on it can publish atomically through
`unit.Outbox`:

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UseEntityFramework<OrdersDb>();              // primary: outbox, inbox, retry state, dashboard
    setup.AddOutbox().UseEntityFramework<BillingDb>(); // additional: outbox rows and their relay only
    setup.AddOutbox().UsePostgreSql(builder.Configuration.GetConnectionString("Shipping")!);
});
```

- **Registration.** `AddOutbox()` returns an `OutboxStorageBuilder` that accepts only a storage:
  `UseEntityFramework<TContext>()` from the EF storage packages, and `UsePostgreSql(...)` / `UseSqlServer(...)`
  with the same connection-string, `IConfiguration`, and delegate overloads as the primary storage. Each outbox
  keeps its own named provider options, validated on start. `AddOutbox()` without a storage, or a second storage
  on one `AddOutbox()`, throws during `AddHeadlessMessaging`.
- **Selection.** `unit.Outbox.PublishAsync` / `EnqueueAsync` writes to the primary storage when the unit's
  database is the primary's, and otherwise to the additional outbox whose database matches, decided by the same
  `RelationalDatabaseIdentity` check a single storage runs. A unit that matches no outbox is refused with the
  usual `Database` mismatch. `IBus` / `IQueue` publishes are autonomous and always write to the primary storage.
- **Startup validation.** The primary storage must be relational (PostgreSQL or SQL Server): an in-memory primary
  joins every unit and would take the additional outboxes' publishes. Two outboxes, the primary included, that
  resolve to the same database fail host startup with a `MessagingConfigurationException` naming both
  registrations. Mixing providers is allowed, for example a SQL Server primary with a PostgreSQL outbox.
- **Schema.** Each additional outbox contributes only the published table and its indexes, in the same `MessagingStorageOptions.Schema` as the primary, as its own `MessagingOutbox` feature in that
  database's `headless_schema_history`. It has no received table or inbox history. Its steps are applied by a schema
  runner of its own, in the host runner's `SchemaRunnerMode`, after the host's runner has applied the primary's: the
  host's runner fails startup on an unreachable database, which an additional outbox must not do. The host's runner
  still carries each outbox's steps as an export-only contribution, so `SchemaRunner.ExportScript` includes them in
  its schema's section of the dialect's script. The script is per dialect and schema, not per database: run it against
  the primary's database and every outbox database of that dialect. An outbox database then also holds the primary's
  tables, which nothing reads there; in exchange a host in `Verify` mode can use additional outboxes.
- **Availability at startup.** Only the primary storage must be reachable for the host to start. The additional
  outboxes are initialized concurrently, once each; one whose database is unreachable logs EventId 106 and the
  host starts without it. A background processor then retries it with jittered backoff from 1 second up to
  30 seconds (EventId 107 per failed retry, 108 once it is initialized). Until then its relay (retry, delayed,
  and collector) skips it, and a `unit.Outbox` publish on its database first initializes it, sharing one attempt
  with the retry, then writes; if the initialization fails, the publish throws and the unit rolls back. Its rows
  are relayed only once it is initialized, so a relay can lag a recovered database by up to that backoff.
  Dead-owner recovery and `IMessageRevoker` keep reaching it and report its failures as they do for any outage.
  A misconfigured additional outbox, such as wrong credentials, therefore does not stop startup either: watch for
  EventId 106.
- **Relay.** Each outbox relays its own rows: retry pickup runs one published-retry quadrant per outbox and lane,
  each with its own lock resource, backoff, and pickup-failure count, and the delayed-message claim runs for every
  outbox concurrently. An unreachable outbox database backs off alone; the primary and the other outboxes keep
  relaying. The collector expires published rows in every outbox, dead-owner recovery reclaims published rows in
  every outbox, and `IMessageRevoker.RevokeAsync` finds a scheduled row in whichever outbox holds it.
- **What stays on the primary.** The inbox, received-message retry state, consumer bookkeeping, the monitoring
  API, and the dashboard read only the primary storage, so the dashboard does not list an additional outbox's
  rows.
- **Jobs.** Jobs keeps one store. To schedule a job atomically with a unit on an additional outbox's database,
  publish a message through `unit.Outbox` and schedule the job from that message's consumer on the Jobs database.
  The outbox row commits with the unit, and the consumer's inbox and `unit.Jobs` give the job the same guarantee.

## Writing a Transport Provider

A transport package adapts one broker to `Headless.Messaging`. Core already owns serialization, outbox behavior, retries, delayed publishing, consumer invocation, circuit breaking, and diagnostics orchestration, so a transport must not reimplement those policies. A transport package normally owns:

- a setup class exposing `UseMyBroker(...)` on `MessagingSetupBuilder`
- `MyBrokerOptions` plus a validator
- an `IBusTransport` and/or `IQueueTransport` implementation
- `MyBrokerConsumerClientFactory : IConsumerClientFactory`
- `MyBrokerConsumerClient : IConsumerClient`
- broker-specific pools, factories, or helpers when connection reuse matters

### Registration shape

Register the broker through `MessagingSetupBuilder.RegisterExtension(...)` with an `IMessagesOptionsExtension` that adds:

- `MessageQueueMarkerService("MyBroker")`
- validated broker options
- immutable `MessagingProviderCapabilities` contributions declaring supported lanes and native affinity routes
- singleton `IBusTransport` and/or `IQueueTransport` for the declared lanes
- singleton `IConsumerClientFactory`
- any broker-owned singletons such as connection pools

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

### Publishing: `ITransport.SendAsync`

`SendAsync(...)` receives a fully prepared `TransportMessage`. The transport publishes `message.Body` as the broker payload, preserves `message.Headers`, returns `OperateResult.Success` on broker success, returns `OperateResult.Failed(new PublisherSentFailedException(...))` on broker failure, and lets `OperationCanceledException` propagate.

After `DisposeAsync`, `SendAsync` returns `OperateResult.Failed(new ObjectDisposedException(...))` without contacting the broker. It does not throw, so a publisher treats a send on a disposed transport like any other failed send. This holds even when `DisposeAsync` releases nothing because a shared pool owns the connection: disposal still stops that transport instance from sending. The `should_return_failed_result_when_sending_after_dispose` case in `TransportTestsBase` checks it.

`BrokerAddress` feeds diagnostics, OpenTelemetry, and dashboard surfaces. Make it a sanitized operator-facing value, never a raw connection string with credentials.

### Consuming: `IConsumerClientFactory` and `IConsumerClient`

`IConsumerClientFactory.CreateAsync(ConsumerClientRequest, ...)` is called once during startup topology discovery with the host-stopping token, and once for each live consumer client with its linked per-client token. The factory must therefore be safe to call repeatedly, and client construction must not start background receive loops early. The request fields, lane-dependent `SubscriptionName`, and cancellation rules are listed under the design constraints of [Headless.Messaging](#headlessmessaging); factories must not wrap shutdown cancellation as `BrokerConnectionException`.

On the Bus lane, derive the broker-legal subscription name from the identity with `BusNameBuilder.Build(identity, rules)` and a provider-specific `BusNameRules` (maximum length, accepted characters, whether names must start and end with a letter or digit). An identity the broker accepts is used unchanged. Any other identity becomes a readable prefix plus `-` and a 12-character SHA-256 hash of the whole identity, so every process derives the same name and distinct identities never collide. Do not hash with `string.GetHashCode()`, which is randomized per process. See [Bus subscription names](#bus-subscription-names) for each provider's rules.

- **`FetchMessageNamesAsync`** is the broker-normalization and provisioning hook: create topics, streams, queues, or subscriptions; translate wildcard topics; map friendly names to broker-native identifiers such as ARNs. If the broker uses topic names as-is, the default pass-through is enough. Pass the host-stopping token through broker connection and topology operations. When a provider SDK operation has no native cancellation parameter, await it through a cancellation-aware wait and retain the provider's existing timeout.
- **`SubscribeAsync`** binds the client's subscription (the Bus consumer identity's subscription name, or the Queue message's destination) to the names `FetchMessageNamesAsync` resolved. Pass the linked per-client token through subscription and topology operations, with the same cancellation-aware wait rule.
- **`ListeningAsync`** owns the long-running receive loop. For every delivery, build a `TransportMessage`, leave `Headers.ConsumerIdentity` alone (Core stamps it on receipt and overwrites any value the transport or publisher set), and pass a broker-specific commit token to `OnMessageCallback(message, commitToken)`. Do not swallow `OnMessageCallback` exceptions; Core decides whether to commit, reject, retry, or trip the circuit breaker.
- **`CommitAsync` and `RejectAsync`** map the callback token back to broker semantics: ack or nack, delete or abandon, commit or seek, complete, dead-letter, or requeue. If the broker cannot reject, make that explicit and implement the best available no-op or requeue behavior.
- **`PauseAsync` and `ResumeAsync`** serve circuit-breaker backpressure. They must be idempotent, safe to call concurrently, and stop new message pulls once `PauseAsync` returns. In-flight deliveries may complete. Pause and resume keep the long-running listener alive; a provider may cancel an in-flight broker receive to reach its pause gate, but it must install fresh receive state before reopening the gate.
- **`OnLogCallback`** emits `MqLogType` events for connection failures, broker shutdown, consumer registration and cancellation, and receive-loop errors. Core relies on them to keep transport health and restart behavior accurate.
- **`DisposeAsync`** disposes only resources owned by that client instance, never shared pools or connections still used elsewhere in the package.

### Header and payload rules

The transport must round-trip at least `Headers.MessageId`, `Headers.MessageName`, `Headers.Type`, `Headers.CorrelationId`, `Headers.CorrelationSequence`, and `Headers.SentTime`. It also preserves optional headers such as `Headers.CallbackName`, `Headers.DelayTime`, `Headers.TenantId`, `Headers.TraceParent`, and custom application headers. On the Queue lane it preserves the request headers `Headers.RequestId`, `Headers.ReplyTo`, and `Headers.RequestDeadline`, and a reply transport preserves `Headers.InReplyTo`, `Headers.ReplyStatus`, `Headers.MessageId`, `Headers.MessageName`, `Headers.ContractVersion`, `Headers.TenantId`, and `Headers.TraceParent` on every reply.

- `Headers.ConsumerIdentity` (`headless-msg-consumer-identity`) is stamped by Core on receipt, not on publish; transports must not set it.
- `Headers.TransportAddress` (`headless-transport-address`) is stamped by the consumer client on receipt: set it to the native address the delivery arrived on after reading wire headers and after any `CustomHeadersBuilder`, so neither can choose it. Never send it.
- `Headers.TenantId` is governed by [Strict Publish Tenancy](#strict-publish-tenancy) in Core; transports round-trip it verbatim and never originate, rewrite, or strip it.
- Treat the body as raw bytes unless the broker API forces encoding or decoding.
- Never leak exception details, credentials, or other secrets through headers or `BrokerAddress`.

### Routing-affinity contributions

`TransportMessage.RoutingAffinityKey` reads the reserved `headless-routing-affinity-key` envelope field. Do not accept it through custom application headers or a provider header contribution. Every official storage serializes the envelope, so retries keep the same logical key without a schema column.

A provider contributes immutable `MessagingRoutingAffinityRoute` entries through its `MessagingProviderCapabilities.Transport(...)` contribution. Each entry identifies one registered `(MessageLane, MessageName)` destination and a `MessagingRoutingAffinityMapping` describing the native header adapter, an optional maximum key length, a printable-ASCII restriction, and additional raw headers that must match. An empty list explicitly means no supported native affinity destinations. A contribution may resolve and snapshot inert options and registrations; it must never resolve a broker client, transport, processor, or storage implementation to discover support. The composed Core capability model is the sole runtime authority.

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

Validate raw application headers before evaluating a selector that could overwrite them, and validate the resulting provider contribution again. Transport adapters also validate before renting producers or creating senders, including retry dispatch. Do not guess support from a provider-wide flag or auto-provision a different topology for an unknown keyed destination.

Startup checks establish local declaration consistency only. Broker I/O must separately prove the actual session, FIFO, or partition topology and permissions, so provider conformance binds every route to either a native mapping or deterministic rejection, covers direct publication and outbox dispatch, and preserves keys on broker redelivery.

### Request/reply: `IReplyTransport`

A provider supports [request/reply](#requestreply) by declaring `supportsRequestReply: true` in its `MessagingProviderCapabilities.Transport(...)` contribution and registering a singleton `IReplyTransport`. The flag requires the Queue lane, and Core rejects request/reply at startup on a transport that does not declare it. Core owns correlation, deadlines, tenant checks, fault encoding, and the decision to send; the reply transport only opens a per-process channel and moves replies to it. The seam lives in `Headless.Messaging.Transport`:

- `IReplyTransport.OpenListenerAsync(onReply, cancellationToken)` opens the process's reply channel and returns an `IReplyListener`. Give each listener a fresh address, normally `ReplyAddresses.Create()` (`headless.reply.` followed by 32 hex digits), so a reply to a previous run of the process never reaches it. Invoke `onReply` once per reply, one at a time; an exception it throws is logged and does not stop the listener. Do not dispose the listener from inside `onReply`.
- `IReplyListener.WaitForAddressAsync(cancellationToken)` waits until the channel can receive and returns its current address; it throws `ObjectDisposedException` once the listener is closed. While the connection is down, keep the address back, because a reply sent then reaches nobody. After a reconnect the address may stay the same only when no other process can hold it; otherwise return a new one, and calls sent with the old address time out. Disposing the listener removes every broker object it created, and a crash must leave none behind.
- `IReplyTransport.SendAsync(address, reply, cancellationToken)` writes one reply. Throw `ArgumentException` for an address outside the reply namespace, and drop the reply without an error when nobody listens. Never store a reply, and never write it to a lane destination.
- `IReplyTransport.IsReplyAddress(address)` defaults to `ReplyAddresses.IsInReplyNamespace`: the `headless.reply.` prefix, a non-empty remainder of ASCII letters, digits, `.`, `-`, and `_`, and at most `ReplyAddresses.MaxLength` (200) characters. Override it only to refuse more, such as names over a broker limit. Core sends only to an address that passes both checks; a request carries its own reply address, so on the responding host the address is untrusted input.

```csharp no-compile
services.AddMessagingProviderCapabilities(MessagingProviderCapabilities.Transport(
    "MyBroker", [MessageLane.Queue], supportsIndependentLaneTopology: true, supportsRequestReply: true));
services.AddSingleton<IReplyTransport, MyBrokerReplyTransport>();
```

Declare the flag only when the provider passes the request/reply conformance scenarios: round trip, callers receiving only their own replies, responder faults, `no_responder`, timeouts that never run the responder, late replies dropped, foreign reply addresses never written, a restarted caller never receiving an old reply, tenant flowing both ways, and no reply objects left after the caller stops.

### What a transport must not do

- reimplement serialization policy already handled by `IMessageSerializer`
- invent its own retry policy around `OnMessageCallback`
- commit before Core finishes processing the message
- hide broker failures by swallowing exceptions and returning success
- expose raw credentials in logs, exceptions, or `BrokerAddress`
- couple itself to one application's consumer registration conventions

## Headless.Messaging.Abstractions

### API and behavior

- `PublishReceipt` carries the resolved wire `MessageId` and nullable durable `StorageId`. Direct delivery returns no storage handle. Middleware suppression before terminal publication returns both values null. A receipt enlisted in the caller's active unit of work remains subject to that unit's completion or rollback and never implies consumer completion.
- `IConsume<TMessage>` consumer contract, declared with exactly one of `[BusConsumer(identity)]` (optional `EveryInstance` and `FailurePolicy`) or `[QueueConsumer(identity)]` (optional `FailurePolicy`); both derive from `MessageConsumerAttribute`, which exposes `Identity` and `FailurePolicy`. An every-instance consumer cannot declare a `FailurePolicy` (HM010). `IOnSubscriptionEstablished` is the optional reconnect hook for every-instance consumers. `ConsumeContext.UnitOfWork` is the unit the inbox transaction runner enlisted the attempt in (the `Transactional` inbox guarantee), or `null` under a weaker guarantee and for an every-instance consumer. `ConsumeContext.GetRequiredUnitOfWork()` returns that unit as non-null, or throws `InvalidOperationException` when the attempt has none; a consumer's own enlisted writes go through it — `context.GetRequiredUnitOfWork().Outbox.PublishAsync(…)`, `context.GetRequiredUnitOfWork().Jobs.ScheduleAsync(…)` — so a rolled-back attempt discards them with the inbox row; a callback response is published through the same unit. A consumer that also holds the inbox's `DbContext` reaches it as `db.UnitOfWork()`.
- `IRespond<TRequest, TResponse>` is the responder contract for [request/reply](#requestreply): `ValueTask<TResponse> RespondAsync(ConsumeContext<TRequest>, CancellationToken)` on a `[QueueConsumer]` class, with both type parameters constrained to `class`. In a responder, `ConsumeContext.SetResponse`, `SetResponseCallbackName`, and `SetResponseDestination` throw `InvalidOperationException`, because the return value is the only answer. `ConsumeContext.RecordReply` is generated-code plumbing, hidden from IntelliSense; application code never calls it.
- `MessageOptions` base options, including headers, correlation, mutually exclusive `Delay` and `ScheduledAt`, message id, message type, and tenant id.
- `IMessageRevoker` deletes a scheduled row by `PublishReceipt.StorageId` before its first dispatch reservation. It returns `Revoked`, `NotFound`, or `AttemptReserved`, retains no audit record, and is not tenant-scoped. Use Jobs for keyed, replaceable, tenant-scoped, or transactional deadlines.
- The enlisted publish contract lives here too: `IUnitOfWorkOutbox`, the `UnitOfWorkOutbox` binding, `OutboxOptions`, and the `unit.Outbox` accessor (an extension property on `IUnitOfWork`, in the `Headless.UnitOfWork` namespace, so holding the unit is enough to reach it). The accessor resolves the singleton `IUnitOfWorkOutbox` feature from the host container and binds it to the handle once per unit, keeping the binding as unit-local state (`GetOrAdd`), so repeated reads allocate nothing and a completed unit refuses the read; the binding owns nothing to dispose. `IUnitOfWorkOutbox` itself is plumbing — hidden from IntelliSense, public only so the unit-of-work packages can hand it out — and application code uses the binding. The implementation ships in `Headless.Messaging` and is registered by `AddHeadlessMessaging`; `unit.Outbox` throws an `InvalidOperationException` naming `AddHeadlessMessaging` when the host registered no messaging.
- `unit.Outbox.PublishAsync` sends on the bus lane and `unit.Outbox.EnqueueAsync` on the queue lane; both take one `OutboxOptions` record, because the verb is the lane authority and the record carries no delivery mode — durable capture is the mechanism the publish enlists through, so the per-call mode, the per-type `WithDeliveryMode` policy, and the host default are all bypassed on this surface. The durable row is written inside the unit's transaction: visible when the unit completes, discarded when it rolls back — the difference from `IBus` and `IQueue`, whose rows are written standalone and survive the caller's rollback. It refuses rather than degrades: when the storage cannot join the given unit the call throws before any storage or transport effect, and which units a storage can join is the storage's own answer (the in-memory storage joins a resource-less unit through its buffer, the relational storages join only a same-database relational resource). Liveness is checked per publish against the handle the binding was taken from, before any storage effect: the outbox writer's first act is a registration on that handle, which a completed or rolled-back unit refuses. A publish into a unit you own (`BeginAsync`, `RunAsync`) leaves it replayable; a publish into an observed-mode unit — the `HeadlessDbContext` save pipeline's own save, from a domain-event handler — calls `IUnitOfWork.PreventRetry()` before writing. See [Delivery Modes](#delivery-modes).
- `MessageOptions.SuppressAmbientBusinessContext` preserves captured business metadata by disabling ambient correlation, causation, and tenant defaults. It defaults to `false`; explicit options, registered contract/selector resolution, and diagnostic trace propagation remain unchanged. Required tenancy still rejects a null explicit tenant when suppression is enabled.
- `DeliveryMode` has two values and belongs to the autonomous surface only. `Durable` (default) stores the row first and the relay dispatches it; `Direct` bypasses storage and cannot be combined with `Delay` or `ScheduledAt`. Precedence is per call (`PublishOptions.DeliveryMode` / `QueueOptions.DeliveryMode`), then per type (`WithDeliveryMode`), then `MessagingOptions.DefaultDeliveryMode`. `MessageOptions`, the shared base, carries no mode and no enlistment: `OutboxOptions` adds neither, because an enlisted publish is durable by construction. See [Delivery Modes](#delivery-modes).
- `MessageHeader`, `Headers`, `TransportMessage`, and broker address primitives.
- The contribution surface: `services.ConfigureMessaging(...)` with `MessagingContributionBuilder` (`Message<T>(name, version)` returning `IMessageContractBuilder<T>` for `CorrelateBy`, `OnBus`, and `OnQueue`, and `AddModule<T>()`), plus the generated-module contract `IMessagingModule` and the hidden `MessagingCatalogBuilder` its `Register` writes to (`MessagingCatalogBuilder.ConsumerIdentityMaxLength` is 200). `Tune(identity, ...)` is added by `Headless.Messaging`.
- Common transport pause/resume and retry/backoff abstractions.

### Design constraints

Headers are not a free-form control plane. Framework-owned headers are reserved because transports, storage, tenancy, retry, and diagnostics depend on their integrity.

### Install

```bash
dotnet add package Headless.Messaging.Abstractions
```

### Setup and use

```csharp
[BusConsumer("orders.projection")]
public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
```

A consumer that publishes a follow-up message publishes it through the attempt's unit, so the message commits with the inbox row and a retried attempt does not send it twice:

```csharp
[QueueConsumer("orders.fulfillment")]
public sealed class OrderFulfillmentConsumer : IConsume<OrderPlaced>
{
    public async ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        var unit = context.GetRequiredUnitOfWork();

        await unit.Outbox.PublishAsync(new OrderChanged(context.Message.OrderId), cancellationToken);
    }
}
```

A library that ships consumers references `Headless.Messaging` (for the generator and `ConfigureMessaging`) and exposes an `Add{Module}` entry point that adds its generated module; see [Orientation](#orientation).

### Configuration

`MessageOptions.RoutingAffinityKey` is an optional provider-neutral string. `TransportMessage.RoutingAffinityKey` reads its reserved `headless-routing-affinity-key` envelope header. Use the typed option; custom writes to that neutral header are rejected. Null preserves unkeyed behavior. Nonempty keys must satisfy the configured provider bounds and raw provider adapters must agree with the typed value.

None.

### Runtime behavior

None.

## Headless.Messaging.Bus.Abstractions

### API and behavior

- `PublishReceipt` carries the resolved wire `MessageId` and nullable durable `StorageId`. Direct delivery returns no storage handle. Middleware suppression before terminal publication returns both values null. A receipt enlisted in the caller's active unit of work remains subject to that unit's completion or rollback and never implies consumer completion.
- `IBus` is the autonomous bus publisher — a singleton that never joins a caller's unit of work. An unset `PublishOptions.DeliveryMode` inherits the per-type `WithDeliveryMode` policy, then `MessagingOptions.DefaultDeliveryMode`, which defaults to `Durable`. Explicit modes override both. For a publish that must live inside the caller's transaction, use `unit.Outbox` (see [Headless.Messaging.Abstractions](#headlessmessagingabstractions)).
- Durable delivery persists messages first, then drains them through the configured bus transport.
- `PublishOptions.Delay` or `PublishOptions.ScheduledAt` schedules durable bus delivery. Supply one scheduling form. `Direct` rejects either form; `OutboxOptions` accepts both.
- `PublishOptionsBuilder` and the `BusExtensions.PublishAsync` callback author canonical options snapshots without a Core dependency.
- Every bus publish carries `MessageLane.Bus` through storage, tracing, dashboard projections, and consume context.

### Install

```bash
dotnet add package Headless.Messaging.Bus.Abstractions
```

### Setup and use

```csharp
using Headless.Messaging;

public sealed class OrderEvents(IBus bus)
{
    public Task PublishAsync(OrderPlaced message, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(message, cancellationToken);
    }

    public Task PublishWithMetadataAsync(OrderPlaced message, string correlationId, CancellationToken cancellationToken)
    {
        return bus.PublishAsync(message, options => options
            .WithHeader("source", "checkout")
            .WithCorrelationId(correlationId), cancellationToken);
    }
}

public sealed record OrderPlaced(Guid OrderId);
```

The short overload uses the registered message contract and inherits the per-type policy or the host delivery mode, which defaults to `Durable`: the message is stored first, standalone, and the relay dispatches it. A unit of work active in the calling scope changes nothing here — the row survives its rollback. Pass `PublishOptions` before the cancellation token for metadata or delivery overrides. Durable acceptance waits for storage, not consumer completion; restart survival requires persistent storage. `Direct` bypasses storage and cannot be combined with `Delay` or `ScheduledAt`.

`PublishAsync` and `EnqueueAsync`, including callback overloads, now return `Task<PublishReceipt>`. Existing `await` statements and `Func<Task>` adapters can ignore the result because `Task<PublishReceipt>` derives from `Task`. Custom `IBus`/`IQueue` implementations and test doubles must update their return signatures and supply a receipt; recompile consumers for this binary API break. Use `var receipt = await bus.PublishAsync(message, cancellationToken);` to retain the returned identity.

Omit an unused cancellation token, or pass `default` or `cancellationToken: default` to select the existing token overload. Supplying `default` followed by a cancellation token selects the options overload. Use `options:` and `configure:` to make record and callback intent explicit.

Import `Headless.Messaging` for `PublishOptionsBuilder` and the callback extension. Its operations are `WithHeader`, `WithHeaders`, `WithCorrelationId`, `WithCausationId`, `WithMessageId`, `WithTenantId`, `WithDelay`, `WithScheduledAt`, and `Build`. Use a callback for a single authoring scope or construct a builder directly and call `Build()` for a reusable options template. Delivery-mode and other advanced overrides remain available through `PublishOptions` or `builder.Build() with { ... }`.

Each callback runs synchronously exactly once on a fresh builder; async-void callbacks are unsupported. A null receiver or `configure` throws before user code, and a throwing callback submits nothing. The adapter forwards the original token and returns the original task. `options: null` and positional `null` keep the existing options path; `configure: null!` selects the callback guard.

Builders support sequential reuse, not concurrent mutation. Header input is copied immediately and again on each `Build()`: ordinal keys, last-write-wins merges, distinct casing, and null values are preserved. No header call leaves `Headers` null; an empty supplied collection creates an empty dictionary. Each result owns mutable headers independently. Nullable metadata and delay setters accept null to clear an explicit value. `Build()` leaves `DeliveryMode` null to inherit the host default and does not validate or accept delivery; the publisher still validates headers, tenancy, and positive delays (zero is invalid).

### Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging` plus bus transport and storage providers.

### Runtime behavior

None. This package registers no services.

## Headless.Messaging.Queue.Abstractions

### API and behavior

- `PublishReceipt` carries the resolved wire `MessageId` and nullable durable `StorageId`. Direct delivery returns no storage handle. Middleware suppression before terminal publication returns both values null. A receipt enlisted in the caller's active unit of work remains subject to that unit's completion or rollback and never implies consumer completion.
- `IQueue` is the autonomous queue publisher — a singleton that never joins a caller's unit of work. An unset `QueueOptions.DeliveryMode` inherits the per-type `WithDeliveryMode` policy, then `MessagingOptions.DefaultDeliveryMode`, which defaults to `Durable`. Explicit modes override both. For an enqueue that must live inside the caller's transaction, use `unit.Outbox` (see [Headless.Messaging.Abstractions](#headlessmessagingabstractions)).
- Durable delivery persists messages first, then drains them through the configured queue transport.
- `QueueOptions.Delay` or `QueueOptions.ScheduledAt` schedules durable queue delivery. Supply one scheduling form. `Direct` rejects either form; `OutboxOptions` accepts both.
- `QueueOptionsBuilder` and the `QueueExtensions.EnqueueAsync` callback author canonical options snapshots without a Core dependency.
- Every queue enqueue carries `MessageLane.Queue` through storage, tracing, dashboard projections, and consume context.
- `IRequestClient.RequestAsync<TRequest, TResponse>(request, RequestOptions?, CancellationToken)` sends a request on the Queue lane and awaits one response. `RequestOptions` carries `Timeout`, `TenantId`, `CorrelationId`, and `Headers` only. Failures derive from `RequestReplyException` (`RequestTimeoutException`, `RequestFaultedException`, `ResponseContractMismatchException`, `RequestNotSentException`, `RequestAbortedException`), except the caller's own cancellation (`OperationCanceledException`) and an unreadable response body (`MessageDeserializationException`), and `RequestFaultCodes` holds the fault codes. `IRequestClient` is registered only by `setup.AddRequestReply()` in `Headless.Messaging`. See [Request/reply](#requestreply).

### Install

```bash
dotnet add package Headless.Messaging.Queue.Abstractions
```

### Setup and use

```csharp
using Headless.Messaging;

public sealed class ImportJobs(IQueue queue)
{
    public Task EnqueueAsync(ImportRequested message, CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(message, cancellationToken);
    }

    public Task EnqueueWithMetadataAsync(ImportRequested message, string correlationId, CancellationToken cancellationToken)
    {
        return queue.EnqueueAsync(message, options => options
            .WithHeader("source", "checkout")
            .WithCorrelationId(correlationId), cancellationToken);
    }
}

public sealed record ImportRequested(Guid ImportId);
```

The short overload uses the registered message contract and inherits the per-type policy or the host delivery mode, which defaults to `Durable`: the message is stored first, standalone, and the relay dispatches it. A unit of work active in the calling scope changes nothing here — the row survives its rollback. Pass `QueueOptions` before the cancellation token for metadata or delivery overrides. Durable acceptance waits for storage, not consumer completion; restart survival requires persistent storage. `Direct` bypasses storage and cannot be combined with `Delay` or `ScheduledAt`.

`PublishAsync` and `EnqueueAsync`, including callback overloads, now return `Task<PublishReceipt>`. Existing `await` statements and `Func<Task>` adapters can ignore the result because `Task<PublishReceipt>` derives from `Task`. Custom `IBus`/`IQueue` implementations and test doubles must update their return signatures and supply a receipt; recompile consumers for this binary API break. Use `var receipt = await bus.PublishAsync(message, cancellationToken);` to retain the returned identity.

Omit an unused cancellation token, or pass `default` or `cancellationToken: default` to select the existing token overload. Supplying `default` followed by a cancellation token selects the options overload. Use `options:` and `configure:` to make record and callback intent explicit.

Import `Headless.Messaging` for `QueueOptionsBuilder` and the callback extension. Its operations are `WithHeader`, `WithHeaders`, `WithCorrelationId`, `WithCausationId`, `WithMessageId`, `WithTenantId`, `WithDelay`, `WithScheduledAt`, and `Build`. Use a callback for a single authoring scope or construct a builder directly and call `Build()` for a reusable options template. Delivery-mode and other advanced overrides remain available through `QueueOptions` or `builder.Build() with { ... }`.

Each callback runs synchronously exactly once on a fresh builder; async-void callbacks are unsupported. A null receiver or `configure` throws before user code, and a throwing callback submits nothing. The adapter forwards the original token and returns the original task. `options: null` and positional `null` keep the existing options path; `configure: null!` selects the callback guard.

Builders support sequential reuse, not concurrent mutation. Header input is copied immediately and again on each `Build()`: ordinal keys, last-write-wins merges, distinct casing, and null values are preserved. No header call leaves `Headers` null; an empty supplied collection creates an empty dictionary. Each result owns mutable headers independently. Nullable metadata and delay setters accept null to clear an explicit value. `Build()` leaves `DeliveryMode` null to inherit the host default and does not validate or accept delivery; the publisher still validates headers, tenancy, and positive delays (zero is invalid).

### Configuration

None in this package. Runtime wiring is provided by `Headless.Messaging` plus queue transport and storage providers.

### Runtime behavior

None. This package registers no services.

## Request/reply

A caller sends a typed request on the Queue lane and awaits one typed response. `IRequestClient.RequestAsync<TRequest, TResponse>` sends the request, the request's one Queue consumer answers it through `IRespond<TRequest, TResponse>`, and the reply returns over a reply channel that belongs to the calling process. Use it for latency-tolerant commands whose caller cannot continue without the answer, such as a quote, a validation result, or a reservation. It is not a durable workflow: the caller's pending call lives only in its process, so durable multi-step coordination stays with Jobs.

### Request/reply versus callbacks

| | Request/reply | Bus callback |
| --- | --- | --- |
| Who waits | The caller awaits `RequestAsync` | Nobody; the response is a new Bus message |
| Declared by | A `[QueueConsumer]` class that implements `IRespond<TRequest, TResponse>` | `CallbackName` on the publish options, and `SetResponse` in an `IConsume<T>` consumer |
| Response path | A reply channel of the calling process; never stored, never a lane message | A durable Bus publish |
| Delivery | At most once: a lost reply ends the call in a timeout | At least once: response consumers must be idempotent |
| Answers per request | One, from the request's one Queue consumer | One per consumer; a Bus request fans out |
| Providers | InMemory, RabbitMQ, NATS, Redis | Every provider with a Bus lane |

Use a callback when nothing waits for the answer or the answer must survive a restart. Use request/reply when the caller needs the answer to continue and can handle a timeout.

### Setup

The responding service declares the responder. It needs no request/reply registration of its own, only a transport that supports request/reply:

```csharp
// Contracts both services share: the request and the response.
public sealed record GetQuote(string Sku, int Quantity);

public sealed record Quote(string Sku, decimal UnitPrice, DateTimeOffset ValidUntil);

public interface IPriceBook
{
    ValueTask<decimal> GetUnitPriceAsync(string sku, CancellationToken cancellationToken);
}

// Pricing service: the responder is the request's one Queue consumer.
[QueueConsumer("pricing.get-quote")]
public sealed class GetQuoteResponder(IPriceBook prices, TimeProvider clock) : IRespond<GetQuote, Quote>
{
    public async ValueTask<Quote> RespondAsync(ConsumeContext<GetQuote> context, CancellationToken cancellationToken)
    {
        var unitPrice = await prices.GetUnitPriceAsync(context.Message.Sku, cancellationToken);

        return new Quote(context.Message.Sku, unitPrice, clock.GetUtcNow().AddMinutes(5));
    }
}

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(options =>
    {
        options.HostName = "localhost";
        options.UserName = builder.Configuration["RabbitMq:UserName"]!;
        options.Password = builder.Configuration["RabbitMq:Password"]!;
    });
    setup.UseEntityFramework<AppDbContext>();
    setup.AddModule<Pricing.MessagingModule>();
});

builder.Services.ConfigureMessaging(messaging =>
{
    messaging.Message<GetQuote>("pricing.get-quote");
    messaging.Message<Quote>("pricing.quote");
});
```

The calling service enables requests with `AddRequestReply` and injects `IRequestClient`:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    setup.UseRabbitMq(options =>
    {
        options.HostName = "localhost";
        options.UserName = builder.Configuration["RabbitMq:UserName"]!;
        options.Password = builder.Configuration["RabbitMq:Password"]!;
    });
    setup.UseEntityFramework<AppDbContext>();

    // Registers IRequestClient and opens this host's reply channel when messaging starts.
    setup.AddRequestReply(requests => requests.DefaultTimeout = TimeSpan.FromSeconds(10));
});

builder.Services.ConfigureMessaging(messaging =>
{
    messaging.Message<GetQuote>("pricing.get-quote");
    messaging.Message<Quote>("pricing.quote");
});

public sealed class CheckoutPricing(IRequestClient requests)
{
    public Task<Quote> QuoteAsync(string sku, int quantity, CancellationToken cancellationToken) =>
        requests.RequestAsync<GetQuote, Quote>(
            new GetQuote(sku, quantity),
            new RequestOptions { Timeout = TimeSpan.FromSeconds(5) },
            cancellationToken
        );
}

public sealed record GetQuote(string Sku, int Quantity);

public sealed record Quote(string Sku, decimal UnitPrice, DateTimeOffset ValidUntil);
```

- **`AddRequestReply` is for callers only.** It registers `IRequestClient` and a reply listener that opens when messaging starts, before any consumer, and closes when it stops. A host that never calls it opens no reply channel and has no `IRequestClient` registration. Calling it more than once is harmless.
- **Both services must resolve the response type to the same contract.** The responder stamps the response's contract name and version from its own registry, and the caller compares them with its own registry for `TResponse`. Declare the request and the response with `Message<T>` in a module both services add. The response contract needs no consumer.
- **Options.** `RequestReplyOptions.DefaultTimeout` (`setup.Options.RequestReply`, default 30 seconds, greater than zero, at most 10 minutes) applies when a call sets no `RequestOptions.Timeout`. A per-call `Timeout` takes at most the same 10 minutes (`RequestOptions.MaxTimeout`); a longer value throws `ArgumentOutOfRangeException`. `IncludeExceptionDetailsInFaults` (default `false`) is read by the responding host and applies there without `AddRequestReply`. `MaxPendingRequests` (default `null`, no limit; when set, greater than zero) caps how many calls this host may have waiting for a reply at once; see [Sending requests](#sending-requests).
- **Startup checks.** A host that calls `AddRequestReply`, or declares a responder, fails startup with a `MessagingConfigurationException` naming the transport when the transport does not support request/reply. A registered message name that starts with `headless.reply.` after `MessagingOptions.MessageNamePrefix` is applied fails startup on either lane, because that prefix is reserved for reply addresses.

### Responders

- **Declaration.** A responder is a `[QueueConsumer]` class that implements `IRespond<TRequest, TResponse>`. It is the request's one Queue consumer: a second Queue consumer or responder for the same request fails the build (HM004) or startup. `[BusConsumer]` on a responder is HM011, `IConsume<T>` and `IRespond<T, TResponse>` on one class for the same `T` is HM012, and two response types for one request is HM013. A `RequestAsync<TRequest, TResponse>` call whose `TResponse` is not what a responder the project can see answers `TRequest` with is HM014, a warning; a caller that sees no responder, the usual case for another service, is not checked.
- **Cancellation.** The `RespondAsync` token is canceled when the host stops. It is not tied to the request's deadline or to the caller's cancellation, because work the responder accepted continues after the caller stops waiting.
- **Never return null.** A `null` result ends the request at once, with no retry, and the caller gets a `null_response` fault.
- **No callbacks.** In a responder, `SetResponse`, `SetResponseCallbackName`, and `SetResponseDestination` throw `InvalidOperationException`; the return value is the only answer. A `CallbackName` that the request carries on the wire is ignored.
- **A plain enqueue still runs it.** `IQueue.EnqueueAsync` of the request type reaches the responder like any Queue consumer; it runs and its result is discarded.
- **Only a Queue message is a request.** A Bus message that carries the request headers is consumed as an ordinary Bus message: no deadline check, no `no_responder` skip, and no reply.
- **A request never reaches a plain consumer.** When a request arrives at a host whose consumer for that message is a plain `IConsume<T>`, the host commits and skips it before inbox admission (receive outcome `no_responder`, no storage row) and answers with a `no_responder` fault, so that code always means no work ran. The `skipped` outcome is reserved for a receive-middleware skip.
- **Normal consume pipeline.** A request is admitted to the inbox, deduplicated, and runs the Queue-lane receive and consume middleware, the circuit breaker, and the inbox transaction runner like any Queue message. `ConsumeOnly` filters a responder like any competing consumer.
- **The reply leaves only after the outcome is durable.** Under the `Transactional` inbox guarantee the reply is sent after the attempt's transaction commits; a rolled-back or indeterminate commit sends nothing. Under a weaker guarantee it is sent after the success write takes effect. Only the attempt whose state write won replies. A crash between the durable outcome and the reply leaves the caller with a timeout. Sending a reply is best effort: a failure is logged and never retried. Each send is bounded by `MessagingOptions.TransportPublishTimeout`, not by host shutdown; a send that outlasts it is given up and logged like any other failed send, so a stalled broker cannot hold the responder's consumer.
- **Tenant scope.** When the host registers messaging tenant propagation (`AddHeadlessTenancy(t => t.Messaging(m => m.PropagateTenant()))`), every Queue consumer runs inside the envelope tenant's scope, responder or plain, under every inbox guarantee, as a Bus consumer does; request/reply adds nothing of its own here. The reply carries the request's tenant verbatim, whatever the ambient tenant is.

### Model expected outcomes in the response

Return an expected business outcome, such as "not found", "rejected", or "out of stock", as part of `TResponse`. Throw only for failures. A thrown exception is retried inline under the host's retry policy and then reaches the caller as a `handler_failed` fault that carries no detail by default, so the caller cannot tell "not found" from a crash, and the responder wastes retries on an answer that will not change.

```csharp
public sealed record FindCustomer(Guid CustomerId);

// "Not found" is an expected answer, so the response carries it instead of the responder throwing.
public sealed record CustomerLookup(CustomerSummary? Customer)
{
    public static CustomerLookup NotFound { get; } = new(Customer: null);
}

public sealed record CustomerSummary(Guid Id, string DisplayName);

public interface ICustomerDirectory
{
    ValueTask<CustomerSummary?> FindAsync(Guid customerId, CancellationToken cancellationToken);
}

[QueueConsumer("customers.find-customer")]
public sealed class FindCustomerResponder(ICustomerDirectory directory) : IRespond<FindCustomer, CustomerLookup>
{
    public async ValueTask<CustomerLookup> RespondAsync(
        ConsumeContext<FindCustomer> context,
        CancellationToken cancellationToken
    )
    {
        var customer = await directory.FindAsync(context.Message.CustomerId, cancellationToken);

        return customer is null ? CustomerLookup.NotFound : new CustomerLookup(customer);
    }
}

public sealed class CustomerNames(IRequestClient requests)
{
    public async Task<string?> GetDisplayNameAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var lookup = await requests.RequestAsync<FindCustomer, CustomerLookup>(
            new FindCustomer(customerId),
            cancellationToken: cancellationToken
        );

        return lookup.Customer?.DisplayName;
    }
}
```

### Sending requests

`RequestOptions` carries `Timeout`, `TenantId`, `CorrelationId`, and `Headers`, and nothing else:

- **The framework owns the request.** It assigns the message id and a `headless-request-id` (a UUIDv7) to each call, so inbox duplicate detection never swallows a legitimate retry. `RequestOptions.Headers` rejects reserved messaging headers and control characters with `InvalidOperationException`.
- **A request is always sent directly.** It is never captured in the outbox, delayed, scheduled, or given a callback name; `WithDeliveryMode` and `MessagingOptions.DefaultDeliveryMode` do not apply. The caller's storage keeps no published row for it.
- **Middleware and tracing.** A request passes through the Queue-lane publish middleware; middleware that suppresses it ends the call with `RequestNotSentException`. The request span injects trace context, and the reply carries the responder span's `traceparent`. No Bus publish or consume middleware runs on a reply.
- **Not inside a transactional inbox unit.** `RequestAsync` called while a consumer attempt runs inside an inbox transaction (its `ConsumeContext.UnitOfWork` is not null, which is every consumer under the `Transactional` inbox guarantee) throws `InvalidOperationException`, because waiting for the reply would hold the database transaction and the inbox lease for the whole timeout. Send the request after the unit completes, or from a consumer under a weaker inbox guarantee. An application unit of work opened with `IUnitOfWorkFactory` does not block a request, but the request never joins it: it is sent at once, and a rollback does not recall it.
- **Tenant.** The request carries `RequestOptions.TenantId`, or else the ambient `ICurrentTenant.Id` that the Queue-lane tenant publish middleware stamps when the host registers messaging tenant propagation, under that middleware's rules (a blank or over-length ambient value is skipped). `RequestOptions` has no switch to suppress the ambient tenant; set `TenantId` to override it. The caller drops a reply whose tenant differs from the request's, where no tenant matches only no tenant, and the call then times out.
- **Pending limit.** With `RequestReply.MaxPendingRequests` set, a call made while that many calls already wait throws `RequestNotSentException` at once, publishes nothing, and counts as `not_sent`; retrying it cannot repeat work. A slot frees the moment a waiting call ends, by reply, fault, contract mismatch, timeout, the caller's cancellation, a failed send, or shutdown. Without a limit, waiting calls are still bounded by request rate times timeout, so they pile up only while responders are slow or down; the default is unlimited because any fixed number either never triggers or refuses healthy high-throughput traffic. Size the limit from the host's peak request rate and timeout. The count is per host process, not per request type.
- **Shutdown.** When the calling host stops, new calls throw `RequestNotSentException` and pending calls fail with `RequestAbortedException` before the reply listener closes. When the caller is itself a consumer, that failure during its own host's shutdown counts as shutdown, not as a consume failure: the attempt writes no failed state, raises no `RetryPolicy.OnExhausted`, sends no fault, reports no circuit-breaker failure, and the stored message is picked up again after restart.

### Outcomes and exceptions

A call that passes argument validation ends in exactly one outcome below, and it never returns `null`. Each failure outcome except the caller's own cancellation and an unreadable response body derives from `RequestReplyException`, which exposes `RequestId`, so one `catch` covers them. An unreadable response body surfaces as `MessageDeserializationException`, the one failure outside that hierarchy.

| Outcome | Surfaces as | Did the responder run? | Safe to retry? |
| --- | --- | --- | --- |
| Response | The `TResponse` value | Yes, and its outcome is durable | Not needed |
| Timeout | `RequestTimeoutException` (`Timeout`) | Unknown: it may have finished, or its reply may have been lost | Only when the responder is idempotent |
| Fault | `RequestFaultedException` (`Code`, plus `RemoteExceptionType` and `Detail` when the responder host opts in) | Depends on `Code`; see below | Depends on `Code` |
| Contract mismatch | `ResponseContractMismatchException` (expected and actual name and version) | Yes, it completed | No: the two services disagree on the response contract and need a coordinated deployment |
| Not sent | `RequestNotSentException` | No when it never left the caller; almost never when the transport reported the send as failed | Yes when it never left the caller; when the transport reported a failure, only if the responder is idempotent |
| Aborted | `RequestAbortedException` | Unknown: the request was sent and the caller stopped | Only when the responder is idempotent |
| Caller canceled | `OperationCanceledException` | Unknown when the request was already sent; canceling ends only the wait | Only when the responder is idempotent |
| Unreadable response body | `MessageDeserializationException` | Yes, it completed | No: fix the response contract |

`RequestNotSentException` covers publish middleware that suppressed the request, a send the transport reported as failed (its `InnerException` is the transport's exception, such as `PublisherSentFailedException`), a calling host that is stopping, a calling host already at its `RequestReply.MaxPendingRequests` limit, and a reply listener that did not become ready within the call's timeout, for example while the broker is unreachable. A transport-reported failure usually means the broker rejected the request, but a connection lost after the broker stored it surfaces the same way, so it is not a guarantee that no responder runs. A send that outlasts `MessagingOptions.TransportPublishTimeout` is not "not sent": the broker may have taken it, so the call keeps waiting and ends with the response or `RequestTimeoutException`. `RequestAsync` also throws `ArgumentNullException` for a null request and `InvalidOperationException` for the transactional-unit and reserved-header cases above.

Fault codes are stable wire values in `RequestFaultCodes`:

| Code | Meaning | Did work run? |
| --- | --- | --- |
| `handler_failed` | The responder failed terminally before the deadline: its immediate retries ran out or were cut short by the deadline, or its failure policy ended the failure at once (a fail rule or the built-in permanent set) | It ran and failed; its effects may be partial |
| `no_responder` | The request reached a consumer that does not respond, or a host with no consumer for it; the host committed and skipped it | No. Safe to retry once a responder is deployed |
| `request_rejected` | The responding host rejected the request on arrival: a contract version mismatch, a body it could not deserialize, or receive middleware that rejected it | No |
| `null_response` | The responder returned `null` | It ran to completion |

A fault body is never empty, and an unreadable one surfaces as `handler_failed`. `RemoteExceptionType` and `Detail` stay `null` unless the responding host sets `RequestReply.IncludeExceptionDetailsInFaults`, because exception messages can carry internal details.

```csharp
public sealed class QuoteGateway(IRequestClient requests)
{
    public async Task<Quote?> TryQuoteAsync(GetQuote request, CancellationToken cancellationToken)
    {
        try
        {
            return await requests.RequestAsync<GetQuote, Quote>(request, cancellationToken: cancellationToken);
        }
        catch (RequestNotSentException)
        {
            // Nothing reached a responder, so a retry cannot repeat work.
            return null;
        }
        catch (RequestFaultedException e) when (e.Code == RequestFaultCodes.NoResponder)
        {
            // The request reached a consumer that does not respond, so no work ran.
            return null;
        }
        catch (RequestTimeoutException)
        {
            // Ambiguous: the responder may have finished. Retry only when GetQuote is idempotent.
            return null;
        }
    }
}

public sealed record GetQuote(string Sku, int Quantity);

public sealed record Quote(string Sku, decimal UnitPrice);
```

### Deadlines, timeouts, and clock skew

- **Every request has a deadline.** The deadline is the send time plus the call's timeout, carried as an absolute UTC instant in `headless-request-deadline`. The caller's own timer stays authoritative for the call.
- **The responder checks the deadline on its own clock.** A request that arrives expired is committed and dropped before inbox admission: receive outcome `expired`, no storage row, no reply, no `RetryPolicy.OnExhausted`, and no circuit-breaker failure. The check repeats at the start of every attempt, including immediate retries, persisted pickups, and lease recovery after a crash; an expired request then ends terminally with the same absence of reply, callback, and breaker report. A request whose deadline header is missing or unreadable has no deadline.
- **A started attempt is not interrupted.** The handler's token is not tied to the deadline, so an attempt that started in time runs to completion. Its reply then arrives after the caller stopped waiting and is dropped as `late`.
- **A force-reprocessed request runs as a plain Queue message.** An operator replays a terminal request long after its caller stopped waiting, so the replayed generation drops `headless-reply-to` and `headless-request-deadline` before its first attempt and keeps `headless-request-id`. Three things follow: the consumer runs under the host's full [failure policy](#consumer-failure-policy), delayed retries included; no reply and no fault are sent, whatever the outcome; and the replay is logged once per dispatch that still finds the request headers on the stored envelope (EventId 4116, `ReplayedRequestRunsWithoutCaller`); immediate retries inside that dispatch do not log again. The parent row keeps the original envelope, so the dashboard still shows the request as it arrived.
- **A nested request inherits the inbound deadline.** When a consumer that is answering a request sends its own request, the nested call's timeout is the smaller of the requested (or default) timeout and what is left of the inbound request's deadline on this host's clock; the capped value is the deadline the nested request carries and the `Timeout` a `RequestTimeoutException` reports. When the inbound deadline has already passed, the nested call throws `RequestNotSentException` naming it, publishes nothing, and counts as `not_sent`. A consumer answering a plain Queue or Bus message, or a request whose deadline header is missing or unreadable, inherits nothing. The inherited value is read from `ConsumeContext.Headers`, so consume middleware that rewrites the deadline header changes what a nested call inherits, while the executor keeps enforcing the stored envelope. Inheritance follows the consume's async context: a task started inside the handler inherits it, a unit-of-work completion callback does not.
- **Clock skew moves the window.** The deadline is written on the caller's clock and read on the responder's. A responder clock that runs ahead expires requests early, and shrinks what a nested request may inherit; one that runs behind starts work after the caller gave up. Keep host clocks synchronized and timeouts much longer than the expected skew.
- **A timeout is ambiguous.** The responder may have completed the work after the caller stopped waiting, or its reply may have been lost after the work became durable. Make responders idempotent, for example by deduplicating on a business key the request carries, before a caller retries after a timeout.
- **The caller's continuation is at most once.** The pending call lives only in the calling process. A caller that restarts loses its pending calls, and its new listener has a new address, so a reply to the previous run never reaches it. There is no persisted caller state and no reply recovery.

### Retries

A request uses the responder's [failure policy](#consumer-failure-policy) and its fail rules, but only its immediate retries. It never takes a delayed retry through the persisted retry processor, because its caller waits only until the deadline:

- **Immediate retries run while the deadline allows.** When the policy's immediate retries are spent, or the deadline has passed, the request ends as exhausted instead of scheduling a delayed retry, even when the policy declares delayed retries: the caller gets a `handler_failed` fault, then `RetryPolicy.OnExhausted` fires. The reply is sent before the callback runs, so the caller is not held up by it.
- **A permanent failure ends at once.** A failure that a fail rule matches, or that is in the built-in permanent set, ends the request with a `handler_failed` fault, and `OnExhausted` fires, as for any consumer.
- **A null response and an expired request never retry.** `null_response` ends the request without `OnExhausted`, and an expired request ends with neither a reply nor `OnExhausted`.
- **A replayed request follows the full failure policy.** A generation an operator force-reprocessed is no longer a request (see [Deadlines](#deadlines-timeouts-and-clock-skew)), so it takes delayed retries through the persisted retry processor like any Queue message, fires `OnExhausted` when it ends terminally, and never sends a fault.
- **A request rejected on arrival** (contract version, deserialization, or receive middleware) gets a `request_rejected` fault from the poison path, and `OnExhausted` fires as it does for any rejected delivery.

Receive middleware that skips a request (`context.Skip(...)`) sends no reply, so its caller times out.

### Telemetry

| Instrument | Kind | Dimension and values |
| --- | --- | --- |
| `messaging.request_reply.requests` | Counter | `messaging.request_reply.outcome`: `replied`, `faulted`, `contract_mismatch`, `timed_out`, `canceled`, `aborted`, `not_sent`, `failed` |
| `messaging.request_reply.duration` | Histogram (ms) | `messaging.request_reply.outcome` |
| `messaging.request_reply.dropped_replies` | Counter | `messaging.request_reply.drop_reason`: `late` (the call already ended), `duplicate` (the call already had its reply), `unknown` (no call of this process sent it), `tenant_mismatch`, `invalid_reply_address` (counted on the responding host, which wrote nothing) |
| `messaging.receive.outcomes` | Counter | `messaging.receive.outcome`: `expired` for a request dropped on arrival after its deadline; `no_responder` for a request that reached a plain consumer; `skipped` only for a receive-middleware skip |

No request id, correlation id, or instance id is a metric dimension.

### Provider support

| Provider | Request/reply | Reply channel |
| --- | --- | --- |
| InMemory | Supported | An in-process channel per listener |
| RabbitMQ | Supported | An exclusive, non-durable queue named `headless.reply.{32 hex}`, consumed with automatic acknowledgement on a connection of the listener's own; replies go through the default exchange with the queue name as routing key and never touch the lane exchanges |
| NATS | Supported | A core NATS subscription, outside JetStream, on the subject `headless.reply.{32 hex}`, on a pooled connection |
| Redis | Supported | A pub/sub subscription to the literal channel `headless.reply.{32 hex}`; `PUBLISH` writes no key |
| Azure Service Bus | Not yet: startup fails until its reply channel ships | |
| Pulsar | Not yet: startup fails until its reply channel ships | |
| Kafka | No: startup fails | Kafka has no per-process address short of a partition per instance |
| AWS SNS/SQS | No: startup fails | AWS has no .NET temporary-queue client, and per-process queues are billed |

Every supported provider removes its reply objects when the caller stops or dies: the exclusive queue, subscription, or channel ends with its connection. A reply sent to an address nobody listens on is discarded by the broker without an error, and its call has already ended.

**Behavior after a connection loss and at startup:**

- **Every provider backs off before it reconnects.** After a lost channel and after a failed attempt alike, the listener waits a jittered delay of about 1 second, doubling up to 30 seconds, and starts over at about 1 second once a channel opens. The wait runs on the system clock, not the registered `TimeProvider`, so a host that registers a `FakeTimeProvider` (a test host) still reconnects in real time. A broker that keeps accepting and then dropping the listener cannot drive a tight reconnect loop, and a fleet that loses the broker at once does not reconnect in lockstep. Every provider logs the same three events, under its reply transport's logger category with the broker named in `{Transport}`: `ReplyListenerReady` (EventId 4117, debug) when a channel opens, with its `{ReplyAddress}`; `ReplyListenerLost` (EventId 4118, warning) when an open channel ends, with the `{Reason}` and the `{RetryDelay}`; and `ReplyListenerFailed` (EventId 4119, error) when opening or serving a channel throws, with the exception and the `{RetryDelay}`.
- **RabbitMQ.** The listener's connection does not recover on its own. After the connection, channel, or consumer is lost, the listener opens a new connection and declares a new queue under a **new** address; calls sent with the old address time out. The listener connects in the background, so a broker that is unreachable at startup, or wrong credentials, never fails bootstrap: each call waits for the address inside its own timeout and then throws `RequestNotSentException`.
- **NATS.** The address stays the same across reconnects, because the client re-subscribes. While the connection is down the listener withholds its address, so new calls wait inside their timeout; a call already waiting completes if its reply arrives after the reconnect, and a reply published during the outage is lost and its call times out. Subscribing runs in the background, so an unreachable server never fails bootstrap; calls throw `RequestNotSentException` when no address arrives within their timeout. A reply that arrives while the subscription's pending channel (`NatsOpts.SubPendingChannelCapacity`) is full is dropped with a warning, and its call times out.
- **Redis.** The address stays the same across reconnects, because the multiplexer re-subscribes; while the subscription connection is down the address is withheld and replies published then are lost, as on NATS. Connecting and subscribing run in the background with a log entry per retry, so bootstrap never fails; calls throw `RequestNotSentException` when no address arrives within their timeout.

**Operator rules:**

- **NATS streams.** Keep JetStream streams on the lane prefixes (`headless.bus.>`, `headless.queue.>`). A stream whose subjects cover `headless.reply.>`, such as `headless.>` or `>`, stores every reply. When a caller's reply listener subscribes, it asks JetStream which streams capture its reply subject and logs one warning naming them (EventId 13, `NatsReplySubjectCapturedByStreams`); the check is best effort, runs after the address is handed out so it never delays a call, and a server without JetStream or a slow API only produces a debug entry (EventId 14, `NatsReplySubjectStreamCheckSkipped`). A check that finds no capturing stream logs a debug entry too (EventId 15, `NatsReplySubjectNotCapturedByStreams`). A request whose subject no stream captures fails immediately with `RequestNotSentException`, whose `InnerException` is the `PublisherSentFailedException`, instead of timing out. That happens only with `StreamProvisioning` set to `Disabled` and no operator stream for the request's message: under `Verify` or `Reconcile` the caller provisions the request's stream before the first send, so a request nobody answers waits out its timeout.
- **Redis channel prefix.** Every host that exchanges requests must use the same StackExchange.Redis `ConfigurationOptions.ChannelPrefix`. Pub/sub ignores the database number, so hosts that use different databases of one server share the reply namespace. Redis lane names are stream keys and reply addresses are channels, so they never collide.
- **Redis Cluster.** Replies use classic `PUBLISH`, which the cluster forwards to every node: a reply reaches its caller whichever node either side uses, and each reply crosses the cluster bus once per node. Sharded pub/sub is not used. This path has no conformance run against a real cluster.
- **RabbitMQ.** A reply is published without the mandatory flag, so a reply to a queue that is gone is discarded silently.

**Broker permissions.** Reply confidentiality and integrity depend on the broker's access control over the reply namespace. A principal that can publish to a reply address can forge a reply: the caller accepts one only when its `headless-in-reply-to` matches a pending call, its tenant matches, and its response contract matches, but request ids travel in request headers that any reader of the request queue sees. A principal that can subscribe to the reply namespace can read replies. Grant callers and responders these permissions in addition to their lane permissions:

| Provider | Caller (sends requests) | Responder (sends replies) | Notes |
| --- | --- | --- | --- |
| RabbitMQ | `configure` and `read` on queues matching `^headless\.reply\.` | `write` on the default exchange (`amq.default`) | The exclusive queue refuses consumers on other connections. `write` on the default exchange reaches any queue by name, so the broker cannot confine a responder to reply queues; the framework sends only to the reply namespace |
| NATS | `subscribe` on `headless.reply.>` | `publish` on `headless.reply.>` | Deny `subscribe` on `headless.reply.>` to every other principal; a subscriber there sees every caller's replies |
| Redis | `+subscribe` and `+unsubscribe` on channels `&headless.reply.*` | `+publish` on channels `&headless.reply.*` | Include the `ChannelPrefix` in the channel pattern. Grant `subscribe` only to callers |

## Headless.Messaging

### API and behavior

- `PublishReceipt` carries the resolved wire `MessageId` and nullable durable `StorageId`. Direct delivery returns no storage handle. Middleware suppression before terminal publication returns both values null. A receipt enlisted in the caller's active unit of work remains subject to that unit's completion or rollback and never implies consumer completion.
- `services.AddHeadlessMessaging(setup => ...)`, called once by the host. `MessagingSetupBuilder` configures transport, storage, `Options`, `Instrumentation`, conventions, and host controls: `AddModule<TModule>()`, `Tune(identity, ...)`, `ConsumeOnly(...)`, `DefaultFailurePolicy<TPolicy>()` or `DefaultFailurePolicy(p => ...)` (the failure policy of every competing consumer and runtime subscription that has none of its own), and `WithMessageNameMapping<T>(name)`. It returns the `MessagingBuilder` that registers middleware.
- `services.ConfigureMessaging(m => ...)`: a module's contribution through `MessagingContributionBuilder`, before or after `AddHeadlessMessaging`: `AddModule<TModule>()`, `Message<T>(name, version)`, and `Tune(identity, ...)`. The callback runs once, synchronously; contracts are recorded when it returns, so a contract builder used after that throws.
- `IMessagingModule`: the generated `<AssemblyName>.MessagingModule` of an assembly that declares `[BusConsumer]` or `[QueueConsumer]` classes. See [Headless.Messaging.SourceGenerator](#headlessmessagingsourcegenerator).
- Message contracts: `Message<T>(name, version = "1")` returns `IMessageContractBuilder<T>` with `CorrelateBy(selector)`, `OnBus(Action<IBusContractBuilder<T>>)`, and `OnQueue(Action<IQueueContractBuilder<T>>)`. Both lane builders expose `RequireRoutingAffinity()` and `WithDeliveryMode(mode)`; provider packages add their message hatches to them. The name and version are stamped on every outgoing envelope and checked before durable dispatch.
- Consumer tuning: `Tune(identity, Action<ConsumerTuningBuilder>)` with `Concurrency(byte)` (greater than zero), `InboxRetention(TimeSpan)` (positive whole seconds), `CircuitBreaker(Action<ConsumerCircuitBreakerOptions>)`, `FailurePolicy<TPolicy>()` or `FailurePolicy(Action<FailurePolicyBuilder>)`, `UseMiddleware<TMiddleware>()`, and the provider consumer hatches. Tuning cannot declare a consumer or change its identity, lane, or messages.
- Consumer identity: validated at build time by the generator (HM001) and again at registration against `ConsumerMetadata.ConsumerIdentityMaxLength` (200), matching relational inbox admission. Bus and Queue identities are collision-scoped independently.
- `IRuntimeSubscriber.SubscribeAsync<T>(handler, options)` attaches a delegate to the Bus lane after startup. `RuntimeSubscriptionOptions` carries `Identity` (the consumer identity and Bus subscription name, derived from `HandlerId` when omitted), `HandlerId` (defaults to `{DeclaringType}|{Method}|{Message}`, required for anonymous delegates), `MessageName`, `Concurrency`, `EveryInstance`, and `DuplicateBehavior` (`Reject` by default). The returned `RuntimeSubscriptionHandle` exposes `Identity`, `HandlerId`, `MessageName`, and `SubscriptionId`.
- `setup.AddRequestReply(Action<RequestReplyOptions>? configure = null)` registers `IRequestClient` and the host's reply listener; `MessagingOptions.RequestReply` holds `DefaultTimeout`, `IncludeExceptionDetailsInFaults`, and `MaxPendingRequests`. Responder registration, deadlines, inline-only request retries, and reply delivery live here too. See [Request/reply](#requestreply).
- Publish, receive, and consume middleware.
- Strict publish tenancy via `RequireTenantOnPublish()`.
- Storage-backed retry/outbox and cleanup processors.
- Singleton `IMessageRevoker` delegates to optional `IMessageRevocationStorage`. Unsupported providers throw `NotSupportedException` naming the provider.
- `IDataStorage.GetScheduledDeliveryOperationsApi()` (`IScheduledDeliveryOperationsApi`) is the audited, provider-neutral operator surface for pending scheduled deliveries: `QueryAsync` lists them (system-scoped, no tenant filter, up to 200 rows a page), `RevokeAsync` deletes one using the same eligibility predicate and fence as `IMessageRevoker`, and `DispatchNowAsync` advances a pending row's due instant to the provider clock unless a dispatch lease is live. Every mutation carries a client-minted operation id, the storage id, and the caller's expected due instant; a request whose due instant no longer matches the row is rejected as `StateConflict`, and a repeated operation id with a different request is `OperationConflict`. `Applied`, `NotFound`, `StateConflict`, `Active` (an attempt was reserved or the row is leased), and `OperationConflict` reuse the inbox outcome vocabulary. Providers without the capability throw a provider-naming `NotSupportedException`, matching `IMessageRevoker`.
- Optional `IDelayedMessageClaimStorage` SPI for providers that can atomically claim, lease, and transition a bounded delayed-message batch before Core enqueues committed winners.
- Optional `IGracefulLeaseReleaseStorage` SPI for providers that can exact-release completed or pre-execution-abandoned retry leases during bounded shutdown.
- Internal `ICircuitRetryDeferralStorage` capability lets built-in storage atomically move circuit-open received retries to the circuit's next eligible probe time, passed as the time remaining and added to the store's clock, while releasing only the exact claimed lease generation; providers without it retain the claim until normal expiry.
- Circuit breaker monitor/control APIs.
- Host-cancellable consumer factory creation, metadata provisioning, and subscription.
- Monitoring pagination uses zero-based `MessageQuery.CurrentPage` values, returns that value as `IndexPage.Index`, and normalizes negative values to zero.
- Direct publishing bypasses storage entirely, while delayed delivery is always durable. `IBus`/`IQueue` are registered as singletons here (`TryAddSingleton`) and never consult a unit of work; `AddHeadlessMessaging` also registers the singleton `IUnitOfWorkOutbox` feature behind `unit.Outbox`.
- An enlisted publish into an observed-mode unit (the EF save pipeline's own save) calls `IUnitOfWork.PreventRetry()` before the write, so that save is not replayed by an EF execution strategy and a fresh unit of work is required after a transaction failure; a publish issued directly into an owned unit (`BeginAsync`, `RunAsync`) leaves replay intact, while a caller-owned save that dispatched events marks the owning block itself. The EF integration-event bridge exempts captured occurrences that its save pipeline retains for replay.

### Design constraints

Core owns logical metadata and provider-independent correctness. Provider packages own broker-specific values and limits. `CorrelateBy(...)` is a universal logical knob on the contract; partition keys, subject shards, and message group ids are provider hatches on a lane builder because their semantics differ per broker and per lane.

Registration is declarative and frozen. Consumers come only from generated modules, contracts only from `Message<T>`, and host controls only from `Tune`, configuration, and `ConsumeOnly`. Startup merges every contribution into one immutable registry per host, and consumer dispatch reads only that registry. Conflicts that one assembly can see fail the build; conflicts across modules fail startup and name both sources.

Immutable provider descriptors are the authority for transport, storage, coordination, lane, delayed-scheduling, and independent-topology support. Bootstrap freezes and validates them before provider resolution or readiness, and per-call gates run before middleware or side effects. Physical lane separation remains provider-owned and is cross-checked against executable conformance evidence.

Storage providers may implement `IDelayedMessageClaimStorage` in addition to `IDataStorage`. Core detects the capability at runtime and uses its committed winner set; providers without it retain the callback-based `ScheduleMessagesOfDelayedAsync(...)` path. Capability implementations must stamp each winner's `LockedUntil` as `max(authoritative store now, ExpiresAt) + DispatchTimeout`, using the same store-clock snapshot that tests lease eligibility, and return only after commit. Extending a future message's lease from its schedule time keeps the ownership grant alive until the first dispatch attempt, while the commit boundary prevents Core's queue-only enqueue from publishing uncommitted state.

Storage providers may also implement `IGracefulLeaseReleaseStorage`. Core detects it opportunistically; providers without it retain normal `LockedUntil` expiry recovery. Implementations must atomically match the complete store-returned `(StorageId, lane, Owner, LockedUntil)` generation, refuse terminal rows, and clear only `Owner` and `LockedUntil`. The collection methods should batch these exact predicates within backend parameter limits so abandoned retry batches do not become one command per row.

The four `IDataStorage` state-transition methods — `ChangePublishStateAsync`, `ChangePublishRetryStateAsync`, `ChangeReceiveStateAsync`, `ChangeReceiveRetryStateAsync` — take a `MessageContentWrite` declaring whether the transition also rewrites the persisted envelope, and a `RetryDelay?` for when the row falls due again. `RetryDelay.Exactly(delay)` makes it due `delay` after the store's clock; `RetryDelay.AtLeast(delay)` does the same but keeps a later due time already on the row, which the inline-retry crash-recovery schedule uses so it never lowers the initial dispatch grace; `null` clears the due time. A provider whose clock lives in process resolves the instant with `RetryDelay.ResolveDueAt(now, currentDue)`; a relational provider computes it in the update statement. `MessageContentWrite.Preserve`, the default on the two non-retry methods, skips re-serializing `MediumMessage.Origin` and omits the content column from the update, because a status transition does not change the envelope. `MessageContentWrite.Refresh` re-serializes `Origin`, writes it to the row, and refreshes `MediumMessage.Content` so the caller's copy keeps matching the row. A caller that mutated `Origin` before the write — the failure paths, which stamp the exception type onto the headers — must pass `Refresh`, or the mutation never reaches storage. Implementors owe the invariant `persisted Content == Serialize(Origin)` in both directions: `Preserve` must leave the stored envelope byte-identical even when the caller's copy has since drifted, and a provider that keeps the envelope as anything other than serialized bytes must update every representation of it on `Refresh`.

`IConsumerClientFactory.CreateAsync(ConsumerClientRequest, ...)` receives a request whose `SubscriptionName` is the consumer identity on the Bus lane and the message name on the Queue lane, with its `Concurrency`, `Lane`, subscription `Kind` (`Competing` or `EveryInstance`), and the host's `InstanceId`. See [Every-instance Bus delivery](#every-instance-bus-delivery) for what a transport owes an every-instance request. Transports do not stamp the consumer on received envelopes; Core stamps `headless-msg-consumer-identity` once it routes the delivery. The public consumer startup contracts accept trailing optional cancellation tokens: `IConsumerClientFactory.CreateAsync(...)`, `IConsumerClient.FetchMessageNamesAsync(...)`, and `IConsumerClient.SubscribeAsync(...)`. Core passes the host-stopping token to metadata startup and a linked token per consumer client to worker creation and subscription. Implementations must let `OperationCanceledException` escape unchanged.

The blessed cross-package SPI (the contracts that storage providers, transports, and dashboards resolve or implement) lives in the public `Headless.Messaging` namespace: `IProcessingServer` (implement to attach a long-running unit to the bootstrap sequence) and `IConsumerServiceSelector` / `MethodMatcherCache` (inspect the resolved consumer topology). The `TransportNaming` (`WildcardToRegex`, `Normalize`) and `RuntimeTypeInspection` (`IsComplexType`, `DeclaresFieldOfType`) helpers in the same namespace are `internal` and shared with the first-party transports via `InternalsVisibleTo` — they are not part of the NuGet contract. These types were previously exposed under `Headless.Messaging.Internal`; that namespace now holds only genuine implementation detail. The monitoring status is a typed enum — `StatusName` (in `Headless.Messaging`, next to `MessageView`/`MessageQuery`) — so `MessageView.StatusName` and the `MessageQuery.StatusName` filter are compile-time safe. Storage providers persist and compare the enum member names verbatim as strings, so the SQL column contract is unchanged, and the dashboard serializes the status by name to keep the SPA wire shape stable.

### Install

```bash
dotnet add package Headless.Messaging
dotnet add package Headless.Messaging.InMemory
dotnet add package Headless.Messaging.Storage.InMemory
```

### Setup and use

```csharp
[BusConsumer("orders.projection")]
public sealed class OrderPlacedConsumer : IConsume<OrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

[QueueConsumer("orders.fulfil-order")]
public sealed class FulfilOrder : IConsume<FulfilOrderCommand>
{
    public ValueTask ConsumeAsync(ConsumeContext<FulfilOrderCommand> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}

services.AddHeadlessMessaging(setup =>
{
    setup.UseInMemory();
    setup.UseInMemoryStorage();
    setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
    setup.AddModule<Orders.MessagingModule>();
});

services.ConfigureMessaging(messaging =>
{
    messaging.Message<OrderPlaced>("orders.placed").CorrelateBy(order => order.OrderId.ToString());
    messaging.Message<FulfilOrderCommand>("orders.fulfil");
});
```

`IBus.PublishAsync(new OrderPlaced(...))` reaches `OrderPlacedConsumer`, and `IQueue.EnqueueAsync(new FulfilOrderCommand(...))` reaches `FulfilOrder`. Hosts that share modules but split consumption add the same modules to each host and filter with `ConsumeOnly`:

```csharp
services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage ...
    setup.ConsumeOnly("orders.projection"); // every-instance consumers still run
});
```

### Configuration

Configure tenant propagation and strict publishing through `AddHeadlessTenancy(tenancy => tenancy.Messaging(...))`. `MessagingOptions.TenantContextRequired` reports the configured requirement and has no public setter.

`RequireRoutingAffinity()` on a Bus or Queue message registration requires a locally supported native mapping at startup; it does not require every publication to supply a key. Set `PublishOptions.RoutingAffinityKey` or `QueueOptions.RoutingAffinityKey` per publication. The frozen capability model snapshots registered destinations from inert options before clients or processors start. Keyed unknown destination overrides, invalid keys, and typed/raw conflicts fail before outbox insertion or transport effects. `MediumMessage.RoutingAffinityKey` reads the authoritative serialized envelope; InMemory, PostgreSQL, and SQL Server preserve it without a new storage column.

A missing registration defers an inbox generation as an orphan without consuming the handler failure retry budget. Recovery requires the exact consumer identity, logical contract name, contract version, and lane. The probe claims a fresh attempt in the same generation and incarnation, then clears the orphan flag under the complete execution fence before dispatch. Registration absence on one host does not establish absence on every deployment. A host started with `ConsumeOnly` never classifies a row outside its filter: its retry and orphan pickups skip consumer identities it does not consume, so only a host that runs a consumer, or an unfiltered host, can defer that consumer's rows as orphans.

Known orphans are excluded from ordinary retry pickup. Each lane has an independent probe allowance, configured through `setup.Options.OrphanProbeInterval` (default five minutes, positive) and `OrphanProbeBatchSize` (default 10, range 1 through 100,000). The interval delays the next probe after missing-registration deferral; it is not a recovery deadline. First discovery can occupy ordinary retry capacity once, so a growing backlog of unclassified work has no absolute latency guarantee.

Orphans have no automatic expiry or terminalization. An orphan with no live execution claim permits `Hold`, `ReleaseHold`, and, when unheld, `Purge`, subject to the normal expected-status and incarnation checks. A live claim blocks these operator exceptions. `ForceReprocess` remains terminal-only. Holds block purge and terminal retention cleanup but do not pause execution: a held orphan can recover and keeps its hold after completion. Recovery claims and purge serialize against the same generation; only the winner can proceed.

One generalized ledger backs both inbox and scheduled-delivery operator actions: the receipt and audit tables carry a `TargetKind` discriminator (`Inbox`, default; `ScheduledDelivery`), a nullable incarnation/expected-status pair, and nullable published-row snapshot columns (message name, message id, lane, expected due instant). The schema ships fresh with the generalized ledger shape (greenfield — no prior schema versions exist). The `MessagingOperationType` enum gained `Revoke` and `DispatchNow`, enrolling scheduled-delivery operations under the same retention cutoffs below without a new collector category.

Revoking a scheduled row is the same fenced delete `IMessageRevoker.RevokeAsync` performs, plus a receipt and audit written in the same transaction; the row stays deleted either way, and revocation is never a visible status -- the ledger is the only trace once the row is gone. Dispatch-now moves a pending row's due instant to the provider clock; some node's delayed processor claims it on its next pass, typically within about a minute, so a dashboard-only host with no dispatcher cannot promise that latency. Neither action creates, reschedules, or replays a delivery -- see the `Headless.Messaging` package section for the `IScheduledDeliveryOperationsApi` request/outcome contract, and use Jobs for a keyed, replaceable, or transactional deadline instead.

Operation history has separate retention from inbox generations, now covering both target kinds under the same four windows. Configure these positive minimum residence durations through `setup.Options`:

| Option | Default |
|---|---|
| `InboxCleanupReceiptRetention` | 7 days |
| `InboxCleanupAuditRetention` | 7 days |
| `InboxOperatorReceiptRetention` | 30 days |
| `InboxOperatorAuditRetention` | 90 days |

For example, inside the existing `AddHeadlessMessaging` callback:

```csharp
setup.Options.OrphanProbeInterval = TimeSpan.FromMinutes(2);
setup.Options.OrphanProbeBatchSize = 20;
setup.Options.InboxOperatorReceiptRetention = TimeSpan.FromDays(14);
setup.Options.InboxOperatorAuditRetention = TimeSpan.FromDays(180);
```


Thirty days is the operator-receipt default, not a validation floor. Each record ages from its immutable `CreatedAt`; replay does not refresh receipt age. A receipt remains until its minimum residence time passes and all referencing audits have been deleted, so audit references can extend its lifetime. Matching-request replay and conflicting-request detection remain available while the receipt physically exists. After deletion, reuse of its operation ID is evaluated as a new request against current state. Clients must use unique operation IDs and retry within the configured receipt window.

Deleting audits removes historical evidence but does not release a surviving generation's hold. Holds do not pin history indefinitely. Retention changes apply to existing history using its original timestamps; shortening a duration can make old evidence eligible on the next sweep, and increasing it cannot restore deleted records. Configure longer evidence windows before enabling collection. These options do not change persisted inbox-generation retention.

The collector obtains one fixed provider-clock history cutoff snapshot per invocation. PostgreSQL and SQL Server use database time; InMemory uses its injected `TimeProvider`. Each round visits published messages, received messages, expired audits, and unreferenced expired receipts, with a maximum batch of 1,000 per category and a one-second pause after each nonzero batch. Rounds repeat until all categories return zero, then wait for `CollectorCleaningInterval`. History deletion creates no replacement history. Practical storage bounds depend on collection throughput keeping up with eligible arrivals; the durations are minimum residence times, not deletion deadlines.

- `MessagingOptions.MessageNamePrefix` and `Version` control naming and isolation. Consumer identities are never prefixed. `Version` is validated non-empty and at most 20 characters — the SQL storage providers persist it as a literal into a `VARCHAR(20)`/`nvarchar(20)` column, so an over-long value is rejected at startup instead of failing every outbox insert.
- `MessagingOptions.DefaultDeliveryMode` defaults to `DeliveryMode.Durable` for both lanes. Null per-call modes inherit the contract's per-lane `WithDeliveryMode` policy, then this setting; explicit modes override both. Metadata-only records and fluent callbacks inherit the same way. Invalid global values fail options validation.
- There is no enlistment option. `MessagingOptions.DefaultEnlistment` and the per-type `WithEnlistment(...)` policy were deleted: the receiver decides enlistment, so a host that used `DefaultEnlistment = Required` as a guardrail gets a compile error and should read [Delivery Modes](#delivery-modes) for what replaces it.
- `MessagingOptions.MinimumInboxGuarantee` defaults to `InboxGuarantee.Transactional` and sets the minimum inbox guarantee required by durable consumers. The guarantees are ordered `ProcessLocal` (0) < `Durable` (1) < `Transactional` (2); the configured storage must declare the selected guarantee or a stronger one. Selecting a weaker requirement is an explicit opt-down and does not change the provider's actual guarantees. Undefined values are rejected.
- `MessagingInstrumentationOptions.IncludeTenantIdInMetricTags` defaults to `false`. Enable it only when the metrics backend and tenant population have an explicit cardinality budget; traces retain their separate tenant-tag policy.
- `ConsumerThreadCount`, `SubscriberParallelExecuteThreadCount`, and `SubscriberParallelExecuteBufferFactor` accept 1 through 1,024; the subscriber thread-count × buffer-factor product must not exceed 100,000.
- Consumer retries come from each consumer's failure policy (see [Consumer failure policy](#consumer-failure-policy)). Publish retries, the dispatch lease, and `OnExhausted` live under `RetryPolicy`; polling lives under `RetryProcessor`. `RetryBatchSize` (default 200) and `SchedulerBatchSize` (default 1,000) accept 1 through 100,000. `SchedulerBatchSize` also bounds the in-memory near-term scheduler queue; overflow remains durable as `Delayed` work.
- `UseStorageLock` coordinates retry processors through a messaging-keyed distributed lock provider.
- `DeadNodeReconcileInterval` (default 1 minute, `> 0`) sets the always-on dead-owner recovery reconcile cadence (see [Dead-owner recovery](#dead-owner-recovery)). Independent of `UseStorageLock`.
- `ShutdownTimeout` (default 30 seconds, `> 0`, `<= 5m`) is one end-to-end messaging shutdown bound. Shutdown first quiesces every processor, then concurrently initiates all drains using the remaining portion of one monotonic deadline. Configure the generic host or orchestrator termination grace to exceed this value; an earlier kill intentionally falls back to normal lease-expiry recovery while eventual cleanup remains fault-observed.
- `SubscriptionEstablishedTimeout` (default 30 seconds, `> 0`, `<= 5m`) bounds each `IOnSubscriptionEstablished` call (see [Every-instance Bus delivery](#every-instance-bus-delivery)).
- `RequestReply.DefaultTimeout` (default 30 seconds, `> 0`, `<= 10m`) is the request timeout when a call sets no `RequestOptions.Timeout`. `RequestReply.IncludeExceptionDetailsInFaults` (default `false`) lets this host's responders put the exception type and message in fault replies; it applies whether or not the host calls `AddRequestReply`. `RequestReply.MaxPendingRequests` (default `null`, no limit; `> 0` when set) caps the calls waiting for a reply at once; a call over it throws `RequestNotSentException` without publishing. See [Request/reply](#requestreply).
- Register middleware through `MessagingBuilder.AddBusPublishMiddleware<T>()`, `AddQueuePublishMiddleware<T>()`, `AddReceiveMiddleware<T>()`, `AddBusConsumeMiddleware<T>()`, `AddQueueConsumeMiddleware<T>()`, `AddPublishMiddlewareFor<TMiddleware,TMessage>(lane)`, `AddReceiveMiddlewareFor<TMiddleware,TMessage>(lane)`, and `AddConsumeMiddlewareFor<TMiddleware,TMessage>(lane)`. Middleware for one consumer attaches through `Tune(identity, c => c.UseMiddleware<T>())`.
- Consumer deployment settings bind from `Headless:Messaging:Consumers:{identity}` after every `Tune` call: `Concurrency` (1 to 255), `InboxRetention` (a `TimeSpan` such as `30.00:00:00`), and `CircuitBreaker:Enabled`, `CircuitBreaker:FailureThreshold`, `CircuitBreaker:OpenDuration`, and `FailurePolicy:ImmediateRetries`, `FailurePolicy:DelayedRetries`, `FailurePolicy:DelayedInitialDelay`, `FailurePolicy:DelayedMaxDelay`. The transient-exception predicate and fail rules are code-only. An unknown identity, an unknown setting, an invalid value, or an inbox retention, circuit breaker, or failure policy on an every-instance consumer fails startup.

  ```json
  {
    "Headless": {
      "Messaging": {
        "Consumers": {
          "orders.projection": {
            "Concurrency": 8,
            "InboxRetention": "14.00:00:00",
            "CircuitBreaker": { "FailureThreshold": 3, "OpenDuration": "00:01:00" },
            "FailurePolicy": { "DelayedRetries": 8, "DelayedMaxDelay": "00:30:00" }
          }
        }
      }
    }
  }
  ```
- Runtime subscriptions attach handlers after startup through `IRuntimeSubscriber`. On a running host, `SubscribeAsync` and `UnsubscribeAsync` apply the change before they return. While the host is starting or rebuilding its clients after a broker failure, they return at once and the change applies when that finishes. If the changed subscription's old clients do not stop within the rebuild budget, or the broker is unreachable, the call still returns, the host reports itself unhealthy, and the transport health check's full rebuild applies the change. Only the subscription groups (subscription name, lane, and competing or every-instance kind) whose consumers or concurrency changed are stopped or started; every other group keeps its clients.

### Runtime behavior

Registers messaging services, hosted processors, publishers, consumers, storage abstractions, runtime registries, middleware registries, keyed messaging lock defaults, and the always-on `DeadOwnerRecoveryBridge<MessagingDeadOwnerReclaimer>` hosted service.

## Retry Policy

Messaging retries in two directions with separate settings. A failed consume attempt follows the consumer's failure policy, described in [Consumer failure policy](#consumer-failure-policy). A failed publish follows `MessagingOptions.RetryPolicy.RetryStrategy` and `MaxPersistedRetries`, described in [Publish retries](#publish-retries). Both share the dispatch lease, the storage clock rules, and the `RetryPolicy.OnExhausted` callback.

### Consumer failure policy

A competing consumer names a `FailurePolicy` type on its attribute. The policy model, its delay math, and how it compares with Jobs are in [reliability.md](reliability.md).

```csharp
using Headless.Reliability;

public sealed class PaymentsFailurePolicy : FailurePolicy
{
    protected override void Configure(FailurePolicyBuilder policy) =>
        policy
            .Immediate(retries: 1)
            .Delayed(retries: 6, initialDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromMinutes(30))
            .FailOn<PaymentDeclinedException>();
}

public sealed class PaymentDeclinedException(string message) : Exception(message);

[QueueConsumer("payments.charge-order", FailurePolicy = typeof(PaymentsFailurePolicy))]
public sealed class ChargeOrder : IConsume<PlaceOrder>
{
    public ValueTask ConsumeAsync(ConsumeContext<PlaceOrder> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
```

The host sets the default for consumers that declare nothing, and replaces one consumer's policy by identity:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage ...
    setup.DefaultFailurePolicy(policy =>
        policy.Immediate(1).Delayed(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10))
    );
    setup.Tune(
        "orders.projection",
        consumer => consumer.FailurePolicy(policy => policy.Delayed(10, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1)))
    );
});
```

**Resolution.** `Tune` replaces the declared policy; a consumer with neither gets the host's `DefaultFailurePolicy`, and without that the framework default: 2 immediate retries, then 5 delayed retries from 30 seconds capped at 15 minutes, with no fail rules. `Headless:Messaging:Consumers:{identity}:FailurePolicy` then overrides `ImmediateRetries`, `DelayedRetries`, `DelayedInitialDelay`, or `DelayedMaxDelay` of the winner and keeps its fail rules. A publisher cannot choose a consumer's policy. One identity has one policy: a class that consumes several messages applies it to all of them, and two modules that declare one identity with different policy types fail startup naming both. A competing runtime subscription (`IRuntimeSubscriber`) uses the host default.

**Attempts.** A failing message gets at most `1 + ImmediateRetries + DelayedRetries` attempts:

- The first dispatch runs the first attempt and then the immediate retries back-to-back, with no delay.
- Each delayed retry is one later dispatch with one attempt. The row is stored with `NextRetryAt` set to the database clock plus the policy's jittered delay for that retry, and the retry processor picks it up once it falls due. The processor polls every `RetryProcessor.BaseInterval` (adaptive), so a delayed retry can run up to one poll interval after its `NextRetryAt`. A delay shorter than the poll interval is not honored precisely.
- Received-row pickup has no global retry-count cap: each row is judged against its own consumer's policy after it is claimed. A row whose stored retry count already exceeds the consumer's budget, because the policy shrank since the row was scheduled, ends as a spent budget without running the handler. A dispatch that crashed after reserving its final attempt ends the same way, so a crash loop cannot exceed the budget.

With the framework default, a message that keeps failing runs 3 attempts at once, then 5 more after about 30 s, 60 s, 120 s, 240 s, and 480 s (each ±20% and each later by up to one poll interval), then fails terminally: 8 attempts.

**Classification.** Fail rules see the handler's own exception, unwrapped from the executor's `SubscriberExecutionFailedException`. A failure ends at once, without its remaining retries, when a fail rule matches or the exception is in the built-in permanent set: `ArgumentException` and its subtypes, `NotSupportedException`, and `SubscriberNotFoundException`. A `FailWhen` predicate that throws counts as a match and is logged (EventId 111, `FailurePolicyRuleThrew`). Every other exception is retried. A cancellation of the consume token, such as host shutdown, is not a failure: once that token is cancelled, any `OperationCanceledException` that ends the attempt writes nothing, whatever token it carries, and the row is picked up again later. An `OperationCanceledException` raised while the consume token is live is an ordinary failure: fail rules see it and the policy retries it. That covers one the handler throws, such as an `HttpClient` timeout or a handler-owned `CancelAfter`, and one the transactional inbox's commit work raises after the handler, such as a consumer `SaveChanges` interceptor's `HttpClient` timeout; the latter reaches fail rules and `OnExhausted` unwrapped. To end such a cancellation at once, declare `FailOn<OperationCanceledException>()`, which also matches `TaskCanceledException`.

**Terminal failure.** A message the policy gives up on is stored as `Failed` with no `NextRetryAt`; there is no separate dead-letter status. It stays until its retention expires, and an operator re-executes it from the Messaging dashboard (`POST /api/received/reexecute`); a re-executed request runs as a plain Queue message and answers no caller (see [Deadlines](#deadlines-timeouts-and-clock-skew)). `RetryPolicy.OnExhausted` fires once for each terminal consume failure, and only on the node whose terminal write wins:

- the policy's budget is spent;
- a fail rule or the built-in permanent set matches;
- the payload fails to deserialize at execution;
- the stored message's consumer is no longer registered;
- the message is poisoned on arrival (rejected before any consume attempt; the callback gets no storage id).

An every-instance consumer has no failure policy and never calls `OnExhausted`; see [Every-instance Bus delivery](#every-instance-bus-delivery).

### Publish retries

`RetryPolicy.RetryStrategy` and `RetryPolicy.MaxPersistedRetries` govern publishing only. `RetryStrategy.MaxRetryAttempts` excludes the original execution and controls inline publish retries through a reusable Polly `ResiliencePipeline`. Once the inline budget is exhausted, Messaging persists `NextRetryAt` and `MessageNeedToRetryProcessor` performs up to `MaxPersistedRetries` pickups, which the published-row claim enforces. `InlineAttempts` is reserved atomically before each invocation, so process recovery cannot reset the current burst. A permanent publish failure ends without retry and without `OnExhausted`; only a spent publish budget invokes it.

```csharp
using Polly;
using Polly.Retry;

builder.Services.AddHeadlessMessaging(setup =>
{
    setup.Options.RetryPolicy.RetryStrategy = new RetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        MaxDelay = TimeSpan.FromMinutes(5),
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Exception is TimeoutException or HttpRequestException
        ),
    };
});
```

The framework's default publish classification (retry anything that is not a cancellation and not classified permanent) is exposed as `RetryPolicyOptions.DefaultShouldHandle`. Reuse or compose it when replacing `RetryStrategy`, so a custom strategy does not silently drop the built-in classification.

Worked publish example with `RetryStrategy.MaxRetryAttempts = 2, MaxPersistedRetries = 2`: total (2+1)×(2+1) = 9 attempts. With the defaults (2 and 15) a publish gets 48 attempts.

```
pickup 1 (initial dispatch):
  attempt 1 (original)        ── inline
  attempt 2 (inline retry #1) ── inline, after Polly delay
  attempt 3 (inline retry #2) ── inline, after Polly delay → persist (1/2)
pickup 2 (persisted retry #1):
  attempt 4                   ── inline
  attempt 5 (inline retry #1) ── inline, after Polly delay
  attempt 6 (inline retry #2) ── inline, after Polly delay → persist (2/2)
pickup 3 (persisted retry #2):
  attempt 7                   ── inline
  attempt 8 (inline retry #1) ── inline, after Polly delay
  attempt 9 (inline retry #2) ── final; on failure → Exhausted → OnExhausted fires
```

### Storage clock and leases

On PostgreSQL and SQL Server, the database clock decides due time as well as lease ownership: retry pickup compares `NextRetryAt`, and the delayed claim compares each scheduled `ExpiresAt`, against the database clock, and the store stamps `Added`, the initial-dispatch-grace `NextRetryAt`, the orphan-probe `NextRetryAt`, and failed-message `ExpiresAt` from that clock too. Core never hands the store an application-clock due time either: a retry transition passes a `RetryDelay`, and a circuit-open deferral passes the time left until the circuit's next probe, and the store adds that delay to its own clock in the same statement that writes the row, so a retry falls due after its configured delay by the database clock however far the application clock is skewed. A scheduled publish time is a genuine instant the caller chose, so it is stored as given and compared against the database clock. Retention stays on the application clock: a terminal row's `ExpiresAt` and the collector's cutoff that purges it are both the caller's, so they cannot drift from each other. InMemoryStorage uses its injected `TimeProvider` for all of these. The public `IDataStorage` SPI accepts a `DispatchTimeout` duration; PostgreSQL and SQL Server compare and stamp leases from one database-clock snapshot, read only after the rows the decision is about are locked, and InMemoryStorage uses its injected `TimeProvider`. A lease, a retry due time, a reserved attempt, or an operator action that waited for another transaction's row lock therefore decides on the clock as it stood when the lock was granted, never on a reading from before the wait: a transition of one row takes the row's lock before the statement that reads the clock, dead-owner reclaim locks the dead owners' rows before it compares their leases, and the claims and retention selections skip locked rows instead of waiting. A successful call returns the persisted `(LockedUntil, Owner)` identity on the message for fenced attempt and state writes. This eliminates client-clock skew from relational ownership, not duplicate delivery: genuine `DispatchTimeout` expiry permits a successor, and a process paused beyond its lease can resume already-running work alongside it. Delivery remains at-least-once.

Inbox lease release and retry deferral also require the complete stored inbox attempt fence, including generation, incarnation, and attempt ID. Missing or mismatched fences leave the row unchanged. Admission uses the same identity validation as PostgreSQL and SQL Server: nonblank contract names, consumer identities, and message IDs up to 200 characters; contract versions up to 100 characters; nonnegative generations; and nonblank tenant IDs up to 200 characters. Blank tenant headers normalize to no tenant.

Inline inbox reservations verify the complete active attempt fence before advancing the counter and preserve its attempt ID within the lease. Acquiring a fresh lease or claiming a due retry allocates a new attempt ID.

For circuit-open received retries, built-in storage providers atomically advance `NextRetryAt` to the circuit's authoritative next eligible probe time while clearing only the exact live `(row, lane, Owner, LockedUntil)` lease generation. A stale generation is a no-op. Providers without the internal capability retain the claim for ordinary lease-expiry recovery instead of clearing the lease without advancing the schedule.

During graceful shutdown, retry pickup is frozen before draining. A claimed lease is released early only when its exact `(row, lane, Owner, LockedUntil)` generation is locally known to have completed or to have been abandoned before execution. A handler still running at the shutdown deadline keeps its lease, and a crash performs no graceful release; both remain governed by normal `LockedUntil` expiry. This makes rolling restarts reclaim safe abandoned work promptly without turning lease release into an exactly-once claim. When `DispatchTimeout - InitialDispatchGrace` is greater than two minutes, startup emits EventId 97 so operators can measure valid handler duration and explicitly align the settings instead of blindly shortening the global lease.

### FailedInfo construction (for tests / fakes)

`FailedInfo` has seven required-init properties — `Lane`, `Exception`, `StorageId`, and `RetryCount` are part of the contract:

```csharp
var info = new FailedInfo
{
    ServiceProvider = scope.ServiceProvider, // live dispatch scope, NOT the root provider
    MessageType = MessageType.Subscribe, // or MessageType.Publish
    Message = mediumMessage.Origin, // the message envelope, not the payload
    Lane = MessageLane.Bus, // or MessageLane.Queue
    Exception = ex, // the exhausting exception
    StorageId = mediumMessage.StorageId, // storage row identifier for DLQ correlation
    RetryCount = mediumMessage.Retries, // final persisted-retry count
};
```

`ServiceProvider` is the live outer dispatch scope. Transactional consume attempts own separate scopes that end after commit or rollback; services resolved through `FailedInfo.ServiceProvider` must not be assumed to be the handler's instances or to participate in its completed transaction.

### RetryProcessorOptions

`MessagingOptions.RetryProcessor` controls the persisted-retry processor's polling cadence:

| Property | Default | Notes |
| --- | --- | --- |
| `BaseInterval` | `60s` | Base polling interval. Replaces the old `FailedRetryInterval`. |
| `AdaptivePolling` | `true` | When enabled, polling interval halves on healthy cycles and doubles when circuit-open skip rate exceeds threshold. |
| `MaxPollingInterval` | `15m` | Cap on adaptive doubling. |
| `CircuitOpenRateThreshold` | `0.8` | Above this fraction of circuit-open skips, the processor backs off. |

### Migration from pre-RetryPolicy primitives

| Old property | New property | Notes |
| --- | --- | --- |
| `FailedRetryCount` | `RetryPolicy.MaxPersistedRetries` (publish); the consumer's failure policy (consume) | Controls persisted publish pickups. Total publish attempts = `(RetryStrategy.MaxRetryAttempts + 1) × (MaxPersistedRetries + 1)`. Consume attempts = `1 + ImmediateRetries + DelayedRetries` of the consumer's policy. |
| `FailedRetryInterval` | `RetryProcessorOptions.BaseInterval` | Default `60s`. |
| `FallbackWindowLookbackSeconds` | *removed* | No replacement — `MessageNeedToRetryProcessor` now polls without a lookback window. |
| `RetryBackoffStrategy` | `RetryPolicy.RetryStrategy` (publish); `FailurePolicyBuilder.Delayed` (consume) | Publish: configure Polly's `RetryStrategyOptions` directly, including explicit `ShouldHandle`, backoff, jitter, delay generator, cap, and `OnRetry`. Consume: the consumer's failure policy sets the delayed backoff. |
| `FailedThresholdCallback` | `RetryPolicy.OnExhausted` | Fires once per owned terminal transition: a spent publish budget, or any terminal consume failure listed under [Consumer failure policy](#consumer-failure-policy). |

## Distributed Lock Integration

`MessagingOptions.UseStorageLock` (default `false`) enables `IDistributedLock`-backed mutual exclusion in `MessageNeedToRetryProcessor`. When `true`, each Published/Received × Bus/Queue quadrant acquires its own named lock before its pickup. No quadrant's lease, contention, or cadence gates another quadrant.

Use `MessagingBuilder.UseDistributedLock(...)` to wire the provider. Calling this method implicitly sets `UseStorageLock = true`:

```csharp
// Instance overload — when you already have an IDistributedLock
var lockProvider = new MyDistributedLock(/* ... */);
builder.Services.AddHeadlessMessaging(setup => { /* ... */ })
    .UseDistributedLock(lockProvider);

// Factory overload — when the provider depends on other DI services
builder.Services.AddHeadlessMessaging(setup => { /* ... */ })
    .UseDistributedLock(sp => sp.GetRequiredService<IDistributedLock>());
```

Messaging keeps its lock provider under an **internal keyed-DI key** (`"headless.messaging"`) so it never conflicts with any `IDistributedLock` registered at the application level for other purposes.

### What this is and isn't (correctness vs coordination)

- Per-row `LockedUntil` (set to `DispatchTimeout` before each publish/consume attempt — see the [Retry Policy](#retry-policy) section) is the storage concurrency primitive. `NextRetryAt` is scheduling state that PostgreSQL and SQL Server compare against the database clock. They independently compare lease expiry and stamp the next `LockedUntil` from one database-clock snapshot inside the same atomic claim command; no separate clock query is added per polling tick. It reduces concurrent dispatch of the same row and works whether or not the distributed lock is enabled, but delivery is still at-least-once under crash, broker redelivery, and broker-accept/storage-mark races.
- Dead-owner recovery is a separate **always-on acceleration primitive**, independent of this lock (see [Dead-owner recovery](#dead-owner-recovery)). With a real `INodeMembership` a recovery bridge reclaims rows owned by `Dead` incarnations and pulls `LockedUntil` back to now; without Coordination, `Owner` remains `null` and rows recover at the normal `LockedUntil` floor.
- Each distributed lock is a **quadrant-scoped pickup mutex**, not a correctness requirement. It lets one replica scan that direction/lane backlog while the other three quadrants continue independently.
- Disabling `UseStorageLock` does not change the at-least-once delivery contract. It introduces wasted pickup work on contended backlogs. If an acquired retry lock's `LostToken` fires (EventId 79), no new pickup starts under an already-lost lease; any in-flight dispatch remains governed by the per-row `LockedUntil` lease.

### When to enable

- Multi-replica deployment where many retry pickups would otherwise contend. Each tick on each replica scans the same backlog table; the distributed lock makes only one replica do the scan per tick.
- Pickup queries that are expensive (large backlog, complex filter, secondary indexes scanned). Even at two replicas, halving the pickup load is observable.
- Operationally noisy retries without it: lots of "0 messages picked up" log lines from sibling replicas competing for the same backlog.

### When to skip

- Single replica. No contention exists; the lock provider is overhead.
- Storage provider that natively prevents duplicate pickup (e.g., row-level locking under a `SELECT ... FOR UPDATE` pattern). Per-row `LockedUntil` already covers correctness; the distributed lock would only deduplicate the SELECT itself.
- Tolerable duplicate pickup churn. Duplicate *pickup* attempts are not duplicate *delivery*; the per-row `LockedUntil` lease still prevents double-dispatch.

### Requirements

- Call `UseDistributedLock(...)` on the returned `MessagingBuilder` to supply a real provider (e.g. from `Headless.DistributedLocks` + a cache/DB backend).
- Without a real provider, only `NoOpDistributedLock` is active (the keyed-DI fallback). The bootstrapper emits two mutually-exclusive Warnings depending on what it finds: **EventId 77** when no real provider is wired under any registration, and **EventId 78** when a real provider is registered un-keyed (e.g., via `AddHeadlessDistributedLocks(setup => setup.UseRedis())`) but not flowed through `MessagingBuilder.UseDistributedLock(...)`. Alert on either.
- `UseDistributedLock(...)` is **last-wins** — calling it twice replaces the prior registration rather than stacking duplicates.

**NoOp introspection contract:** when `NoOpDistributedLock` is the resolved messaging-keyed provider, the introspection methods (`IsLockedAsync`, `GetLockInfoAsync`, `ListActiveLocksAsync`, `GetActiveLocksCountAsync`) silently return empty/false/null. They cannot be used to verify lock state in that mode; rely on the EventId 77 / 78 warning at startup as the operational signal.

### Lock names

- `messaging.publish-retry-bus-{version}` — Published-Bus pickup.
- `messaging.publish-retry-queue-{version}` — Published-Queue pickup.
- `messaging.receive-retry-bus-{version}` — Received-Bus pickup.
- `messaging.receive-retry-queue-{version}` — Received-Queue pickup.
- `messaging.publish-retry-{lane}-{version}-outbox-{key}` — Published pickup of an [additional outbox](#additional-outboxes), per lane. `{key}` is a 16-hex-digit hash of the outbox's provider, data source, and database, so each database's relay holds its own lease.

Both names follow the literal pattern shown above. They are constructed internally by `Headless.Messaging`; downstream consumers must not depend on the internal helper that builds them — register a real provider exclusively via `MessagingBuilder.UseDistributedLock(...)` and let messaging resolve its own keyed-DI slot.

`{version}` comes from `MessagingOptions.Version` and is the **cross-process isolation key**. Two services that share a single lock store (e.g., both pointed at the same Redis) MUST set distinct `Version` values — otherwise matching quadrants collide on the same resources and starve each other. All retry locks use `acquireTimeout: TimeSpan.Zero` (non-blocking try-once), a finite lease window equal to that quadrant's current polling interval, and `Monitoring = LockMonitoringMode.AutoExtend`; contention skips only that quadrant's pickup cycle.

**When `UseStorageLock = false`** (default): `IDistributedLock` is never called and distributed lock wiring is not required. Dead-owner recovery is unaffected — it runs independently of this lock (see [Dead-owner recovery](#dead-owner-recovery)).

### Dead-owner recovery

Dead-incarnation retry recovery runs **always-on**, independent of `UseStorageLock`. On every messaging host a `DeadOwnerRecoveryBridge` (an `IHostedService` from `Headless.Coordination`, registered unconditionally) drives reclaim from the membership substrate on two triggers:

- a `WatchAsync` loop that reclaims a node's rows on a `NodeLeft` event (low-latency), and
- a periodic liveness-snapshot reconcile every `MessagingOptions.DeadNodeReconcileInterval` (default 1 minute) that reclaims every `Dead` incarnation as the authoritative backstop — a watch-loop failure degrades to reconcile, not to no recovery.

Reclaim is **dead-only**: only owners the snapshot classifies `Dead` are reclaimed. A `Suspected` owner (likely still alive and mid-dispatch) is never reclaimed, so a transient GC pause, thread-pool starvation, or network blip does not trigger duplicate delivery. Reclaim is idempotent — an in-memory dedup set suppresses duplicate work between the watch and reconcile paths, and the owner-scoped conditional `UPDATE` (which only pulls leases still in the future) makes a repeated reclaim, or a peer's concurrent reclaim, a no-op. Reclaim writes use `CancellationToken.None` so a reclaim racing host shutdown is not torn mid-write.

`LockedUntil` remains the correctness floor: a row whose lease has already expired is recovered by normal pickup regardless of the bridge, and an owner that ages out of the snapshot before reclaim still recovers via lease expiry.

Because reclaim acts only on the `Dead` set (never `Suspected`), a transient suspect window — GC pause, thread-pool starvation, brief network blip — no longer triggers reclaim, which is the v1 duplicate-delivery footgun this design removes. One operational invariant still holds, though: set Coordination's dead threshold no lower than the largest retry `DispatchTimeout`. A node starved long enough to miss heartbeats is classified `Dead` even if it is still completing an in-flight dispatch; with `DeadThreshold >= DispatchTimeout` that node's lease has already passed its window by the time it is declared `Dead`, so reclaim's `LockedUntil > now` predicate matches nothing and the floor already owns recovery. Set it lower and reclaim can pull a still-valid lease and re-dispatch a row the original owner is still handling.

With only `NullNodeMembership` (no Coordination provider) the bridge is a benign no-op and recovery falls back to the floor — startup logs EventId 88.

| Configuration | Recovery behavior | Startup signal |
| --- | --- | --- |
| No Coordination membership (`NullNodeMembership`) | Floor-only (`LockedUntil`); the bridge is a benign no-op. | EventId 88 Information |
| Real Coordination membership | Always-on dead-owner reclaim: `NodeLeft` watch + `DeadNodeReconcileInterval` reconcile, dead-only. | None on success; bridge EventIds 1–3 on failure |

`UseStorageLock` is orthogonal to recovery — it only serializes retry *pickup* across replicas, it does not gate reclaim.

### EventIds

| EventId | Name | Severity | Trigger | Remediation |
| --- | --- | --- | --- | --- |
| 77 | `UseStorageLockWithNoOpProvider` | Warning | `UseStorageLock = true` but no real provider is registered under any key. | Wire a real provider via `MessagingBuilder.UseDistributedLock(...)`, or set `UseStorageLock = false`. |
| 78 | `UseStorageLockWithNoOpProviderButRealUnkeyed` | Warning | `UseStorageLock = true`, real provider registered un-keyed, but not flowed through `MessagingBuilder.UseDistributedLock(...)`. | Re-register the provider via `MessagingBuilder.UseDistributedLock(...)` so it lands under messaging's keyed slot. |
| 79 | `RetryLockLeaseLost` | Warning | The acquired published- or received-retry lease's `LostToken` was already canceled before pickup, or fired while pickup was in flight. | Investigate lock-store TTLs, clock skew, and auto-extension health if frequent; per-row `LockedUntil` remains the correctness boundary. |
| 81 | `PublishedRetryLockAcquireFailed` | Warning | `TryAcquireAsync` threw on the published-retry path. | Investigate lock-store health if persistent; the pickup is skipped. |
| 82 | `PublishedRetryLockAcquireFailureEscalated` | Error | Three consecutive published-retry acquire failures. | Investigate lock-store health. Adaptive polling is backing off. After lock-store recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` to restore normal polling immediately. |
| 83 | `ReceivedRetryLockAcquireFailed` | Warning | `TryAcquireAsync` threw on the received-retry path. | Investigate lock-store health if persistent; the pickup is skipped. |
| 84 | `ReceivedRetryLockAcquireFailureEscalated` | Error | Three consecutive received-retry acquire failures. | Investigate lock-store health. Adaptive polling is backing off. After lock-store recovery, call `IRetryProcessorMonitor.ResetBackpressureAsync` to restore normal polling immediately. |
| 88 | `MessagingRecoveryUsingLockedUntilFloorOnly` | Information | Only `NullNodeMembership` is registered, so dead-owner recovery falls back to the `LockedUntil` floor (independent of `UseStorageLock`). | Register a Coordination provider to enable dead-owner reclaim, or accept floor-only recovery. |
| 91 | `MessagingDeadOwnerRowsReclaimed` | Information | The dead-owner reclaimer recovered N orphaned rows (published or received) for a dead owner. | Informational — no action needed. |
| 94 | `MessagingDeadThresholdBelowDispatchTimeout` | Warning | A real Coordination membership is registered but `DeadThreshold` is below the retry `DispatchTimeout`, so a still-alive node crossing the dead threshold mid-dispatch is reclaimed and re-dispatched. | Set Coordination `DeadThreshold` >= the retry `DispatchTimeout`. |
| 97 | `MessagingDispatchTimeoutMateriallyExceedsInitialGrace` | Warning | `DispatchTimeout - InitialDispatchGrace` is strictly greater than two minutes, so a crash or over-budget handler can delay rolling-restart pickup until lease expiry. | Measure the longest valid handler duration, ensure the outer termination grace exceeds `ShutdownTimeout`, then explicitly align the retry timings if restart latency matters. Do not shorten the lease below valid handler duration. |

The always-on `DeadOwnerRecoveryBridge` logs failures under its own category, `Headless.Coordination.DeadOwnerRecoveryBridge<MessagingDeadOwnerReclaimer>` (EventIds restart at 1 within that category):

| EventId | Name | Severity | Trigger | Remediation |
| --- | --- | --- | --- | --- |
| 1 | `MembershipWatchFailed` | Error | The `WatchAsync` event loop failed; recovery falls back to the periodic reconcile. | Investigate Coordination store health if frequent; the reconcile backstop still recovers. |
| 2 | `DeadNodeReconcileFailed` | Error | A reconcile tick failed. | Investigate Coordination/storage health if persistent; retries on the next `DeadNodeReconcileInterval`. |
| 3 | `DeadNodeReclaimFailed` | Error | A single dead owner's reclaim threw; the owner is removed from the dedup set and retried on the next reconcile. | Investigate storage health if persistent. |

### Pros and cons

- **Pros:** less wasted pickup work, cleaner logs at scale, halves backlog scan load per added replica.
- **Cons:** extra lock-store round trip per tick, extra dependency, more EventIds to monitor (79 for lease loss, 81-84 for acquire failures).

---

## Strict Publish Tenancy

`MessagingOptions.TenantContextRequired` is the messaging sibling of the EF write guard (#234) and the HTTP authorization requirement. Defaults to `false` to preserve today's behavior. Enable it with `.Messaging(messaging => messaging.RequireTenantOnPublish())`; the property has no public setter. Every guarded publish must resolve a tenant identifier:

1. `PublishOptions.TenantId` if set (the source of truth — see `Headers.TenantId` integrity rules in [Multi-Tenancy / Message Consumers](multi-tenancy.md#message-consumers)).
2. Otherwise, the ambient `ICurrentTenant.Id`, unless `SuppressAmbientBusinessContext` is enabled.
3. If neither resolves, the publish wrapper throws `Headless.MultiTenancy.MissingTenantContextException`.

The raw-header checks (`ReservedTenantHeader`, `TenantIdMismatch`) still run first, so flipping `TenantContextRequired` cannot bypass them.

Root tenancy setup:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims()) // the ambient tenant that propagation captures
        .Messaging(messaging => messaging.PropagateTenant().RequireTenantOnPublish())
);
```

Configure messaging transport and storage with `AddHeadlessMessaging(...)`. Configure tenant propagation and enforcement only through `AddHeadlessTenancy(...)`.

**Remediation for background workers / `IHostedService` callers (no ambient HTTP scope):**

```csharp
// Option A: pass the tenant explicitly
await publisher.PublishAsync(message, new PublishOptions { TenantId = tenantId }, cancellationToken);

// Option B: scope the AsyncLocal accessor before publishing
using (currentTenant.Change(tenantId))
{
    await publisher.PublishAsync(message, cancellationToken);
}
```

Catch `MissingTenantContextException` directly (it inherits from `Exception`, not `InvalidOperationException`) when a cross-cutting handler needs to map it to an HTTP 4xx or suppress retries.

## Middleware

The pipeline supports cross-cutting middleware across three stages via russian-doll contracts:

- **Publish middleware**: `IPublishMiddleware<TContext>` where `TContext : PublishContext` (typed object before serialization)
- **Receive middleware**: `IReceiveMiddleware` on `ReceiveContext` (raw envelope after subscriber lookup and before contract-version validation and deserialization)
- **Consume middleware**: `IConsumeMiddleware<TContext>` where `TContext : ConsumeContext` (typed object after deserialization and inbox admission)

Middleware receives one context plus `Func<ValueTask> next`. Code before `await next()` runs before the inner ring; code after it runs after a successful inner ring. Use ordinary `try/catch` around `await next()` for compensation, retries, and error policy. Returning without calling `next` short-circuits the pipeline.

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

### Receive Middleware

Receive middleware intercepts the raw transport envelope (`ReceiveContext.Headers`, `ReceiveContext.Body`) before payload deserialization. It runs per delivery with the consumer identity known (`MessageType`, `ConsumerContractVersion`, `MessageName`, `ConsumerIdentity`, `Lane`).

- **Context views & copy-on-write:** `context.Headers` and `context.Body` provide read-only views of the current envelope as subsequent components will see it. Calling `context.SetHeader(key, value)`, `context.RemoveHeader(key)`, or `context.ReplaceBody(bytes)` performs copy-on-write modification. Identity headers (`headless-msg-id`, `headless-msg-name`, `headless-msg-consumer-identity`), `headless-exception`, and `headless-transport-address` cannot be modified.
- **Outcomes:** Middleware controls execution via `next()`, `context.Skip(reason)`, or `context.Reject(reason, cause)`.
- **Outcome ownership:** `ConsumerRegister` owns all transport settlement, storage writes, and circuit-breaker signals:
  - `Accept`: `next()` completes; persists admitted message, commits transport, dispatches to consumer.
  - `Skip`: commits transport, drops message without storage rows or `OnExhausted`.
  - `Reject`: stores received-exception poison row (`data:` URI), commits transport, fires `OnExhausted`. Explicit policy `Reject(reason, cause)` releases the circuit-breaker probe without reporting failure; thrown exceptions or undeclared outcomes report breaker failures.
  - `Cancelled`: bound `OperationCanceledException` requeues delivery without storage rows.
  - `Post-success throw`: exception after `next()` succeeds is logged and suppressed, preserving the accepted delivery.
- **Ordering rule:** byte-exact verification middleware (HMAC/signature) must register with lower `Priority` than any body-transforming middleware (`ReplaceBody`/`SetHeader`), because `ReceiveContext` deliberately exposes only the current transformed envelope.
- **Poison cap:** persisted poison `data:` URIs are bounded by `MessagingOptions.MaxPoisonEnvelopeBytes` (default 1 MB) to prevent storage amplification on oversized rejected payloads.

**Registration scopes:**

- `AddBusPublishMiddleware<T>()` / `AddBusConsumeMiddleware<T>()`: object-typed middleware for every publish or consume on the Bus lane.
- `AddQueuePublishMiddleware<T>()` / `AddQueueConsumeMiddleware<T>()`: the same for the Queue lane. Register a type through both methods to run it on both lanes; it keeps one scoped service registration. A builder registration is lane-scoped: middleware registered for one lane never runs on the other, whether or not the other lane has middleware of its own. Middleware added straight into DI as `IPublishMiddleware<PublishContext>` or `IConsumeMiddleware<ConsumeContext>` without the builder stays lane-agnostic, and a type the builder registered obeys the builder's lanes everywhere.
- `AddReceiveMiddleware<T>()`: global receive middleware running on both lanes for every resolved consumer.
- `AddPublishMiddlewareFor<TMiddleware, TMessage>(lane)`: typed publish middleware for one message type and lane.
- `AddReceiveMiddlewareFor<TMiddleware, TMessage>(lane)`: typed receive middleware for one message type and lane, for every consumer of that message.
- `AddConsumeMiddlewareFor<TMiddleware, TMessage>(lane)`: typed consume middleware for one message type and lane, for every consumer of that message.
- `Tune(identity, c => c.UseMiddleware<TMiddleware>())`: consume middleware for one consumer, on the `AddHeadlessMessaging` setup or `services.ConfigureMessaging(...)`. It implements the untyped `IConsumeMiddleware<ConsumeContext>` because one consumer may handle several messages, runs innermost (inside the global and per-message consume middleware), and is resolved from the delivery scope, registered as scoped when not already registered. Several `Tune` calls accumulate middleware; the same type tuned twice runs once.
- Each `MessagingBuilder` call returns a registration handle with `.WithPriority(int)`. Lower priority runs first and wraps later middleware. Ties use registration order. Default priority is `0`; first-party tenant propagation uses `-1000`.

**Framework guarantees:**

- Post-success middleware failures are logged and suppressed only after the inner handler/publisher completed successfully, avoiding duplicate publish or consume retries.
- `OperationCanceledException` whose token matches `context.CancellationToken` is never silently swallowed, including recursive `AggregateException` cases.
- After middleware returns normally, the pipeline rechecks `context.CancellationToken.IsCancellationRequested` and throws OCE if the current context token is canceled.

**Publish context rules:** Production publish contexts freeze the delivery mode, `Delay`, and `ScheduledAt` before middleware runs. Middleware can change other options before `await next()`; all mutations throw after the inner publisher completes. Reads, including `IsTransactional`, remain valid. `IsTransactional` is true only when the resolved delivery enlists in the caller's active unit of work, whose completion is the caller's responsibility.

For middleware tests and tooling, `new PublishContext<T>(content, lane, options, defaultDeliveryMode, now, isTransactional, isStorageSupported, requireCoordination, cancellationToken)` requires the host default and resolution timestamp explicitly. The constructor uses the canonical delivery resolver with the per-call mode on `options` falling back to `defaultDeliveryMode`, and both scheduling options. `requireCoordination` models the receiver: `true` is the outbox surface, where the mode is `Durable` by construction and both the per-call mode and the host default are ignored; `false` is the autonomous bus or queue. It rejects invalid lanes, modes, and delays, simultaneous scheduling options, Direct with either scheduling option, Direct with `requireCoordination`, and `requireCoordination` while `isTransactional` is `false`. Scheduled contexts calculate `PublishAt` from the relative delay or absolute instant. `isTransactional` models a unit of work the storage can join; `IsTransactional` is true only when the resolved delivery writes into that unit. Manually constructed contexts remain mutable until `MarkCompleted()` and do not own a live unit of work.

Absolute schedules retain the requested instant in UTC as `ScheduledAt`; `PublishAt` floors that instant to microsecond precision, matching runtime publication.

**Cancellation token swaps:** middleware that creates per-attempt or per-operation tokens must call `context.WithCancellationToken(...)` before `await next()`. Downstream middleware must re-read `context.CancellationToken` at each await boundary; do not capture it once at method entry.

### Multi-tenancy

The framework ships built-in middleware that propagates the originating tenant on the wire:

```csharp
builder.AddHeadlessTenancy(tenancy =>
    tenancy
        .Http(http => http.ResolveFromClaims()) // the ambient tenant that propagation captures
        .Messaging(messaging => messaging.PropagateTenant())
);
```

The root tenancy seam registers `TenantPropagationPublishMiddleware` (stamps the tenant option from ambient `ICurrentTenant.Id`) and `TenantPropagationConsumeMiddleware` (calls `ICurrentTenant.Change(...)` for the lifetime of the consume) on both lanes: `IBus.PublishAsync` and `IQueue.EnqueueAsync` stamp the ambient tenant alike, and every Bus and Queue consumer, including a responder, runs inside the envelope tenant's scope under every inbox guarantee. Caller-set values on `PublishOptions.TenantId` and `QueueOptions.TenantId` are preserved verbatim — set one explicitly to override the ambient tenant. See the multi-tenancy doc's [Message Consumers](multi-tenancy.md#message-consumers) section for the trust boundary and the strict-tenancy guard.

## Message Ordering Guarantees

Message ordering guarantees depend on the transport provider and configuration:

### Transport-Specific Ordering

- **Kafka**: Messages with same partition key are strictly ordered within partitions. With concurrent consumers, Headless commits offsets only up to the lowest offset still in flight for each partition, so a fast high offset does not acknowledge lower in-flight messages.
- **Azure Service Bus**: FIFO ordering when sessions are enabled (`EnableSessions = true`)
- **RabbitMQ**: No ordering guarantees by default; consumers may process messages concurrently
- **AWS SQS**: FIFO queues provide strict ordering; standard queues do not
- **Redis Streams**: Ordered within a stream, but parallel consumers may process out of order
- **NATS**: Ordering preserved per subject, but concurrent consumers introduce variability
- **Pulsar**: Ordered within partitions when using partition key
- **InMemory**: FIFO ordering with single consumer thread

### Configuration Impact on Ordering

- **`ConsumerThreadCount > 1`**: Enables concurrent message consumption, messages may process out of order
- **`EnableSubscriberParallelExecute = true`**: Buffers messages in-memory queue for parallel processing, no ordering guarantee
- **Single consumer thread (`ConsumerThreadCount = 1`)**: Sequential processing, maintains transport order

### Recommendations

- For strict ordering: Use `ConsumerThreadCount = 1` with Kafka (partition key), Azure Service Bus (sessions), or AWS SQS (FIFO)
- For high throughput: Use parallel processing; design consumers to be order-independent
- Test ordering behavior with your specific transport and configuration

## Circuit Breaker

Per-consumer circuit breaker that pauses transport consumption when a dependency is unhealthy, preventing message-retry storms. A circuit belongs to one consumer identity on one lane: it opens on that consumer's failures and pauses every client that delivers to it.

**State machine:** Closed → Open (pause transport) → HalfOpen (probe) → Closed (resume) or Open (re-trip).

Open duration escalates exponentially on repeated trips and resets after consecutive successful close cycles.

Persisted received retries share the same lane-qualified probe generation as transport delivery. Open rows are durably deferred to the current circuit generation's next-probe boundary; in HalfOpen, one row or transport delivery owns the probe, while sibling claims retain their exact leases for normal store-authoritative expiry without blocking healthy pickup. Healthy consumers in the same claimed batch dispatch before circuit dispositions, so an open consumer cannot monopolize retry pickup.

Pause and resume work carries a monotonic circuit epoch, and each consumer client handle applies intents
in epoch order. A resume launched before `ForceOpenAsync`, another Open transition, or a restart
pre-pause cannot reopen a newer Open generation. Force-open therefore leaves the transport paused
even when recovery was already in flight.

### Global Configuration

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // Global circuit breaker (applies to all consumers)
    setup.Options.CircuitBreaker.FailureThreshold = 5; // consecutive transient failures to trip
    setup.Options.CircuitBreaker.OpenDuration = TimeSpan.FromSeconds(30); // initial open duration
    setup.Options.CircuitBreaker.MaxOpenDuration = TimeSpan.FromSeconds(240); // cap after escalation

    // Adaptive retry backpressure
    setup.Options.RetryProcessor.AdaptivePolling = true;
    setup.Options.RetryProcessor.MaxPollingInterval = TimeSpan.FromMinutes(15);
    setup.Options.RetryProcessor.CircuitOpenRateThreshold = 0.8; // back off above 80% circuit-open rate
});
```

### Per-Consumer Override

Override the host settings for one consumer by its identity. A setting left `null` on `ConsumerCircuitBreakerOptions` falls back to `MessagingOptions.CircuitBreaker`.

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.Tune("payments.handler", consumer => consumer.CircuitBreaker(cb =>
    {
        cb.FailureThreshold = 3; // more sensitive
        cb.OpenDuration = TimeSpan.FromSeconds(60); // longer cooldown
        cb.IsTransientException = ex => ex is PaymentGatewayUnavailableException;
    }));

    // Disable the circuit breaker for a best-effort consumer
    setup.Tune("metrics.handler", consumer => consumer.CircuitBreaker(cb => cb.Enabled = false));
});
```

The same overrides bind from configuration, applied after every `Tune` call. `IsTransientException` is code-only.

```json
{
  "Headless": {
    "Messaging": {
      "Consumers": {
        "payments.handler": { "CircuitBreaker": { "FailureThreshold": 3, "OpenDuration": "00:01:00" } },
        "metrics.handler": { "CircuitBreaker": { "Enabled": false } }
      }
    }
  }
}
```

Overrides for one identity do not merge; unset fields fall back only to the host options. A later `CircuitBreaker(...)` call, or a `CircuitBreaker` configuration section, replaces the earlier override and drops every value it set, including a code-set `IsTransientException`. An every-instance consumer has no circuit breaker, so a circuit-breaker override on one fails startup.

### Custom Exception Predicate

```csharp
setup.Options.CircuitBreaker.IsTransientException = ex =>
    CircuitBreakerDefaults.IsTransient(ex) || ex is MyCustomTransientException;
```

Default `CircuitBreakerDefaults.IsTransient` covers: `TimeoutException`, `HttpRequestException` (5xx), `SocketException`, `BrokerConnectionException`, `TaskCanceledException` (timeout-only).

### Observability

- **OTel counter**: `messaging.circuit_breaker.trips` (tagged `messaging.consumer.group.name` with the lane-qualified consumer identity)
- **OTel histogram**: `messaging.circuit_breaker.open_duration` (same tag)
- State transitions logged at Warning level

### Programmatic Control

Inject `ICircuitBreakerMonitor` for runtime observation and manual recovery:

```csharp
var monitor = app.Services.GetRequiredService<ICircuitBreakerMonitor>();

// Enumerate the lane-qualified circuit keys of registered consumers (available before any messages are processed)
IReadOnlySet<string> consumers = monitor.KnownConsumers;

// Check state
var states = monitor.GetAllStates(); // every consumer circuit with its current state
var isOpen = monitor.IsOpen(MessageLane.Bus, "payments.handler");
var state = monitor.GetState(MessageLane.Bus, "payments.handler"); // Closed, Open, HalfOpen, or null if unregistered

// Rich snapshot with escalation and timing details
CircuitBreakerSnapshot? snapshot = monitor.GetSnapshot(MessageLane.Bus, "payments.handler");

// snapshot.State, snapshot.EscalationLevel, snapshot.ConsecutiveFailures,
// snapshot.FailureThreshold, snapshot.OpenedAt, snapshot.EstimatedRemainingOpenDuration,
// snapshot.EffectiveOpenDuration

// Manual recovery (operator/agent action)
var wasReset = await monitor.ResetAsync(MessageLane.Bus, "payments.handler"); // true if reset performed
var wasOpened = await monitor.ForceOpenAsync(MessageLane.Bus, "payments.handler"); // true if force-opened
```

Inject `IRetryProcessorMonitor` for adaptive retry backpressure inspection and reset:

```csharp
var retryMonitor = app.Services.GetRequiredService<IRetryProcessorMonitor>();

// Inspect backpressure state
var pollingInterval = retryMonitor.CurrentPollingInterval;
var isBackedOff = retryMonitor.IsBackedOff;

// Manual recovery (operator/agent action)
await retryMonitor.ResetBackpressureAsync(cancellationToken);
```

### Cluster Scope Limitation

The circuit breaker operates per-process only. There is no cross-instance coordination — each application instance maintains its own circuit state. In a multi-replica deployment, one instance may have an open circuit while others remain closed.

## Messaging runtime effects

- Starts background hosted service for message processing
- Creates database tables for outbox storage (via storage provider)
- Establishes transport connections (via transport provider)
- Opens one reply channel per process when the host calls `AddRequestReply` (see [Request/reply](#requestreply))

---

## Headless.Messaging.Dashboard

Web-based dashboard for monitoring and managing distributed messaging infrastructure.

Read [dashboards.md](dashboards.md) for the shared authentication modes and production security boundary.

### API and behavior

- **Real-Time Monitoring**: Live message throughput and latency metrics
- **Message Explorer**: Search, filter, and inspect messages
- **Failure Management**: View and retry failed messages
- **Node Discovery**: Multi-instance cluster visibility through async `INodeDiscoveryProvider` operations with optional trailing cancellation tokens; implementations propagate caller-requested cancellation instead of converting it to an empty or not-found result
- **Provider Capabilities**: The protected metadata endpoint and responsive footer dialog show every registered provider role. Transport cards report delivery lanes and topology, storage cards report delivery lanes and delayed scheduling, and coordination cards report cluster coordination without exposing physical resource names or credentials
- **Performance Metrics**: Consumer processing stats and bottlenecks
- **Five authentication modes** (the same modes as the Jobs Dashboard, via `Headless.Dashboard.Authentication`): none, Basic, API key, host-app auth, custom. The configuration is the dashboard's own, registered under `MessagingDashboardOptionsBuilder.AuthenticationName`, so a Jobs dashboard in the same host can use another mode.
- **Scheduled-delivery operator actions**: `GET /api/scheduled` lists pending scheduled deliveries (published rows in `Delayed`/`Queued` with no inline attempt, retry, or persisted retry time, presented as one `Pending` state); `POST /api/scheduled/revoke` and `POST /api/scheduled/dispatch-now` are audited, fenced mutations sharing the inbox operator ledger. These are operator actions on an existing schedule, not a way to create, reschedule, or replay one — see the `Headless.Messaging` package section for the storage-side contract.

### Design constraints

The dashboard exposes operational endpoints for inspecting, retrying, re-executing, and deleting message records. Its protected `/api/meta` response also projects sanitized registered-provider descriptors; deployment state remains operator-owned and is never inferred by the dashboard. Treat `WithNoAuth()` as development-only unless the dashboard is isolated behind trusted network controls. Production deployments should use `WithHostAuthentication(...)`, `WithBasicAuth(...)`, `WithApiKey(...)`, or `WithCustomAuth(...)`, and should set an explicit CORS policy before exposing the dashboard cross-origin.

The dashboard shows every stored payload in full to anyone it authorizes, which is one reason a payload must never carry a secret. See [Never put a secret in a message payload](#agent-rules).

Inbox query and operation JSON uses camelCase properties and named string enum values, such as `"Failed"`, `"Succeeded"`, and `"Queue"`, independently of the host's JSON configuration. Operation requests must send `expectedStatus` as a string; responses use the same format for status, lane, operation type, and outcome, including conflict and not-found results.

Inbox and scheduled-delivery operations share one actor resolver and require an authenticated principal with a stable audit actor. The primary identity's name is used first, then its `NameIdentifier` or `sub` claim, then the authenticated dashboard username. An unauthenticated request returns HTTP 401 and ends the dashboard session. An authenticated principal with no usable name -- the no-auth mode's `anonymous` identity or the Host mode's shared `host-user` placeholder -- returns HTTP 403 with error code `g:operator_actor_required` and a body naming the remedy (configure Basic or Host authentication with a name or `sub` claim); the dashboard stays signed in. **Behavior change:** the inbox `host-user` case previously returned 401; it now returns 403, and `WithNoAuth()` deployments can no longer perform any operator action (inbox or scheduled-delivery), read-only monitoring is unaffected. ApiKey and Custom identities (`api-user`, `custom-user`) are accepted as before and attribute actions to the deployment's shared secret identity. Authorization retains the host's claims and role mappings. Operation bodies require a JSON content type; unsupported media types return HTTP 415 and malformed JSON returns HTTP 422.

The legacy `POST /api/published/requeue` and `POST /api/published/delete` bulk endpoints reject any id that matches the pending-scheduled-delivery predicate: each rejected id is reported in the response's `rejected` array alongside a message pointing at the audited `/api/scheduled/revoke` and `/api/scheduled/dispatch-now` actions, and the remaining ids are processed exactly as before. This fencing runs under the host principal, not the operator actor requirement, so it still functions under `WithNoAuth()` for non-pending rows.

### Install

```bash
dotnet add package Headless.Messaging.Dashboard
```

### Setup and use

The dashboard is enabled on the `MessagingSetupBuilder` via `UseDashboard(...)`; it does not need an explicit `app.Use...` call:

```csharp
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.UseDashboard(dashboard => dashboard.WithBasicAuth("admin", password));
});
```

### Configuration

Configured through `MessagingDashboardOptionsBuilder` inside `UseDashboard(...)`. Authentication must be chosen explicitly — if no `WithXxx` auth method (including `WithNoAuth()`) is called, the host fails to start. No CORS policy is applied by default (same-origin only); use `SetCorsOrigins(...)` for the cross-origin SPA case.

| Method | Default | Description |
| --- | --- | --- |
| `WithNoAuth()` | (no default — auth is required) | Explicitly opt out of authentication; development or trusted-network use only. |
| `WithBasicAuth(username, password)` | — | HTTP Basic authentication. |
| `WithApiKey(apiKey)` | — | API-key authentication. |
| `WithHostAuthentication(policy?)` | — | Reuse the host app's auth, with an optional authorization policy. |
| `WithCustomAuth(validator)` | — | Custom `(token, services) => bool` validation. |
| `WithSessionTimeout(minutes)` | `60` | Auth session lifetime. |
| `SetBasePath(path)` | `/messaging` | Dashboard URL path. |
| `SetStatsPollingInterval(ms)` | `2000` | How often the overview polls `/stats` and the real-time metrics; a non-positive value falls back to 2000. |
| `SetCorsPolicy(builder)` | none | CORS policy for cross-origin access. |

### Runtime behavior

Mounts the embedded web UI and monitoring API through an `IStartupFilter` (no explicit middleware call required), and registers dashboard and node-discovery services.

Node lookups (30s), node counts (60s, 20s on discovery failure) and the metrics history (10m) are memoized in `MessagingDashboardCache`, a dashboard-owned in-process cache registered by `UseDashboard(...)` and injected into `ConsulNodeDiscoveryProvider` / `K8sNodeDiscoveryProvider`. The dashboard deliberately does not call `AddMemoryCache()`: registering the shared `IMemoryCache` from a framework package would put dashboard entries in the host's cache, where they compete for its size limit and are evictable by its compaction, purely as a side effect of adding a diagnostics UI. It is also not a `Headless.Caching` `ICache` — `AddHeadlessCaching` accepts exactly one call per service collection, so a registration here would throw for every consumer that configures caching themselves, or force the caching package on consumers who only wanted the dashboard. These values are per-node discovery results and metrics snapshots that must never leave the process, so a private in-process cache is the correct scope; this is the one sanctioned exception to the "do not use `IMemoryCache` directly" rule in [index.md](index.md).

## Headless.Messaging.Dashboard.K8s

### API and behavior

- Kubernetes Service discovery restricted to the configured namespace.
- `UseK8sDiscovery(...)` extension.

### Install

```bash
dotnet add package Headless.Messaging.Dashboard.K8s
```

### Setup and use

```csharp
services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.UseK8sDiscovery();
});
```

### Configuration

Configure `K8sDiscoveryOptions` through `UseK8sDiscovery(...)`:

- `K8sClientConfig` — Kubernetes client configuration used to query the cluster. Defaults to `KubernetesClientConfiguration.BuildDefaultConfig()`. Its configured namespace is the only namespace eligible for dashboard discovery and proxy selection; discovery fails closed when no namespace is configured.
- `ShowOnlyExplicitVisibleNodes` — when `true` (default), only Services labeled `headless.messaging.visibility:show` are listed as visible dashboard nodes. Set to `false` to show all discovered Services.

The dashboard stores the selected Service name rather than a client-composed endpoint. The server resolves that name in the configured namespace and reuses the list's visibility and port-label rules before forwarding. Invalid, hidden, cross-namespace, and stale selections are cleared.

### Runtime behavior

- Registers a Kubernetes-backed node discovery provider.
- Queries the Kubernetes API for Services in the configured namespace.
- Requires RBAC permissions to read Services in the configured namespace.

## OpenTelemetry (native, in Headless.Messaging)

### Purpose

Spans and metrics for messaging publish, persist, consume, and subscriber-invoke flows are emitted **natively** from `Headless.Messaging` via BCL `System.Diagnostics` `ActivitySource`/`Meter` primitives. There is **no** separate `Headless.Messaging.OpenTelemetry` satellite package (removed) and no `DiagnosticSource` bridge — Core references only the 80 KB, dependency-free `OpenTelemetry.Api` (for W3C context propagation and the typed provider-builder helpers), never the SDK. Any consumer subscribes: an OpenTelemetry exporter, a raw `ActivityListener`/`MeterListener`, Application Insights, or `dotnet-counters`. Cross-cutting naming, PII, and registration rules for all Headless instrumentation live in [OpenTelemetry instrumentation conventions](../solutions/conventions/opentelemetry-instrumentation-conventions.md).

### API and behavior

- The Meter/ActivitySource is named `Headless.Messaging` — exposed as the `public const string MessagingDiagnostics.SourceName`.
- Typed `AddMessagingInstrumentation()` extensions on both `TracerProviderBuilder` (namespace `OpenTelemetry.Trace`) and `MeterProviderBuilder` (namespace `OpenTelemetry.Metrics`) — thin `AddSource`/`AddMeter` wrappers over the const. Subscribing by name is equally supported.
- Instrument names + standard dimensions follow the OpenTelemetry messaging **semconv** (`messaging.publish.messages`, `messaging.consume.duration`, dims `messaging.operation` / `messaging.system` / `messaging.consumer.group.name` (valued with the consumer identity) / `error.type` / `messaging.subscriber` / `messaging.persistence.type`); framework-specific span attributes are bespoke `headless.messaging.*`.
- Inbox lifecycle counters are `messaging.inbox.duplicates`, `.attempts`, `.recoveries`, `.terminal`, `.replays`, `.retention`, and `.capabilities`. Their fixed labels are registered consumer identity, lane, finite outcome, tier, and provider; tenant is present only with the explicit cardinality opt-in.
- W3C `traceparent` + baggage are injected on publish headers and extracted on consume — **always on whenever any messaging telemetry is enabled**, no toggle. A metrics-only service (meter subscribed, no trace listener) — or a sampled-out publish — **relays** the incoming/ambient parent context verbatim onto outgoing messages instead of dropping it, so trace continuity survives non-tracing hops; a consumed message's context flows to publishes made from its handler even without a span. A fully unobserved host (no listeners at all) pays nothing and forwards nothing. The framework never fabricates a root: relay happens only when a parent actually exists. The app's OpenTelemetry setup must assign `Propagators.DefaultTextMapPropagator` (the standard `AddOpenTelemetry().WithTracing()` does this).
- `IActivityTagEnricher` extension point, invoked **synchronously at span start** (`void Enrich(Activity activity, in MessagingEnrichmentContext context)`), with per-enricher exception isolation.

### Design constraints

- Delivery mode tags use lowercase values on spans and metrics. `headless.messaging.delivery.requested` and `headless.messaging.delivery.resolved` now only ever emit `durable` or `direct` — that removed `DeliveryMode` member never appears as a requested or resolved value. Queries and alerts must use `direct` for `DeliveryMode.Direct`. The former `transport_direct`, `auto`, and "coordinated" values have no compatibility alias.
- Metrics are always registered; **subscribing a meter is the toggle** — there is no `EnableMetrics` flag. Emission is near-free when unobserved (`ActivitySource.HasListeners()` / `Counter.Enabled` early-outs).
- Enricher registration and the built-in suppression toggles live on the **messaging setup builder** (`setup.Instrumentation`), not at OpenTelemetry-registration time. This is what fixes the old bridge's fire-and-forget async-enricher wart: enrichers run synchronously, so every tag they add is attached before the span can end.
- **PII guardrails.** Enrichers must not write the reserved namespaces `messaging.*`, `server.*`, `headless.messaging.*`, `exception.*` (the framework/SDK overwrite them). The tenant attribute is controlled by `TenantTelemetryOptions.EnrichTraces`. Never serialize raw `context.Headers` onto tags — they may carry tokens/PII.

### Span attributes and toggles

| Tag / attribute | Emitted by | Toggle |
| --- | --- | --- |
| `headless.messaging.intent` (`bus`/`queue`) + `messaging.destination.kind` | built-in `IntentTagEnricher` | `setup.Instrumentation.SuppressIntentTags` |
| `tenant.id` (`TenantTelemetryOptions.AttributeName`), written before any enricher runs | `MessagingTelemetry` | `TenantTelemetryOptions.EnrichTraces`, set through `AddHeadlessTenancy(t => t.Telemetry(...))`; see [multi-tenancy observability](multi-tenancy.md#observability) |
| `headless.messaging.retry_count` | built-in `RetryCountTagEnricher` (subscriber-invoke) | `setup.Instrumentation.SuppressRetryCountTag` |
| custom tags | your `IActivityTagEnricher` | `setup.Instrumentation.AddEnricher(...)` |

### Setup and use

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// 1. Register enrichers / suppression on the messaging setup builder (optional).
builder.Services.AddHeadlessMessaging(setup =>
{
    // ... transport + storage registration ...
    setup.Instrumentation.SuppressRetryCountTag = true;    // opt out of retry-count tagging
    setup.Instrumentation.AddEnricher(new MyTagEnricher()); // custom tags
});

// 2. Subscribe the messaging scope on your OpenTelemetry providers.
builder
    .Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddMessagingInstrumentation())
    .WithMetrics(metrics => metrics.AddMessagingInstrumentation());
```

### Instrument reference

All instruments register on the `Headless.Messaging` meter. Names and standard dimensions follow the OTel messaging semantic conventions; span attributes specific to the framework are namespaced `headless.messaging.*`.

| Instrument | Kind | Dimensions |
| --- | --- | --- |
| `messaging.publish.messages` | Counter | `messaging.operation`, `messaging.system` |
| `messaging.publish.errors` | Counter | `messaging.operation`, `messaging.system`, `error.type` |
| `messaging.publish.duration` | Histogram (ms) | `messaging.operation`, `messaging.system` |
| `messaging.consume.messages` | Counter | `messaging.operation`, `messaging.system`, `messaging.consumer.group.name` |
| `messaging.consume.errors` | Counter | `messaging.operation`, `messaging.system`, `error.type`, `messaging.consumer.group.name` |
| `messaging.consume.duration` | Histogram (ms) | `messaging.operation`, `messaging.system`, `messaging.consumer.group.name` |
| `messaging.subscriber.invocations` | Counter | `messaging.subscriber`, `messaging.operation` |
| `messaging.subscriber.errors` | Counter | `messaging.subscriber`, `messaging.operation`, `error.type` |
| `messaging.subscriber.duration` | Histogram (ms) | `messaging.subscriber`, `messaging.operation` |
| `messaging.persistence.duration` | Histogram (ms) | `messaging.operation`, `messaging.persistence.type` |
| `messaging.message.size` | Histogram (bytes) | `messaging.operation`, `messaging.system` |
| `messaging.request_reply.requests` | Counter | `messaging.request_reply.outcome` |
| `messaging.request_reply.duration` | Histogram (ms) | `messaging.request_reply.outcome` |
| `messaging.request_reply.dropped_replies` | Counter | `messaging.request_reply.drop_reason` |

Framework span attributes: `headless.messaging.intent` (`bus`/`queue`), `tenant.id` (named and switched by `TenantTelemetryOptions`), `headless.messaging.retry_count` (suppressible), plus per-phase duration attributes (`headless.messaging.persistence.duration_ms`, `send.duration_ms`, `receive.duration_ms`, `invoke.duration_ms`) retained verbatim from the pre-migration bridge.

## Headless.Messaging.Aws

### API and behavior

- `setup.UseAws(...)`.
- SNS topics for bus publishing.
- SQS queues for queue delivery.
- FIFO topic/queue support.
- Message hatch on either lane builder: `.OnBus(b => b.UseAws(aws => aws.MessageGroupId(message => ...)))` or `.OnQueue(q => q.UseAws(...))`.
- Every-instance consumers are not supported: startup fails naming the consumer.
- Request/reply is not supported: AWS has no .NET temporary-queue client, so a host that sends requests or declares a responder fails startup. See [Request/reply](#requestreply).
- Consumer startup honors host cancellation through SNS/SQS provisioning and subscription.
- `AutoProvision = false` looks topics and queues up instead of creating them, for topology managed by Infrastructure as Code.
- Received messages stay hidden until the core settles them: a heartbeat extends their visibility, and shutdown drains running handlers.

### Design constraints

`MessageGroupId(...)` is message-side only because it is stamped while publishing. The provider maps it to native FIFO `MessageGroupId`; it is not a custom message attribute. Values longer than 128 characters are rejected.

Malformed transport envelopes are terminally deleted after sanitized logging. Handler rejection makes the message visible again after 3 seconds, so it is a normal SQS retry and can use an external SQS redrive policy.

### Install

```bash
dotnet add package Headless.Messaging.Aws
```

### Setup and use

```csharp
setup.UseAws(options =>
{
    options.Region = Amazon.RegionEndpoint.USEast1;
});

services.ConfigureMessaging(messaging =>
    messaging
        .Message<PlaceOrder>("orders-place.fifo")
        .OnQueue(queue => queue.UseAws(aws => aws.MessageGroupId(order => order.CustomerId.ToString())))
);

[QueueConsumer("orders.place-order")]
public sealed class PlaceOrderWorker : IConsume<PlaceOrder>
{
    public ValueTask ConsumeAsync(ConsumeContext<PlaceOrder> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
```

AWS declares immutable Bus and Queue capabilities with independent SNS/SQS topology. Bus uses `bus-{logical-name}` SNS topics and one `bus-{consumer-identity}` SQS queue per consumer identity; Queue sends directly to `queue-{logical-name}`.

### Configuration

`RoutingAffinityKey` maps to native `MessageGroupId` only for registered `.fifo` SNS topics or SQS queues. Keys are 1–128 printable ASCII characters (`!` through `~`), without spaces. `AwsMessagingHeaders.MessageGroupId` and `MessageGroupId(...)` remain raw adapters and must agree with a supplied typed key. Standard SQS message-group fairness is not an affinity guarantee; typed keys on standard routes are rejected. Shared groups do not imply whole-pipeline FIFO or handler exclusivity. No application headers are discarded.

Both lanes use one envelope. Every send encodes the complete header dictionary, including null, delivery, trace, and business metadata, as one String attribute named `headless-aws-headers-v1`: an SQS message attribute on the Queue lane, and an SNS message attribute on the Bus lane. The payload body and native `MessageGroupId` remain unchanged. Bus subscriptions use SNS raw message delivery (`RawMessageDelivery = true`), so SQS receives the published body and the bag as they were sent, not an SNS JSON wrapper. With `AutoProvision`, the consumer asks for raw delivery when it creates a subscription and sets it on one that already exists (`sns:SetSubscriptionAttributes`). One attribute stays within the SQS limit of ten message attributes, which raw delivery would otherwise exceed. Consumers on both lanes require that exact attribute; other names with the same prefix are ordinary application headers inside the bag. Missing bags (including an SNS-wrapped body from a subscription without raw delivery), mixed attributes, malformed JSON, duplicate or reserved header names, and invalid value types are terminally deleted from the source queue without a handler callback. Affinity is optional, so unkeyed messages use the same format.

Configure AWS region, service URLs, and credentials through `AmazonSqsMessagingOptions`. Its other settings:

| Option | Default | Effect |
| --- | --- | --- |
| `AutoProvision` | `true` | Creates SNS topics, SQS queues, the queue access policy, and raw-delivery SNS-to-SQS subscriptions on first use. With `false`, queues are looked up with `sqs:GetQueueUrl` and topics with `sns:ListTopics`, and nothing is created, subscribed, or given a policy. Every topic, queue, and subscription must then exist before the host starts, and each Bus subscription must enable raw message delivery. A missing queue fails consumer startup naming the queue lookup; a missing topic fails the send. |
| `VisibilityTimeout` | 30 s | Sent on every receive, so the queue's own default does not apply. Whole seconds from 1 second to 12 hours. |
| `ReceiveWaitTime` | 5 s | SQS long-poll wait per receive. Whole seconds from 0 to 20. |

With `AutoProvision = false`, a consumer needs `sqs:GetQueueUrl`, `sqs:ReceiveMessage`, `sqs:DeleteMessage`, and `sqs:ChangeMessageVisibility`. A Bus publisher needs `sns:ListTopics` and `sns:Publish`, and a Queue publisher needs `sqs:GetQueueUrl` and `sqs:SendMessage`.

### Runtime behavior

Registers SNS/SQS clients, bus/queue transports, and AWS consumer client services.

- **Visibility heartbeat.** A receive takes up to ten messages, and every one of them stays unsettled until the core commits or rejects it. That includes messages still waiting for a free handler slot under the consumer's `Concurrency`. Every third of `VisibilityTimeout`, one loop per consumer client extends all unsettled messages by `VisibilityTimeout`, through `ChangeMessageVisibilityBatch` calls of ten. Commit and reject stop the extension first. A message SQS refuses to extend for a reason of the request, such as a receipt handle that is no longer current, is dropped from the heartbeat with a warning, because it may be redelivered; a service-side failure is retried on the next beat. A reject or shutdown release waits for an extension of the same client already in flight, so the extension never overrides it. SQS caps a message's total invisibility at 12 hours from its first receive.
- **Deletes.** Commits to one queue share `DeleteMessageBatch` calls when they overlap: the first delete is sent at once, and the deletes that arrive while it is in flight leave together in the next call. A lone delete adds no wait. Each caller gets its own entry's outcome; a stale receipt handle is logged, not thrown.
- **Shutdown.** `ShutdownAsync` stops receiving, then waits for the receive loop and running handlers within the shutdown budget (30 seconds for `DisposeAsync`). The heartbeat keeps running meanwhile, so slow handlers still settle. Messages still unsettled after that, because they were never handed over or because their handler outlived the budget, are made visible at once instead of after their timeout. The SQS and SNS clients are disposed last.

## Headless.Messaging.AzureServiceBus

### API and behavior

- `setup.UseAzureServiceBus(...)`.
- Topic and queue transport support.
- Session-aware processing.
- Message hatch on either lane builder: `.OnBus(b => b.UseAzureServiceBus(asb => asb.PartitionKey(message => ...)))` or `.OnQueue(q => q.UseAzureServiceBus(...))`.
- Consumer startup honors host cancellation through client, topology, and processor setup.
- Request/reply is not supported yet: a host that sends requests or declares a responder fails startup until the provider's reply channel ships. See [Request/reply](#requestreply).
- Shared connection: bus and queue publishing and consumer processors share one `ServiceBusClient` (one AMQP connection) per namespace with per-destination cached senders and a shared administration client; senders are drained before the client on shutdown, and consumers stop their processors without touching the shared client.

### Design constraints

`PartitionKey(...)` is message-side only and limited to 128 characters. When sessions are enabled, Azure Service Bus requires `PartitionKey` to equal `SessionId`; the message builder rejects mismatches.

Headless disables Azure SDK auto-complete internally and settles messages explicitly after durable receive storage and handler outcome.

Structurally malformed envelopes, including envelopes with missing or invalid required Messaging headers, are terminally completed after sanitized logging to prevent poison redelivery. Retryable handler or custom-header hook failures are not classified as terminal malformed failures; they remain unsettled or are abandoned according to the runtime failure path.

### Install

```bash
dotnet add package Headless.Messaging.AzureServiceBus
```

### Setup and use

```csharp
setup.UseAzureServiceBus(options => options.ConnectionString = connectionString);

services.ConfigureMessaging(messaging =>
    messaging
        .Message<OrderPlaced>("orders.placed")
        .OnBus(bus => bus.UseAzureServiceBus(asb => asb.PartitionKey(order => order.CustomerId.ToString())))
);
```

A `[BusConsumer("orders.projection")]` consumer of `OrderPlaced` gets the subscription `orders.projection` on the Bus topic. Azure Service Bus declares immutable Bus and Queue capabilities with independent topic/queue topology, so the same contract and logical name can carry Bus and Queue consumers without cross-delivery.

### Configuration

`RoutingAffinityKey` maps to native `SessionId` on registered session-enabled routes, with a 128 UTF-16-code-unit maximum. Queue routes require `EnableSessions`; Bus routes may use global sessions or a matching custom producer with sessions. Raw `SessionId` and `PartitionKey` must both agree with a supplied typed key. A non-session partition key alone is insufficient configuration evidence. Local startup validation does not query the broker: the actual queue/subscription must also require sessions. Affinity does not promise application-handler exclusivity or whole-pipeline FIFO.

Configure connection string or namespace, retry/client settings, queue/topic behavior, session support, and SQL filters through `AzureServiceBusMessagingOptions`. Authentication is an either/or contract: supply either `ConnectionString` or both `Namespace` and `TokenCredential` — both are nullable (`string?`) and the validator enforces that exactly one mode is configured at start. Processor settlement is not configurable; Headless disables Azure SDK auto-complete and completes or abandons messages explicitly.

### Runtime behavior

Registers a shared client pool (one `ServiceBusClient` per namespace, shared by the bus and queue transports and every consumer client, plus a shared administration client), transports, consumer client factory, and producer descriptor services.

## Headless.Messaging.InMemory

### API and behavior

- `setup.UseInMemory()`.
- In-process bus and queue delivery.
- Bus fan-out per consumer identity, competing replicas, Queue ownership, every-instance subscriptions, and same-name Bus/Queue isolation within the process.
- No external broker.
- Request/reply through an in-process reply channel per listener. See [Request/reply](#requestreply).
- Consumer startup implements the same host-cancellable contract as broker-backed providers.

### Install

```bash
dotnet add package Headless.Messaging.InMemory
```

### Setup and use

```csharp
setup.UseInMemory();
```

### Configuration

The in-memory transport declares no native routing-affinity mapping. `RequireRoutingAffinity()` fails during startup; a supplied `RoutingAffinityKey` is rejected before persistence or transport effects. This is separate from in-memory storage, which preserves the key when paired with a supported transport.

None.

### Runtime behavior

Registers in-memory transports and consumer client factory. Messages are lost when the process exits.

## Headless.Messaging.Storage.InMemory

### API and behavior

- `IMessageRevocationStorage` atomically deletes a scheduled row before reservation, fenced by storage version, terminal status, and retry state. Claimed but unreserved rows remain revocable; deleted rows cannot be restored by reservation or shutdown flush.
- `setup.UseInMemoryStorage()`.
- Stores published, received, failed, and monitoring state in memory.
- Declares `InboxGuarantee.ProcessLocal`; state and duplicate suppression do not survive process restart and cannot satisfy a durable transactional requirement.

InMemoryStorage uses its injected `TimeProvider` for both application-scheduled `NextRetryAt` and authoritative lease ownership. It implements the same duration-based lease SPI and returns the persisted `(LockedUntil, Owner)` identity. Delayed scheduling atomically transitions and leases each per-message winner before returning a deterministic bounded batch. Circuit-open received retries atomically advance `NextRetryAt` and clear only the exact live `(lane, Owner, LockedUntil)` lease generation under the per-row lock. Retry pickup claims due rows in `NextRetryAt` order, as the relational providers do, so an earlier-scheduled row is never starved by a later one once `RetryBatchSize` bounds the batch. Rows sharing an identical `NextRetryAt` fall back to a deterministic per-provider tie-break, which no fairness guarantee depends on.

### Install

```bash
dotnet add package Headless.Messaging.Storage.InMemory
```

### Setup and use

```csharp
setup.UseInMemoryStorage();
```

### Configuration

No provider-specific configuration is required.

Known orphans use a separate bounded probe batch and recover only when the exact consumer identity, logical contract name/version, and lane return. They do not expire automatically. Unclaimed orphans permit Hold/ReleaseHold and unheld Purge; live claims block those actions, and ForceReprocess remains terminal-only. A hold protects retention and purge but does not stop recovery.

History retention uses the shared `MessagingOptions` defaults: cleanup receipts/audits 7 days each, operator receipts 30 days, and operator audits 90 days. All four are positive configurable minimum residence durations. Audit references can extend receipt lifetime; deleting history does not release holds. The injected `TimeProvider` controls history age. State is process-local and is lost on restart. This history also covers scheduled-delivery operator actions (revoke, dispatch-now) under the shared `TargetKind` ledger; record shapes changed accordingly. See [Core configuration](#configuration-3) for probe settings, replay limits, rollout effects, and collector pacing.

### Runtime behavior

Registers in-memory storage and monitoring services. State is lost when the process exits.

## Headless.Messaging.Kafka

### API and behavior

- `setup.UseKafka(...)`.
- Kafka topic auto-creation support.
- Message hatch on the Queue lane builder only: `.OnQueue(q => q.UseKafka(kafka => kafka.PartitionBy(message => ...)))`.
- Consumer hatch: `Tune(identity, c => c.UseKafka(kafka => kafka.WithIsolationLevel(IsolationLevel.ReadCommitted)))`.
- A Queue consumer's Kafka `group.id` is its message name, so every host consuming that message joins one Kafka consumer group.
- Request/reply is not supported: Kafka has no per-process address short of a partition per instance, so a host that sends requests or declares a responder fails startup. See [Request/reply](#requestreply).
- Consumer startup honors host cancellation while creating topics and subscriptions.

### Design constraints

Kafka supports only the Queue lane in this package. A `[BusConsumer]` fails startup capability validation before provider creation, provisioning, or storage side effects, and an `IBus` publish fails when attempted. Message contracts are accepted: startup validates contract routes only on the Queue lane. `PartitionBy(...)` maps to the Kafka key. The framework does not impose a Kafka key length cap; broker/client configuration owns practical limits. Delivery remains at-least-once; consumers must dedupe by business key or message id. A publish succeeds only when Kafka reports `Persisted`; `PossiblyPersisted` is retried and can therefore produce duplicates. When consumer concurrency is greater than one, successful handlers can finish out of order, but Kafka commits advance only to the lowest offset still in flight for that partition; a completed high offset does not commit past lower in-flight offsets. Offsets the broker never hands to the application — transaction control records, aborted batches under `read_committed`, compaction holes, and tombstones — do not hold that watermark back, because ordered per-partition delivery proves they can never arrive later. Rebalances invalidate tracked offsets for revoked or lost partitions so late handlers cannot commit or seek partitions now owned by another consumer. Malformed transport envelopes are terminally logged and their offsets join the same per-partition completion watermark, bounding poison replay without skipping lower in-flight messages.

### Install

```bash
dotnet add package Headless.Messaging.Kafka
```

### Setup and use

```csharp
using Confluent.Kafka;

setup.UseKafka(options => options.Servers = "localhost:9092");

setup.Tune(PlaceOrderWorker.Identity, consumer =>
    consumer.UseKafka(kafka => kafka.WithIsolationLevel(IsolationLevel.ReadCommitted))
);

// In the Orders module:
services.ConfigureMessaging(messaging =>
{
    messaging.AddModule<Orders.MessagingModule>();
    messaging
        .Message<PlaceOrder>("orders.place")
        .OnQueue(queue => queue.UseKafka(kafka => kafka.PartitionBy(order => order.CustomerId.ToString())));
});

[QueueConsumer(Identity)]
public sealed class PlaceOrderWorker : IConsume<PlaceOrder>
{
    public const string Identity = "orders.place-order";

    public ValueTask ConsumeAsync(ConsumeContext<PlaceOrder> context, CancellationToken cancellationToken) =>
        ValueTask.CompletedTask;
}
```

### Configuration

`QueueOptions.RoutingAffinityKey` maps to the native UTF-8 string key on registered Queue routes. The optional `KafkaMessagingHeaders.KafkaKey` adapter must match it. `RequireRoutingAffinity()` rejects configurations with a random or unrecognized `MainConfig["partitioner"]`; accepted partitioners are `consistent`, `consistent_random` (default), `murmur2`, `murmur2_random`, `fnv1a`, and `fnv1a_random`, all deterministic for a nonempty key. Headless adds no key-length limit beyond broker message limits. Keep partition count, encoding, and partitioner fixed while relying on placement. Different keys may share partitions; affinity promises neither FIFO nor exclusive handling.

Configure bootstrap servers, main Kafka config, topic options, custom headers, and retriable error codes through `KafkaMessagingOptions`. `RetriableErrorCodes` / `DefaultRetriableErrorCodes` are `int` values of Confluent's `ErrorCode` enum (not the native enum type), so configuring retries needs no compile-time `Confluent.Kafka` reference; the framework casts back to `ErrorCode` internally.

### Runtime behavior

Registers Kafka transports, connection pool, consumer factory, and provider-specific message/consumer config support.

## Headless.Messaging.Nats

### API and behavior

- `setup.UseNats(...)`.
- JetStream stream provisioning modes and durable consumers. Consumer startup and the first publish to each stream both provision the stream, so a publish-only host works without a consumer host having started.
- `NatsMessagingOptions.Streams` declares streams by name: `Own(name, s => s.Subjects(...))` for a stream Headless creates and keeps in shape, `Bind(name, subjects)` for one managed elsewhere that Headless only checks. A message no declared stream covers keeps its derived `headless-{lane}-{key}` stream.
- `NatsMessagingOptions.DefaultStreamMaxAge` (default 7 days) bounds every stream Headless owns that sets no age limit of its own.
- `NatsMessagingOptions.UseConnection(sp => ...)` publishes, answers requests, and provisions streams over a NATS connection the app owns.
- `ConsumeContext.TransportAddress` is the subject the message arrived on, including any shard token.
- Message hatch on either lane builder: `.OnBus(b => b.UseNats(nats => nats.SubjectShard(message => ...)))` or `.OnQueue(q => q.UseNats(...))`.
- Consumer hatch: `Tune(identity, c => c.UseNats(nats => nats.Sharded()))`, needed only when the producer shards a message this host declares without `SubjectShard(...)`.
- Consumer startup honors host cancellation while connecting and provisioning JetStream topology, while preserving configured topology timeouts.
- Request/reply over a core NATS subscription on `headless.reply.{32 hex}`, outside JetStream; the address survives reconnects. Keep JetStream streams off `headless.reply.>`. See [Request/reply](#requestreply).
- Contributes the `messaging-nats` readiness health check (tags `ready`, `headless`, `messaging`), which sends a NATS `PING` on a pooled connection. See [Health checks](utilities.md#health-checks).

### Design constraints

`SubjectShard(...)` appends one safe subject token to the logical message name. It rejects `.`, `*`, `>`, whitespace, and control characters so payload values cannot change the subject hierarchy or wildcard behavior.

Shard coverage follows the contract. A consumer of a message whose contract, declared in this host, uses `SubjectShard(...)` on that lane filters on the `{subject}.>` wildcard automatically. The contract is the only thing the host can see, so when another service shards a message that this host declares without `SubjectShard(...)`, tune the consumer with `.UseNats(n => n.Sharded())`: NATS delivers zero messages with no error to a filter subject that matches no shard subject. Keeping one shared contract declaration for the message in both services avoids the gap.

Connection-specific failures (`NatsConnectionFailedException`, `NatsJSConnectionException`, or a `NatsException` wrapping `SocketException`/`IOException`) terminate the listener instead of retrying in place, so the supervising consumer register's health watchdog can replace the failed client. JetStream protocol, timeout, API, and other consumer errors retry per-subject with backoff. As a backstop, a run of `NatsMessagingOptions.MaxConsecutiveConsumeFailures` (default `10`) consecutive consume-loop failures of any exception type also terminates the listener for a supervised restart — bounding in-place spinning when a permanently dead connection surfaces an error that is not one of the classified connection-failure types (consumer connections set `MaxReconnectRetry = 0`, which NATS.Net treats as unlimited, so the client keeps reconnecting on its own and this counter is what hands a stuck listener back for a rebuild). The streak resets on any forward progress (a successful consumer bind or fetch). Consumer connection faults are owned by the health watchdog, not the per-message circuit breaker, which never observes connection-level failures. `NatsMessagingOptions.ConnectionPoolSize` defaults to `1` — a single connection multiplexes all publishers, so raise it only as a throughput knob. During host shutdown, NATS bounds its in-flight handler drain by the remaining shared `MessagingOptions.ShutdownTimeout` budget instead of starting an independent 30-second drain.

Commit uses JetStream double acknowledgement and waits for the broker's settlement confirmation before returning. This keeps immediate consumer replacement from racing an unconfirmed `ACK` and redelivering an already-successful message.

Bus publishes to `headless.bus.{logical-name}` with interest-retained streams and `bus-{consumer-identity}-{logical-name}` durables. Queue publishes to `headless.queue.{logical-name}` with work-queue-retained streams and the shared `queue-{logical-name}` durable. `StreamOptions` may tune storage, replicas, and limits but cannot replace provider-owned stream names, subjects, or retention.

A message lives on the stream declared in `NatsMessagingOptions.Streams` whose subjects cover its subject (`headless.{lane}.{name}`), and otherwise on the stream derived from its name, below. Declare a stream when its name, subjects, retention, or limits matter to operators, or when it is managed outside the application:

- `Own(name, s => ...)` declares a stream Headless creates and keeps in shape under `StreamProvisioning`, exactly like a derived one. It takes the declared subjects (`Subjects(...)`, wildcards allowed, at least one required), defaults to limits retention, and accepts `Retention(...)`, `MaxAge(...)`, `MaxBytes(...)`, and `Configure(config => ...)` for any other setting; `Configure` runs before `StreamOptions`, and neither may change the name, subjects, or retention. Owned declared streams are created in the background when the messaging host starts; a failure there is logged (EventId 16, `NatsStreamWarmupFailed`) and never fails startup, because the first publish or consumer start ensures the stream again.
- `Bind(name, subjects)` declares a stream created by the NATS CLI, Terraform, or a Kubernetes operator. Headless never creates, updates, or deletes it, even under `Reconcile`; it fails a publish or consumer start when the stream is missing, does not carry the declared subjects, or uses work-queue retention for Bus messages, which need one durable consumer per consumer identity.

A declaration is checked when it is made. The name may not contain whitespace, `.`, `*`, `>`, `/`, or `\`, start with the reserved `headless-`, or repeat another declaration. Its subjects may not overlap another declared stream's, since JetStream refuses overlapping streams and a message must resolve to one stream, nor the reserved `headless.reply.>`, `$JS.>`, `$SYS.>`, or `_INBOX.>`. Keep declared Headless messages on the lane prefixes: a stream that covers `headless.queue.orders.>` carries the Queue lane only, so a Bus message of the same name still resolves to its own stream. Work-queue retention refuses a second Bus consumer identity, and interest retention discards a Queue message no consumer exists for yet, so limits retention, bounded by an age, suits both lanes and is the default. On the Bus lane retention changes storage, not delivery: a Bus consumer starts at messages published after it was created, whatever the retention.

A message no declared stream covers lives on a derived stream, `headless-{lane}-{key}`, where the key is the message name's first dot-separated segment. The stream carries the key's wildcard, `headless.{lane}.{key}.>`, rather than the subjects of the messages one host knows. The bare `headless.{lane}.{key}` is added only for a message named exactly the key. Every host therefore asks for the same subjects whichever messages it publishes or consumes on that key, and the first host to create the stream covers every later one. The one exception is a message named exactly its key that is published first without a shard and later with one: the shard wildcard is a subject the stream does not carry yet, which only `Reconcile` adds. Consumer filters stay exact per message. To choose a stream's name or group messages differently, declare it in `Streams`; the `NormalizeStreamName` option that used to map names to stream keys is gone.

`NatsMessagingOptions.StreamProvisioning` decides what a host does about a stream it owns, derived or declared with `Own`, at consumer startup and before the first publish to it. `Verify` (the default) creates a missing stream but throws with the divergent fields rather than writing to one that already exists; `Reconcile` updates fields JetStream accepts in place, reports immutable divergence instead of sending an update the server rejects, and `Disabled` neither creates nor modifies. The default changed because the old flag's `true` silently overwrote the storage class, replicas, and limits of a stream provisioned with the NATS CLI, Terraform, or a Kubernetes operator on every startup.

Every stream Headless owns gets an age limit: its declared `MaxAge`, else `NatsMessagingOptions.DefaultStreamMaxAge` (7 days), because NATS keeps a stream's messages without limit by default. `TimeSpan.Zero`, on the option or a declaration, keeps messages without an age limit. The limit is asserted like any other field, so under `Verify` an existing stream with a different age limit, such as one created without one, fails as divergent, and `Reconcile` updates it.

A publisher ensures each stream once per process, sharing one broker round trip between concurrent first publishes. A failed ensure fails that publish (a durable publish retries from the outbox), and every later publish to the stream fails with the same error, without a broker request, until a back-off ends. A configuration fault, which retrying cannot heal (a diverged stream, a missing or mismatched bound stream, a stream JetStream refuses with a 4xx API error), backs off 30 seconds, doubling to 5 minutes. Any other fault (a `StreamCreateTimeout`, a meta leader election, a lost connection) backs off 1 second, doubling to 30 seconds, so publishes recover soon after the broker does. Every stream create, info, or update request goes through the cluster's meta leader, so a failing stream must not turn each publish into another request. Only the caller's own cancellation leaves the shared ensure running without a back-off. A publish no stream answers (NATS.Net throws `NatsJSPublishNoResponseException` after its retries, or the server acknowledges with `seq=0`) fails and makes the next publish ensure again, which recovers a stream deleted while the host ran. A `StreamCreateTimeout` that expires during a publish-time ensure fails that publish with `PublisherSentFailedException`; only the caller's own cancellation propagates as `OperationCanceledException`. On the Queue lane a stream a publisher creates keeps messages until the first consumer binds; on the Bus lane an interest-retained stream with no consumer yet discards the message, as Bus delivery does for a subscriber that does not exist yet. A publisher whose first ensure of an interest-retained stream finds no consumer logs one warning per stream (EventId 17, `NatsInterestStreamWithoutConsumer`). Start Bus consumers before publishers, or send a message that must wait for its consumer on the Queue lane. A host whose streams are provisioned outside the application declares them with `Bind` rather than setting `Disabled`: under `Verify` an undeclared message would get a derived stream, and JetStream refuses a stream whose subjects overlap another's. `Disabled` remains for a host that must never touch JetStream's stream API. To keep environments apart on one NATS server, give each its own NATS account: stream names and subjects are scoped per account, so no prefix option is needed.

The comparison covers only fields the provider or the `StreamOptions` callback actually asserts — a field neither set is never compared, since the server defaults it and diffing it would report drift against every existing stream. Subjects compare asymmetrically: extra subjects on the live stream (from other hosts or an earlier deployment) are ignored, while a subject this host requires that the stream does not cover is a divergence, because JetStream delivers zero messages and reports no error to a filter that matches nothing. A stream created with exact subjects by an earlier version therefore reports as divergent under `Verify`; `Reconcile` adds the key's wildcard. Divergence on a field JetStream refuses to change on a live stream — storage type is the clearest case — is reported with a recreate-or-migrate remedy instead of a mode-switch suggestion that would fail at the server.

`UseConnection(sp => ...)` hands Headless a connection the app owns, for example the one `services.AddNatsClient(...)` registers. Publishing, request/reply, stream provisioning, and the `messaging-nats` health check use it; Headless never disposes it, and `Servers`, `ConfigureConnection`, and `ConnectionPoolSize` do not apply (a pool size other than `1` fails validation). Each consumer client still opens a connection of its own, copied from the supplied connection's options: a stuck consumer is recovered by replacing its connection, which Headless cannot do to one it does not own, and NATS disconnects a slow consumer's whole connection, which would cut the app's own traffic on a shared socket.
### Install

```bash
dotnet add package Headless.Messaging.Nats
```

### Setup and use

```csharp
setup.UseNats(options =>
{
    options.Servers = "nats://localhost:4222";

    // Name the Queue stream for orders and keep its messages 3 days; undeclared messages keep derived streams.
    options.Streams.Own("ORDERS", stream => stream.Subjects("headless.queue.orders.>").MaxAge(TimeSpan.FromDays(3)));
    options.Streams.Bind("PAYMENTS", "headless.bus.payments.>"); // provisioned outside the application
});

services.ConfigureMessaging(messaging =>
    messaging
        .Message<OrderPlaced>("orders.placed")
        .OnBus(bus => bus.UseNats(nats => nats.SubjectShard(order => order.CustomerId.ToString())))
);

// Only when the producer shards OrderPlaced but this host declares it without SubjectShard:
setup.Tune("orders.projection", consumer => consumer.UseNats(nats => nats.Sharded()));
```

An app that also uses NATS directly registers its connection once and hands it to Headless, instead of opening a second one:

```csharp
services.AddSingleton<NATS.Client.Core.INatsConnection>(_ => new NATS.Client.Core.NatsConnection(
    new NATS.Client.Core.NatsOpts { Url = "nats://localhost:4222" }));

setup.UseNats(options => options.UseConnection(sp => sp.GetRequiredService<NATS.Client.Core.INatsConnection>()));
```

NATS declares independent Bus and Queue topology, so the same contract and logical name can carry Bus and Queue consumers without cross-delivery. Malformed transport envelopes are terminally double-acknowledged and logged without payload or headers.

### Configuration

The current NATS subjects and stream topology do not provide the provider-neutral routing-affinity contract. `RequireRoutingAffinity()` fails during startup; a supplied `RoutingAffinityKey` is rejected before persistence or transport effects. Existing raw subject-shard hooks remain provider-specific configuration and do not establish a neutral key mapping. No transparent sharding topology is introduced.

Configure NATS servers, credentials, stream behavior, durable names, and connection settings through `NatsMessagingOptions`.

### Runtime behavior

Registers NATS connection pool, transports, consumer factory, stream provisioning, and the processing server that creates owned declared streams at host start.

## Headless.Messaging.Pulsar

### API and behavior

- `setup.UsePulsar(...)`.
- Pulsar bus and queue transport support.
- TLS-related options through provider configuration.
- Configurable negative-ack redelivery with a one-minute default and a validated 100-millisecond minimum.
- Producer settings through `PulsarMessagingOptions.Producer`: compression, batching and its publish delay, and send timeout.
- Shutdown lets in-flight handlers settle within the shutdown budget (30 seconds on a plain dispose) before it closes the consumer.
- Consumer startup honors host cancellation while acquiring the client and subscribing, while preserving configured timeouts.
- Request/reply is not supported yet: a host that sends requests or declares a responder fails startup until the provider's reply channel ships. See [Request/reply](#requestreply).

### Design constraints

Bus topics insert `headless-bus-` before the local topic name and use one lane-qualified `headless-bus-{consumer-identity}` subscription per consumer identity. Queue topics insert `headless-queue-` and use one owned `headless-queue` subscription per physical topic. Replicas within a subscription compete; distinct Bus consumer identities each receive one copy. Malformed transport envelopes are terminally acknowledged so they cannot create negative-ack redelivery storms. A handler still running when the shutdown budget runs out never acknowledges its message, so the broker redelivers it after the consumer closes (at-least-once). Negative-ack redelivery uses one fixed delay: Pulsar.Client 3.19 has no redelivery backoff. The package has no consumer hatch yet, so `Key_Shared` subscriptions, dead-letter policies, and ack timeouts are not configurable. Message chunking is not offered: with Pulsar.Client 3.19.4 a chunked message read through a `Shared` subscription, which every competing and Bus consumer uses, arrives truncated to its first chunk. Keep payloads under the broker's `maxMessageSize` (5 MB by default).

### Install

```bash
dotnet add package Headless.Messaging.Pulsar
```

### Setup and use

```csharp
setup.UsePulsar(options => options.ServiceUrl = "pulsar://localhost:6650");

services.ConfigureMessaging(messaging =>
    messaging.Message<OrderPlaced>("persistent://public/default/orders.placed")
);
```

A `[BusConsumer("orders.projection")]` consumer of `OrderPlaced` subscribes as `headless-bus-orders.projection`.

### Configuration

`RoutingAffinityKey` on publish/enqueue options maps to the native Pulsar message key on registered Bus and Queue routes. The optional `PulsarMessagingHeaders.PulsarKey` adapter must agree. The configured client uses its built-in key hashing; Headless adds no key-length limit beyond broker message limits. Keep routing configuration and partition topology fixed while relying on placement. This does not select a `Key_Shared` subscription, guarantee FIFO, or prevent concurrent handling.

Configure service URL, authentication, TLS, negative-ack redelivery, and producer settings through `PulsarMessagingOptions`. `Producer` applies to every producer the transport creates, and each default matches Pulsar.Client: `CompressionType` (`None`; also `LZ4`, `ZLib`, `ZStd`, `Snappy`, decompressed transparently by consumers), `EnableBatching` (`true`), `BatchingMaxPublishDelay` (1 ms; each publish waits for its own send, so a longer delay adds up to that much latency per publish in exchange for larger batches), and `SendTimeout` (30 seconds; `TimeSpan.Zero` waits indefinitely, and a send that times out fails the publish). `NegativeAckRedeliveryDelay` defaults to one minute and must be at least 100 milliseconds; smaller values fail startup validation instead of being silently clamped by Pulsar.Client.

### Runtime behavior

Registers Pulsar connection factory, transports, and consumer client factory.

## Headless.Messaging.RabbitMq

### API and behavior

- `setup.UseRabbitMq(...)`.
- Bus exchange and queue delivery.
- Consumer hatch: `Tune(identity, c => c.UseRabbitMq(rabbit => rabbit.PrefetchCount(...)))`.
- Consumer startup threads host cancellation through connection, channel, exchange, queue, and binding operations.
- Request/reply over an exclusive `headless.reply.{32 hex}` queue that each calling process declares on a connection of its own; the address changes after a connection loss. See [Request/reply](#requestreply).

### Design constraints

RabbitMQ exposes consumer-side QoS through `PrefetchCount(...)` on `Tune`; it has no message hatch. For base exchange `myapp.events`, Bus uses the `myapp.events.bus` topic exchange, `bus.{logical-name}` routing keys, and `bus.{consumer-identity}` queues; Queue uses the `myapp.events.queue` direct exchange, `queue.{logical-name}` routing keys, and `queue.{logical-name}` queues. When `PublishConfirms` is enabled, publish completion awaits the broker acknowledgement or negative acknowledgement. Malformed transport envelopes are terminally rejected without requeue while ordinary handler rejection remains retryable.

### Install

```bash
dotnet add package Headless.Messaging.RabbitMq
```

### Setup and use

```csharp
setup.UseRabbitMq(options =>
{
    options.HostName = "localhost";
    options.Port = 5672;
    options.UserName = "app_user"; // required
    options.Password = "app_secret"; // required
});

setup.Tune("orders.projection", consumer => consumer.UseRabbitMq(rabbit => rabbit.PrefetchCount(20)));
```

RabbitMQ declares independent Bus and Queue topology, so the same contract and logical name can carry Bus and Queue consumers without cross-delivery.

### Configuration

The current RabbitMQ exchange and binding topology does not provide the provider-neutral routing-affinity contract. `RequireRoutingAffinity()` fails during startup; a supplied `RoutingAffinityKey` is rejected before persistence or transport effects. Hash-exchange topology is not inferred or provisioned. Unkeyed routing remains unchanged.

Configure host, credentials, exchange, queue arguments, QoS defaults, and custom headers through `RabbitMqMessagingOptions`. `UserName` and `Password` are `required` and must be set explicitly; the validator rejects the RabbitMQ default `guest`/`guest` credentials for production safety.

### Runtime behavior

Registers RabbitMQ connection/channel pool, bus/queue transport, consumer client factory, and provider-specific config support.

## Headless.Messaging.Redis

### API and behavior

- `setup.UseRedis(...)`.
- One Bus copy per consumer identity: the Redis consumer group on each Bus stream is named after the identity, and replicas that register the identity compete inside it.
- One Queue copy per message: the Redis consumer group on the Queue stream is named after the message, and replicas compete inside it.
- Lane-qualified stream keys, acknowledgements, pending-entry claim, and terminal poison handling.
- A pending entry, read and not acknowledged, is claimed by another consumer of the group once it has been idle for `PendingClaimMinIdleTime` (60 s by default), so a crashed consumer's entries move on within a minute.
- A rejected delivery stays pending in its place in the stream and is delivered again by that claim; the stream does not grow and the entry keeps its position.
- Each publish trims, approximately, the entries older than `StreamMaxAge` (7 days by default) from its stream.
- Streams consumer startup honors host cancellation through connection, provisioning, and subscription.
- Request/reply over pub/sub on the literal channel `headless.reply.{32 hex}`; replies write no key, and every host that exchanges requests must use the same `ChannelPrefix`. See [Request/reply](#requestreply).

### Install

```bash
dotnet add package Headless.Messaging.Redis
```

### Setup and use

```csharp
using StackExchange.Redis;

setup.UseRedis(options => options.Configuration = ConfigurationOptions.Parse("localhost:6379"));
```

Redis physical keys are `headless:messaging:bus:{logical-name}` and `headless:messaging:queue:{logical-name}`. Both lanes use retained Streams with explicit Redis consumer-group ownership, which the provider creates on subscribe.

### Configuration

The current Redis Streams topology does not provide the provider-neutral routing-affinity contract. `RequireRoutingAffinity()` fails during startup; a supplied `RoutingAffinityKey` is rejected before persistence or transport effects. A stream name identifies a route, not a per-message affinity partition. No transparent stream sharding is added.

Configure Redis connection and Stream behavior through `RedisMessagingOptions`:

| Option | Default | Effect |
| --- | --- | --- |
| `StreamEntriesCount` | `100` | Entries one read takes from each stream. A read that returns a full batch is followed at once by the next read, so a backlog drains without waiting the poll interval per batch. |
| `PendingClaimMinIdleTime` | 60 s | How long an entry stays pending before another consumer claims it with `XAUTOCLAIM`. Must be positive. |
| `IdleConsumerDeleteAfter` | 1 hour | How long a consumer with no pending entries stays idle before it is deleted from its group. `TimeSpan.Zero` keeps every consumer. |
| `StreamMaxAge` | 7 days | Age past which each publish trims entries from its stream with `XADD MINID ~`. `TimeSpan.Zero` keeps entries without an age limit; otherwise it must exceed `PendingClaimMinIdleTime`. |
| `ConnectionPoolSize` | `10` | Multiplexers in the shared connection pool. |

Trade-offs and limits:

- **Pending window.** A durable consumer acknowledges an entry once the core admits it into the inbox, before its handler runs, so `PendingClaimMinIdleTime` bounds admission, not handler duration. A runtime subscription, which has no consumer identity, acknowledges after an inline handler returns; set the option above that handler's longest run. Keep the time the core takes to admit one batch of `StreamEntriesCount` entries well below it, or another consumer claims entries still waiting their turn and the inbox discards the duplicates.
- **Retention is not acknowledgement-aware.** Trimming removes an entry whether or not a group read or acknowledged it, so a group offline longer than `StreamMaxAge` misses the trimmed entries, and an entry that keeps failing admission is dropped once it is older than `StreamMaxAge`. The age is measured against the publishing process's clock. Approximate trimming removes whole internal nodes only, so entries can outlive the limit slightly; it never removes them early. No length cap (`MAXLEN`) is applied.
- **Every-instance reads.** A group-less every-instance reader that falls further behind than `StreamMaxAge` skips the trimmed entries.

### Runtime behavior

Registers Redis transports, consumers, and Redis connection services.

- **Consumer names.** A process reads each group as `{group}:{machine name}:{slot}`, where the slot is the lowest one no other live client of that group in the process holds. A process restarted on the same machine, such as a StatefulSet pod, reads under the names it used before, and its startup pass delivers the entries it left pending at once. A process whose machine name changes, such as a Deployment pod, leaves its pending entries to the claim after `PendingClaimMinIdleTime`. Two processes that share a machine name share consumer names: each one's startup pass then also redelivers the other's pending entries, and the inbox discards the duplicates.
- **Idle-consumer sweep.** Every `PendingClaimMinIdleTime`, each consumer client runs one Lua script per stream that deletes the group's consumers idle longer than `IdleConsumerDeleteAfter` with no pending entries. The check and the delete are atomic, because deleting a consumer discards its pending entries; a live consumer deleted while idle is created again by its next read. The sweep needs `EVAL`; where an ACL denies it, set `IdleConsumerDeleteAfter` to `TimeSpan.Zero`.
- **Wire format.** A stream entry has two fields: `headers`, the headers as a JSON object, and `body`, the message body as raw bytes.
- **Shutdown.** Shutdown waits, within the shutdown budget, for in-flight handlers to settle their entries before the client is disposed; an entry a handler never settles stays pending for the claim.

## Headless.Messaging.SourceGenerator

Roslyn incremental source generator that registers `[BusConsumer]` and `[QueueConsumer]` classes at compile time. `Headless.Messaging` carries it as an analyzer, so a project that references Core gets it without a separate package reference.

### API and behavior

- **Explicit registration**: the generated file (`MessagingModule.g.cs`) declares `<AssemblyName>.MessagingModule`, and nothing is registered until the module adds it with `AddModule<…MessagingModule>()` on `services.ConfigureMessaging(...)` or on the `AddHeadlessMessaging` setup. There is no module initializer and no runtime assembly scanning. Adding one module more than once registers it once. An assembly that declares no consumer gets no module.
- **One entry per message**: a consumer class registers one entry for every `IConsume<T>` and every `IRespond<TRequest, TResponse>` it implements, all with the attribute's identity, lane, `EveryInstance` flag, and a factory for its `FailurePolicy` type when it declares one. The factory is `static () => new TPolicy()`, so the runtime never creates a policy by reflection; the host builds it once per identity when it builds the consumer registry. A responder entry also carries its response type, which makes it a [request/reply](#requestreply) responder.
- **Typed dispatch**: each consumer class gets one generated factory and one generated dispatcher. The factory resolves the class from the delivery's scope with `GetService<T>()` and falls back to `ActivatorUtilities.CreateInstance<T>` when the container has no registration for it. The dispatcher builds the class through that factory, runs `IConsumerLifecycle` hooks when the class implements them, calls the `ConsumeAsync` or `RespondAsync` that matches the context's message type (explicit interface implementations included) and records a responder's return value for the reply, and disposes the instance only when the factory constructed it. An every-instance class that implements `IOnSubscriptionEstablished` also gets a generated hook call that builds the class through the same factory in its own scope. Dispatch and the hook use no reflection and no compiled expressions.
- **Responder metadata**: for each responder, the generated file also declares `[assembly: ResponderMetadataAttribute(typeof(TRequest), typeof(TResponse))]`. The generator in a referencing project reads it to check that project's `RequestAsync<TRequest, TResponse>` calls against the responder (HM013). The attribute is generated metadata; do not apply it by hand.
- **Incremental**: declarations are reduced to value models when discovered, so an edit that does not change a consumer declaration reuses every generator step and re-emits nothing. Referenced responder metadata is read only while the project calls `RequestAsync`.
- **Build-time checks**: HM001 to HM014, listed under [Diagnostics](#messaging-source-generator-diagnostics). `EveryInstance` exists only on `[BusConsumer]`, so writing it on `[QueueConsumer]` is a compiler error rather than a generator rule.

### Startup checks and host controls

- **Keys**: a consumer is keyed by its lane, identity, message name, and contract version, so one identity can cover several messages. On the Bus lane one identity is one subscription: the host starts one consumer client for it that binds all its messages. On the Queue lane the host starts one client per message.
- **Cross-module conflicts fail startup and name both sources**: one identity on two consumer classes in the same lane, and a second Queue consumer for one message. An identical declaration contributed twice registers once.
- **`Tune(identity, c => ...)`** on the `AddHeadlessMessaging` setup or on `services.ConfigureMessaging(...)` changes a registered consumer's deployment settings on this host: `Concurrency(n)` (`0` is rejected), `InboxRetention(TimeSpan)`, `CircuitBreaker(...)`, `FailurePolicy<TPolicy>()` or `FailurePolicy(p => ...)`, `UseMiddleware<TMiddleware>()`, and the provider consumer hatches `UseKafka(...)`, `UseNats(...)`, and `UseRabbitMq(...)`. Several `Tune` calls for one identity apply in registration order, so a later value wins and middleware accumulates. `InboxRetention`, `CircuitBreaker`, or `FailurePolicy` on an every-instance consumer fails startup. Tuned middleware implements `IConsumeMiddleware<ConsumeContext>`, runs only for that consumer, runs inside the global and per-message middleware, and is resolved from the delivery scope (registered as scoped when it is not already registered). A consumer that handles several messages takes the settings for each of them. An identity that no registered consumer declares fails startup.
- **Configuration**: `Headless:Messaging:Consumers:{identity}` binds `Concurrency` (1 to 255), `InboxRetention` (a `TimeSpan` such as `30.00:00:00`), and `CircuitBreaker:Enabled`, `CircuitBreaker:FailureThreshold`, and `CircuitBreaker:OpenDuration`, and `FailurePolicy:ImmediateRetries`, `FailurePolicy:DelayedRetries`, `FailurePolicy:DelayedInitialDelay`, and `FailurePolicy:DelayedMaxDelay`, and applies after every `Tune` call and after the host default fills in. An unknown identity, an unknown setting, or an invalid value fails startup.
- **`ConsumeOnly("orders.*", "billing.invoice-projection")`** on the `AddHeadlessMessaging` setup limits which competing consumers this host starts clients for; every-instance consumers are never filtered and always run. An entry is an exact identity or an `owner.*` pattern that matches the first identity segment. Consumers outside the filter stay registered, so the host still publishes their messages; another host consumes them. The filter also scopes the host's retry processor: its received-retry and inbox-orphan pickup queries never lease a row whose consumer identity is outside the filter, so the host never marks another host's healthy inbox row as an orphan or fails its retry as having no subscriber. Runtime subscriptions are never filtered: the pickups also include the identity of every competing runtime subscription attached to the host when the cycle runs, so the host that holds the delegate retries that subscription's failed rows. Every-instance consumers and subscriptions store no rows and add nothing. A host without the filter picks up rows of every identity, including identities no host registers. An entry that matches no registered consumer, or only every-instance consumers (the filter would not affect them), fails startup.

### Diagnostics

<a id="messaging-source-generator-diagnostics"></a>Every rule is reported at compile time in category `Headless.Messaging.SourceGenerator`. HM006 and HM014 are warnings; every other rule is an error. The table below is each rule's help link target.

| Rule | Reported when | Fix |
| --- | --- | --- |
| <a id="hm001"></a>HM001 | The identity is not a compile-time constant, or is not in `owner.name` form: it is empty, longer than 200 characters, has no `.` separator or an empty segment, has surrounding white space, or contains a control character. | Pass a literal or `const` identity such as `"billing.invoice-projection"`, whose first segment names the owning module. |
| <a id="hm002"></a>HM002 | Two consumer classes in one compilation use the same identity on the same lane. Nothing is generated. | Give each consumer class its own identity; one class covers several messages by implementing several `IConsume<T>`. |
| <a id="hm003"></a>HM003 | The class carries a consumer attribute but implements neither `IConsume<T>` nor `IRespond<TRequest, TResponse>`. | Implement `IConsume<T>` for every message the consumer handles, or `IRespond<TRequest, TResponse>` for every request a `[QueueConsumer]` answers. |
| <a id="hm004"></a>HM004 | A second `[QueueConsumer]` class in one compilation consumes or responds to a message that already has a Queue consumer or responder. A responder counts as its request's Queue consumer. Nothing is generated. | Keep one Queue consumer or responder per message; use `[BusConsumer]` for fan-out. |
| <a id="hm005"></a>HM005 | The `FailurePolicy` type does not derive from `Headless.Reliability.FailurePolicy`, is not a class, is abstract, still has an open type parameter (its own or a containing type's), is private, protected, or `file`-local, or has no public parameterless constructor, so the generated factory cannot construct it. A closed generic such as `RetryTwice<Payments>` is accepted. Nothing is generated. | Point `FailurePolicy` at a concrete, closed `public` or `internal` class derived from `FailurePolicy` with a public parameterless constructor. |
| <a id="hm006"></a>HM006 | A consumer that is not an every-instance `[BusConsumer]` implements `IOnSubscriptionEstablished`, whose hook never runs for it. | Set `EveryInstance = true` on a `[BusConsumer]`, or remove the interface. |
| <a id="hm007"></a>HM007 | The consumer class, a type containing it, or a consumed message type is private, protected, or `file`-local, so generated code cannot name it. | Make the type and every type containing it `public` or `internal`. |
| <a id="hm008"></a>HM008 | The consumer class is abstract or generic, or nested in a generic type, so a delivery cannot construct it. | Put the attribute on a concrete, non-generic class. |
| <a id="hm009"></a>HM009 | One class carries both `[BusConsumer]` and `[QueueConsumer]`. Nothing is generated. | Keep one lane attribute; split the class when it must consume on both lanes. |
| <a id="hm010"></a>HM010 | A `[BusConsumer]` with `EveryInstance = true` declares a `FailurePolicy`. Every-instance deliveries are at most once and never stored, so no policy can run. Nothing is generated. | Remove `FailurePolicy`, or make the consumer competing by removing `EveryInstance = true`. |
| <a id="hm011"></a>HM011 | A class that implements `IRespond<TRequest, TResponse>` carries `[BusConsumer]`. A reply goes to one caller, and only the Queue lane gives a request exactly one consumer. | Put `[QueueConsumer]` on the responder. |
| <a id="hm012"></a>HM012 | One class implements both `IConsume<T>` and `IRespond<T, TResponse>` for the same `T`. | Implement only `IRespond<T, TResponse>`; a responder that a plain enqueue reaches runs and discards its result. |
| <a id="hm013"></a>HM013 | One class implements `IRespond<TRequest, TResponse>` for the same request with two or more response types. | Answer each request with one response type; put alternatives in one response contract. |
| <a id="hm014"></a>HM014 | A `RequestAsync<TRequest, TResponse>` call expects a `TResponse` that no responder visible to the project answers `TRequest` with. A responder is visible when it is declared in the calling project or in a referenced assembly built with this generator. Without the fix, the call fails with `ResponseContractMismatchException`. | Request the responder's response type. When the call deliberately reads the reply into another type registered under the same message contract name and version, suppress the warning at the call with a reason. |

### Install

```bash
dotnet add package Headless.Messaging.SourceGenerator
```

## Headless.Messaging.Storage.PostgreSql

### API and behavior

- `IMessageRevocationStorage` atomically deletes a scheduled row before reservation, fenced by storage version, terminal status, and retry state. Claimed but unreserved rows remain revocable; deleted rows cannot be restored by reservation or shutdown flush.
- `setup.UsePostgreSql(...)` — connection string, `IConfiguration` binding, `Action<PostgreSqlOptions>`, or `Action<PostgreSqlOptions, IServiceProvider>`. The parameterless `setup.UsePostgreSql()` reads the connection registered by `AddPostgreSqlSql`.
- Validates the feature-owned `MessagingStorageOptions.Schema` against PostgreSQL identifier rules at startup.
- Raw ADO.NET integration and startup initialization.
- Declares `InboxGuarantee.Durable`; durable consumers can require `Durable` or `ProcessLocal`. The default `Transactional` requirement is rejected unless the configured provider declares that stronger guarantee.
- **GUID Row IDs**: Message storage identifiers come from the `Version7` keyed `IGuidGenerator` and are persisted as PostgreSQL `UUID` columns.

Fresh dispatch, retry pickup, and delayed scheduling atomically compare due time and lease expiry and stamp ownership from one PostgreSQL clock snapshot (`clock_timestamp()`, read once per statement, after the row's lock is held). Retry pickup and the delayed claim each run on their own READ COMMITTED transaction and retry a transient fault raised before the commit (a deadlock, a serialization failure, a dropped connection), never a fault from the commit. Delayed scheduling uses ordered `FOR UPDATE SKIP LOCKED` claiming, commits the transition to `Queued`, and only then returns winner messages for local enqueue. Circuit-open received retries atomically advance `NextRetryAt` and clear only the exact live `(lane, Owner, LockedUntil)` lease generation using PostgreSQL's authoritative clock and null-safe owner matching.

### Install

```bash
dotnet add package Headless.Messaging.Storage.PostgreSql
```

### Setup and use

```csharp
setup.UsePostgreSql(builder.Configuration.GetConnectionString("Messaging")!);
// or reuse the connection from services.AddPostgreSqlSql(connectionString):
setup.UsePostgreSql();
```

The parameterless overload and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

### Configuration

Configure the connection string and provider-specific storage options through `PostgreSqlOptions`. The schema is **not** one of them — it belongs to the feature, on `MessagingStorageOptions` (see [Storage schema](#storage-schema)).

Known orphans use a separate bounded probe batch and recover only when the exact consumer identity, logical contract name/version, and lane return. They do not expire automatically. Unclaimed orphans permit Hold/ReleaseHold and unheld Purge; live claims block those actions, and ForceReprocess remains terminal-only. A hold protects retention and purge but does not stop recovery.

History retention uses the shared `MessagingOptions` defaults: cleanup receipts/audits 7 days each, operator receipts 30 days, and operator audits 90 days. All four are positive configurable minimum residence durations. Audit references can extend receipt lifetime; deleting history does not release holds. PostgreSQL database time controls history age. The schema steps create the history-selection and audit-reference indexes. This history also covers scheduled-delivery operator actions (revoke, dispatch-now) under the shared `TargetKind` ledger; the schema is created fresh with the generalized ledger columns (greenfield — no prior schema versions exist), and the runner's `headless_schema_history` rows for `Messaging` pin the schema contract. See [Core configuration](#configuration-3) for probe settings, replay limits, rollout effects, and collector pacing.

- **Schema steps**: `Messaging/1` creates the tables, constraints, and key indexes and asserts that a pre-existing table has the expected shape; `Messaging/2` creates the retry-pickup, owner, and history indexes; `Messaging/3` installs `pg_trgm` and the trigram indexes. The [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) applies them under its one lock per database. Indexes are built with plain `CREATE INDEX`, not `CONCURRENTLY`, so a build on a large existing table blocks writes to it until it finishes; `SchemaRunnerOptions.CommandTimeout` (default 10 minutes) bounds each statement.
- **`pg_trgm` on managed PostgreSQL**: dashboard content (ILIKE) search uses GIN trigram indexes that need the `pg_trgm` extension. Step `Messaging/3` tries `CREATE EXTENSION IF NOT EXISTS pg_trgm` inside a guarded block. On managed PostgreSQL (AWS RDS, Azure, Neon, Supabase) the app role usually lacks `CREATE EXTENSION`; the step raises a warning, **skips the trigram content indexes**, and still records itself — write/retry paths are unaffected, only dashboard content search is disabled. Because the step is recorded, installing `pg_trgm` later does not add the indexes on the next start: have the DBA install the extension **before** the first start, or create the two trigram indexes by hand afterwards.
- **Bootstrap indexes**: fresh schemas directly create `(status_name, added)` indexes for dashboard timelines/statistics and a partial `(version, expires_at) WHERE status_name = 'Queued'` index for delayed-message scheduling. The steps create the final schema shape; a future shape change is a new step.

### Runtime behavior

Registers PostgreSQL storage, the monitoring API, table-name resolution, and the messaging schema contribution. It does not register EF Core or `Headless.UnitOfWork`.

## Headless.Messaging.Storage.PostgreSql.EntityFramework

Adds `setup.UseEntityFramework<TContext>()` for PostgreSQL, derives the connection from the registered context, and registers `Headless.UnitOfWork` (`AddUnitOfWork()`); there is no startup gate. Depends on the raw PostgreSQL storage package; install it only for EF-backed transactional outbox composition.

The storage connects the way the context connects. When the context holds an Npgsql data source (one passed to `UseNpgsql`, one registered in DI, or one EF builds for `ConfigureDataSource` or a plugin such as NetTopologySuite), the storage shares it, with its password provider and other customizations; the context's connection string would omit the password. Otherwise it uses the context's connection string. A context that is not on Npgsql fails at startup.

Each transactional consume attempt shares one DI scope and configured `TContext` across the EF runner, consume middleware, and handler. The runner saves tracked changes after the handler returns and keeps the scope alive through commit or rollback. Explicit handler saves and rows published through `context.UnitOfWork.Outbox` roll back with application state when inbox completion rejects the attempt fence; an `IBus`/`IQueue` publish from the handler is outside the transaction and does not roll back.

EF execution-strategy retries are allowed only before handler entry. After entry, handler, save, commit, rollback, and disposal failures return to Messaging's fenced retry path; EF cannot transparently replay the handler within the reserved attempt. Ambiguous commit outcomes are still probed before deciding whether the attempt committed.

## Headless.Messaging.Storage.SqlServer

### API and behavior

- `IMessageRevocationStorage` atomically deletes a scheduled row before reservation, fenced by storage version, terminal status, and retry state. Claimed but unreserved rows remain revocable; deleted rows cannot be restored by reservation or shutdown flush.
- `setup.UseSqlServer(...)` — connection string, `IConfiguration` binding, `Action<SqlServerOptions>`, or `Action<SqlServerOptions, IServiceProvider>`. The parameterless `setup.UseSqlServer()` reads the connection registered by `AddSqlServerSql`.
- Validates the feature-owned `MessagingStorageOptions.Schema` against SQL Server identifier rules at startup.
- Raw ADO.NET integration and startup initialization.
- Declares `InboxGuarantee.Durable`; durable consumers can require `Durable` or `ProcessLocal`. The default `Transactional` requirement is rejected unless the configured provider declares that stronger guarantee.
- **GUID Row IDs**: Message storage identifiers come from the `SqlServer` keyed `IGuidGenerator` and are persisted as SQL Server `uniqueidentifier` columns.

Fresh dispatch, retry pickup, and delayed scheduling atomically compare due time and lease expiry and stamp ownership from one SQL Server clock snapshot, read after the row's lock is held. Retry pickup, the delayed claim, and the published-row purge each run on their own READ COMMITTED transaction, whatever isolation level a pooled session last used, and retry a transient fault raised before the commit (a deadlock, a lock timeout, a dropped connection), never a fault from the commit. Delayed scheduling uses ordered `UPDLOCK, READPAST` claiming, commits the transition to `Queued`, and only then returns winner messages for local enqueue. Circuit-open received retries atomically advance `NextRetryAt` and clear only the exact live `(lane, Owner, LockedUntil)` lease generation using SQL Server's authoritative clock and null-safe owner matching.

### Install

```bash
dotnet add package Headless.Messaging.Storage.SqlServer
```

### Setup and use

```csharp
setup.UseSqlServer(builder.Configuration.GetConnectionString("Messaging")!);
// or reuse the connection from services.AddSqlServerSql(connectionString):
setup.UseSqlServer();
```

The parameterless overload and the shared `headless` schema are described in [sql.md § Shared connection and schema for storage features](sql.md#shared-connection-and-schema-for-storage-features).

### Configuration

Configure the connection string and provider-specific storage options through `SqlServerOptions`. The schema is **not** one of them — it belongs to the feature, on `MessagingStorageOptions` (see [Storage schema](#storage-schema)).

Known orphans use a separate bounded probe batch and recover only when the exact consumer identity, logical contract name/version, and lane return. They do not expire automatically. Unclaimed orphans permit Hold/ReleaseHold and unheld Purge; live claims block those actions, and ForceReprocess remains terminal-only. A hold protects retention and purge but does not stop recovery.

History retention uses the shared `MessagingOptions` defaults: cleanup receipts/audits 7 days each, operator receipts 30 days, and operator audits 90 days. All four are positive configurable minimum residence durations. Audit references can extend receipt lifetime; deleting history does not release holds. SQL Server database time controls history age. The schema steps create the history-selection and audit-reference indexes. This history also covers scheduled-delivery operator actions (revoke, dispatch-now) under the shared `TargetKind` ledger; the schema is created fresh with the generalized ledger columns (greenfield — no prior schema versions exist), and the runner's `headless_schema_history` rows for `Messaging` pin the schema contract. See [Core configuration](#configuration-3) for probe settings, replay limits, rollout effects, and collector pacing.

Fresh schemas directly create `([StatusName],[Added])` indexes for dashboard timelines/statistics. The [schema runner](sql.md#schema-runner-apply-verify-and-deploy-time-scripts) applies two steps: `Messaging/1` (tables, constraints, indexes, and a shape assertion) and `Messaging/2` (history indexes).

- **DDL timeout**: the history-index builds are bounded by `SchemaRunnerOptions.CommandTimeout` (default 10 minutes), not the OLTP `MessagingOptions.CommandTimeout`. The builds run offline, since `ONLINE = ON` depends on the edition, and block writes to that history table until they finish. Peer replicas wait on the runner's lock instead of failing.

### Runtime behavior

Registers SQL Server storage, the monitoring API, table-name resolution, and the messaging schema contribution. It does not register EF Core or `Headless.UnitOfWork`.

- **Monitoring skips locked rows**: dashboard counts, pages, and timelines read with `READPAST`, so a row another transaction holds locked (a claim, an admission, a state change in flight) is left out of that read instead of blocking it. PostgreSQL's monitoring reads instead see the row's last committed version. Counts can therefore dip briefly under load on SQL Server.
- **Inbox operation lock**: concurrent requests of one inbox operation (hold, release, purge, force-reprocess) that share an operation id serialize on a transaction-scoped `sp_getapplock` named `headless.messaging.inbox.operation.{operationId}`, so one applies and the others replay its receipt. The lock has no timeout of its own, so the wait is bounded by `MessagingOptions.CommandTimeout`, and a timed-out request can be retried with the same operation id.
- **Admission under READ COMMITTED**: duplicate admission is prevented by the unique index on the inbox key hash, not by the isolation level. Concurrent admissions of one key yield one `Winner`, the rest `InFlightDuplicate`, and one row.

## Headless.Messaging.Storage.SqlServer.EntityFramework

Adds `setup.UseEntityFramework<TContext>()` for SQL Server, derives the connection from the registered context, and registers `Headless.UnitOfWork` (`AddUnitOfWork()`); there is no startup gate. Depends on the raw SQL Server storage package; install it only for EF-backed transactional outbox composition.

The storage uses the context's connection string, checked against the context's connection by `GetReusableConnectionString` (see [sql.md](sql.md#headlesssqlsqlserver)). A connection that authenticates with credentials no connection string carries (an access token, an access-token callback, a `SqlCredential`, or a password SqlClient removed when an opened connection was handed to `UseSqlServer`) fails at startup with an error naming the cause; give such a host's storage its own connection string through the raw `UseSqlServer(...)`. A token an interceptor attaches while the connection opens is invisible to that check. A context that is not on SqlClient fails at startup.

Each transactional consume attempt shares one DI scope and configured `TContext` across the EF runner, consume middleware, and handler. The runner saves tracked changes after the handler returns and keeps the scope alive through commit or rollback. Explicit handler saves and rows published through `context.UnitOfWork.Outbox` roll back with application state when inbox completion rejects the attempt fence; an `IBus`/`IQueue` publish from the handler is outside the transaction and does not roll back.

EF execution-strategy retries are allowed only before handler entry. After entry, handler, save, commit, rollback, and disposal failures return to Messaging's fenced retry path; EF cannot transparently replay the handler within the reserved attempt. Ambiguous commit outcomes are still probed before deciding whether the attempt committed.

## Headless.Messaging.Testing

### API and behavior

- `MessagingTestHarness` records messages at the bus/queue transport layer.
- `WaitForPublishedAsync<T>(...)`, `WaitForConsumedAsync<T>(...)`, `WaitForFaultedAsync<T>(...)`, and `WaitForExhaustedAsync<T>(...)` block until a match arrives or the timeout elapses.
- Consumers under test register the same way as in production: attribute-declared classes and `AddModule<…MessagingModule>()`. `WaitForPublishedAsync<T>(MessageLane.Bus)` / `MessageLane.Queue` distinguishes identical payloads sent through the two lanes.
- Predicate overloads for filtering by payload shape.
- Store-first by default: the harness keeps the production `DeliveryMode.Durable` default, so a plain publish is stored first and dispatched from storage; `RecordedMessage.RequestedDeliveryMode` / `ResolvedDeliveryMode` report what was asked for and what ran.
- `RunInUnitOfWorkAsync(...)` creates a service scope, begins a resource-less unit of work on it, and hands the delegate **both** that scope's `IServiceProvider` and the `IUnitOfWork` itself — `Func<IServiceProvider, IUnitOfWork, Task>`, plus a `Task<TResult>` overload. Publish through the unit's `Outbox` inside the delegate to exercise an enlisted publish against a live commit or rollback.
- `ResetAsync()` drains in-flight publish and consume work before clearing a shared harness.
- `TestConsumer<T>` captures consume contexts without custom handler logic. It is generic and carries no consumer attribute, so the generator cannot register it; attach it as a runtime subscription, for example `subscriber.SubscribeAsync<OrderPlaced>((context, _, ct) => testConsumer.ConsumeAsync(context, ct), new RuntimeSubscriptionOptions { HandlerId = "tests.order-placed" })`.

### Design constraints

Use the testing package for application tests that need to assert published messages or consumed messages. Provider conformance still belongs in provider-specific or shared harness tests.

The harness does not weaken delivery: `MessagingOptions.DefaultDeliveryMode` stays `Durable`, so `PublishAsync` returns once the row is in in-memory storage and the transport send, the `Published` observation, and consumption follow on dispatcher threads. Assert through `WaitFor*` rather than reading the collections right after a publish. `ResetAsync()` waits until no published row is `Scheduled`/`Queued` and no received row is `Scheduled` (those states bracket every send and consumer execution), drops transport messages no consumer picked up, and only then clears observations and storage; a publish delayed by more than a minute is not awaited and still fires when due. `RunInUnitOfWorkAsync` begins the unit of work with `IUnitOfWorkFactory.BeginAsync()` (resource-less) and hands it to the delegate; in-memory storage is the one storage that can join a resource-less unit (see [Delivery Modes](#delivery-modes)), so `unit.Outbox` publishes made inside the delegate are captured on the unit. Completion stores the captured rows and hands them to the dispatcher, so they surface through `WaitForPublishedAsync`/`WaitForConsumedAsync`; an exception from the delegate rolls the unit back, discards the rows, and records nothing. Every call opens an independent scope and unit — a nested call is a second unit, not a participant in the outer one. The recorded `ResolvedDeliveryMode` is `Durable` either way; enlistment shows up in `RecordedMessage.IsCoordinated`, not in the mode.

Two consequences of running on in-memory storage. First, the in-memory transport hands a message to its consumer inside the send, before the sending thread records `Published`; the harness's consume decorator therefore waits for the message's `Published` record before running the consumer, so for any one message `Published` is always observable before `Consumed` or `Faulted`, and `harness.Published` is safe to read after `WaitForConsumedAsync`. Second, `harness.Publisher`, `harness.Queue`, and `harness.GetRequiredService<T>()` resolve from a harness-owned scope, and `IBus`/`IQueue` are autonomous singletons in any case, so publishes through them are always standalone durable writes that survive a rollback — including inside the `RunInUnitOfWorkAsync` delegate. Enlistment in a test comes from the unit handed to that delegate and nothing else.

### Install

```bash
dotnet add package Headless.Messaging.Testing
```

### Setup and use

```csharp
using AwesomeAssertions;

services.AddMessagingTestHarness();

var harness = provider.GetRequiredService<MessagingTestHarness>();
await harness.WaitForPublishedAsync<OrderPlaced>(TimeSpan.FromSeconds(5));

// Shared harness: wait for in-flight store-first work, then clear observations and storage.
await harness.ResetAsync();

// Enlisted publish: completing the unit of work dispatches the captured message;
// a throwing delegate rolls it back and nothing is recorded.
await harness.RunInUnitOfWorkAsync(async (sp, unit) =>
{
    await unit.Outbox.PublishAsync(new OrderPlaced(Guid.NewGuid()));
});

var recorded = await harness.WaitForPublishedAsync<OrderPlaced>(TimeSpan.FromSeconds(5));
recorded.ResolvedDeliveryMode.Should().Be(DeliveryMode.Durable);
recorded.IsCoordinated.Should().BeTrue();
```

### Configuration

None. `MessagingTestHarness` has no configuration class or options object. The per-call `timeout` parameter controls how long `WaitFor*` methods and `ResetAsync` wait; when omitted it defaults to `MessagingTestHarness.DefaultTimeout`.

### Runtime behavior

- `CreateAsync(...)` builds and owns a test `ServiceProvider`; dispose the harness after each test.
- `AddMessagingTestHarness()` decorates the host's existing messaging registrations with recording wrappers; call it after `AddHeadlessMessaging(...)`.
- Both entry points call `services.AddUnitOfWork()` (idempotent), so the host always resolves a real `IUnitOfWorkFactory`.
- Transport parallelism is disabled inside the harness for deterministic test execution.
