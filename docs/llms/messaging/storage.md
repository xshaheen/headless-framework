---
domain: Messaging
packages: Messaging.Storage.InMemory, Messaging.Storage.PostgreSql, Messaging.Storage.PostgreSql.EntityFramework, Messaging.Storage.SqlServer, Messaging.Storage.SqlServer.EntityFramework
---

# Storage

> Part of [Messaging](../messaging.md). Choose the storage that holds the outbox, inbox, and retry state, and wire its schema, transactional inbox, and additional outboxes.

## Choosing storage

A host has exactly one primary storage. Zero or several fail startup.

| Storage | Use when | Inbox guarantee | Schema |
| --- | --- | --- | --- |
| `UsePostgreSqlEntityFramework<TContext>()` / `UseSqlServerEntityFramework<TContext>()` | The app uses EF Core and handlers write through the `DbContext` | `Transactional` | Schema runner steps |
| `UsePostgreSql(...)` / `UseSqlServer(...)` | No EF Core, or handlers write with ADO.NET or Dapper | `Transactional` (default), or `Durable` with `EnableTransactionalInbox = false` | Schema runner steps |
| `UseInMemoryStorage()` | Development and tests only | `ProcessLocal` | None; lost on restart |

`MessagingOptions.MinimumInboxGuarantee` (default `Transactional`) is what durable consumers require from the storage. The guarantees are ordered `ProcessLocal` < `Durable` < `Transactional`. A storage that declares a weaker guarantee fails startup before any subscription starts. Lowering the requirement is an explicit opt-down: `Durable` lets duplicate suppression commit separately from application state, and `ProcessLocal` is only for in-memory development storage.

## Transactional inbox

Under the `Transactional` guarantee, each consume attempt runs in one database transaction that commits the inbox outcome, the handler's own writes, and the messages the handler publishes through its unit of work, or rolls all of them back.

- **One scope per attempt.** The inbox runner, consume middleware, and handler share one DI scope. A later attempt, an immediate retry included, gets a fresh scope.
- **EF storages** keep the scoped `TContext` alive through commit or rollback. The runner saves tracked changes after the handler returns, and explicit handler saves roll back if the inbox completion rejects the attempt.
- **Raw storages** open one connection per attempt; the handler writes through the unit. See [Raw storage handlers](#raw-storage-handlers).
- **Size the connection pool** for the consumer's parallelism: each attempt holds one pooled connection and an open transaction while its handler runs.
- **It does not make everything exactly once.** Handler entry, `Direct` publishes, and writes outside the unit can still repeat.

### Publish from a consumer

Publish through the attempt's unit, so the outgoing row commits with the inbox row and a failed attempt does not send it:

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

- `IBus` and `IQueue` never join that unit. An `IBus` publish from a handler that then fails is not rolled back, and the retried attempt publishes it again.
- `ConsumeContext.GetRequiredUnitOfWork()` returns the unit on every attempt of a durable consumer under the `Transactional` guarantee. Otherwise it throws `InvalidOperationException` naming the setting that removed the unit, so a consumer that depends on atomic publishing fails on its first message.
- `ConsumeContext.UnitOfWork` is `null` under a weaker guarantee and for an every-instance consumer. A handler that needs an atomic publish there opens its own unit of work.
- Jobs enlist the same way: `context.GetRequiredUnitOfWork().Jobs.ScheduleAsync(...)`. A consumer that also holds the inbox's `DbContext` reaches the unit as `db.UnitOfWork()`.

### Failures around the transaction

- **Before the handler runs** (opening the connection, a pool timeout, beginning the transaction, enlisting), the attempt does not spend the retry budget. EF execution strategies may retry this setup first; the raw storages do not. See [retries.md § Consumer failure policy](retries.md#consumer-failure-policy) for `RetryPolicy.MaxPreEntryFailures`.
- **After the handler entered**, a failure in the handler, save, commit, rollback, or disposal goes to Messaging's retry path. EF never replays the handler inside one attempt.
- **An ambiguous commit** is probed on every relational storage, so a commit that did land is recognized.
- **A cancelled storage call** throws `OperationCanceledException` on every relational storage, including when the cancel interrupts a statement running on SQL Server, inside your transaction or on the storage's own connection. The driver's fault is its `InnerException`. Catch `OperationCanceledException`, not `SqlException`, to detect a cancel.

## Raw storage handlers

On `UsePostgreSql(...)` and `UseSqlServer(...)`, each durable attempt opens its own connection (through `PostgreSqlOptions.DataSource` when one is set), begins a `ReadCommitted` transaction, and hands the handler a unit of work over it. A handler's writes commit or roll back with the inbox row only when they run on that connection and transaction. Reach them through the unit:

```csharp
using System.Data.Common;
using Headless.UnitOfWork;

[BusConsumer("orders.projection")]
public sealed class OrderProjectionConsumer : IConsume<OrderPlaced>
{
    public async ValueTask ConsumeAsync(ConsumeContext<OrderPlaced> context, CancellationToken cancellationToken)
    {
        var unitOfWork = context.GetRequiredUnitOfWork();
        var transaction = UnitOfWorkTransactions.RequireTransaction<DbTransaction>(unitOfWork, "order projection");

        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO order_projection (order_id) VALUES (@orderId)";
        var orderId = command.CreateParameter();
        orderId.ParameterName = "@orderId";
        orderId.Value = context.Message.OrderId;
        command.Parameters.Add(orderId);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await unitOfWork.Outbox.PublishAsync(context.Message, cancellationToken);
    }
}
```

- **Dapper** works the same way: pass `transaction.Connection` and `transaction` to the call.
- **Code handed only the connection** reaches the attempt's unit with `connection.UnitOfWork()`. The attempt's connection is not registered as a service.
- **A write through any other connection**, such as a repository that opens its own, commits on its own. A retried attempt writes it again.
- **Turn the transactional inbox off** with `PostgreSqlOptions.EnableTransactionalInbox` or `SqlServerOptions.EnableTransactionalInbox = false` (default `true`, bindable from configuration) when handlers never write through the unit and should not hold a connection while they run. The storage then declares `InboxGuarantee.Durable`, consumers get a `null` `ConsumeContext.UnitOfWork`, and durable consumers need `MinimumInboxGuarantee = InboxGuarantee.Durable` to start. An additional outbox ignores the switch.

## Storage schema

Messaging owns its database naming. Set the schema once, on `MessagingStorageOptions`, through the setup builder. The provider options have no `Schema`.

```csharp
services.AddHeadlessMessaging(setup =>
{
    // ... transport registration ...
    setup.ConfigureStorage(storage => storage.Schema = "outbox"); // default: "headless"
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("Messaging")!);
});
```

- **Default** `headless`, shared by every Headless feature; see [sql.md § Shared connection and schema](../sql.md#shared-connection-and-schema-for-storage-features).
- **Validation.** The registered storage checks the value at startup against its dialect's identifier rules (63 characters on PostgreSQL, 128 on SQL Server). An invalid schema fails startup with `OptionsValidationException`.
- **Configuration binding.** `setup.ConfigureStorage(builder.Configuration.GetSection("Headless:Messaging:Storage"))` binds it. Pass the section, not the root. Provider `Use...(IConfiguration)` overloads never bind the schema. Both `ConfigureStorage` overloads apply in call order, so the last wins.
- **EF storages** take no schema of their own; pair them with `ConfigureStorage` when the tables are not in `headless`.
- **Upgrade before the new build starts.** A build that adds a schema step fails its host start, naming the step, until `resources provision` (or `deploy`) applies it. Run it before the new build starts. The correlation step adds `correlation_id` and `causation_id` columns, and an index on the correlation id, to the published and received tables. A row stored before the step has null columns, so the correlation id search and `IMonitoringApi.GetCorrelatedMessagesAsync` do not find it.
- **Create the tables at deploy time** with `resources provision` (or `deploy`), or with the script from `SchemaRunner.ExportScript`. A host start only verifies them and fails when a step is missing. A development or test host calls `AddDeployStepsOnStartup()`. See [sql.md § Schema runner](../sql.md#schema-runner-apply-verify-and-deploy-time-scripts). A process that never starts the host (a console tool calling `IBootstrapper.BootstrapAsync`) applies the steps itself: `await provider.GetRequiredService<SchemaRunner>().ApplyAsync()`.

Table names are fixed inside the schema:

| PostgreSQL | SQL Server | Holds |
| --- | --- | --- |
| `messaging_published` | `MessagingPublished` | Outbox rows |
| `messaging_received` | `MessagingReceived` | Inbox rows |
| `messaging_inbox_operation_receipts` | `MessagingInboxOperationReceipts` | Operator and cleanup receipts |
| `messaging_inbox_audit` | `MessagingInboxAudit` | Operator and cleanup audit |

Columns and indexes are snake_case on PostgreSQL (`status_name`, `next_retry_at`) and PascalCase on SQL Server (`StatusName`, `NextRetryAt`). Raw SQL against the PostgreSQL tables needs no identifier quoting. A received row belongs to one consumer: both providers key it by message id plus `consumer_identity` / `ConsumerIdentity`, so a message delivered to two consumers has one row per consumer.

Row ids are `Guid`s generated in the application: UUIDv7 (`UUID` columns) on PostgreSQL and a sequential comb `uniqueidentifier` on SQL Server. `MediumMessage.StorageId`, the monitoring API, and dashboard routes use them.

**Verify:** run `resources provision`, or start the host and check the `messaging-storage-postgresql-default` / `messaging-storage-sqlserver-default` readiness check; see [operations.md § Health checks](operations.md#health-checks).

## Additional outboxes

When units of work run on other databases too, for example one `DbContext` per module, register an additional outbox for each so a unit on that database can publish atomically through `unit.Outbox`. Do not register a second primary storage.

```csharp
services.AddHeadlessMessaging(setup =>
{
    setup.UsePostgreSqlEntityFramework<OrdersDb>(); // primary: outbox, inbox, retry state, dashboard
    setup.AddOutbox("billing").UsePostgreSqlEntityFramework<BillingDb>(); // additional: outbox rows and their relay only
    setup.AddOutbox("shipping").UsePostgreSql(builder.Configuration.GetConnectionString("Shipping")!);
});
```

- **Name.** Letters, digits, `-`, and `_`, unique in the host ignoring case. The primary is always `primary` (`MessagingSetupBuilder.PrimaryOutboxName`), so `AddOutbox("primary")`, a duplicate, or another character throws `ArgumentException`. The name appears in logs, health checks, the host manifest, and dashboard ids. Choose it once and keep it.
- **Registration.** `AddOutbox(name)` returns an `OutboxStorageBuilder` that accepts one storage: `UsePostgreSqlEntityFramework<TContext>()`, `UseSqlServerEntityFramework<TContext>()`, `UsePostgreSql(...)`, or `UseSqlServer(...)`, with the same overloads as the primary. `AddOutbox(name)` without a storage, or with two, throws during `AddHeadlessMessaging`.
- **Selection.** `unit.Outbox` writes to the primary when the unit's database is the primary's, otherwise to the outbox whose database matches. A unit that matches none is refused with the `Database` mismatch. `IBus` and `IQueue` always write to the primary.
- **Startup validation.** The primary must be relational, because an in-memory primary joins every unit. Two outboxes, the primary included, on one database fail startup with `MessagingConfigurationException` naming both. Mixing providers is allowed.
- **Schema.** Each additional outbox has only the published table, in the same schema, recorded as its own `MessagingOutbox` feature in that database's `headless_schema_history`. `resources provision` creates it; a database that is down fails that one resource and the run goes on. `SchemaRunner.ExportScript` emits one script per dialect and schema: run it against the primary and every outbox database of that dialect.
- **Startup availability.** Only the primary must be reachable. An unreachable or misconfigured additional outbox logs EventId 106 and the host starts without it. A background retry runs with jittered backoff from 1 to 30 seconds (EventId 107 per failure, 108 once initialized). Until then its relay skips it, and a `unit.Outbox` publish on its database first tries to initialize it; a failure throws and the unit rolls back. The `messaging-outboxes` readiness check reports `Degraded` meanwhile.
- **Relay.** Each outbox relays, retries, expires, and recovers its own rows, and backs off alone when its database is down. `IMessageRevoker.RevokeAsync` finds a scheduled row in whichever outbox holds it.
- **What stays on the primary.** The inbox, received retry state, and the operator ledger. The dashboard's Messaging module reads every outbox; see [dashboards.md](../dashboards/messaging-module.md).
- **Jobs** keeps one store. To schedule a job atomically with a unit on an outbox database, publish through `unit.Outbox` and schedule the job from that message's consumer with `unit.Jobs`.

**Verify:** the host manifest's `messaging.outboxes` lists `primary` and each name (see [operations.md § Host manifest](operations.md#host-manifest)), and `messaging-storage-postgresql-outbox-{name}` or `messaging-storage-sqlserver-outbox-{name}` reports `Healthy`.

## Storage clock and leases

On PostgreSQL and SQL Server the database clock decides due times and lease ownership. Retry pickup compares `NextRetryAt`, and the delayed claim compares each scheduled time, against the database clock, and the store stamps added times, retry due times, and lease expiry from it. A retry delay is added to the database clock in the statement that writes it, so application clock skew never moves a retry. A scheduled publish time is the instant you chose, compared against the database clock. Retention stays on the application clock. In-memory storage uses the registered `TimeProvider` for all of these.

- **Leases are per row.** A dispatch leases its row for `RetryPolicy.DispatchTimeout` and renews it every `RetryPolicy.LeaseRenewalInterval` (default a third of `DispatchTimeout`; an explicit value is above zero and below `DispatchTimeout`) while the handler or send runs. `DispatchTimeout` bounds how long a stalled or crashed attempt keeps its row, not how long a healthy one runs.
- **A renewal that finds the lease gone** cancels the attempt's token (EventId 4131 `MessageLeaseLost`), and the attempt writes no state even if the handler finishes. A renewal that fails is logged (EventId 4129 `MessageLeaseRenewalFailed`) and retried; once the lease could lapse before the next tick, the attempt is cancelled (EventId 4130 `MessageLeaseRenewalFailedPastLease`). A send cancelled this way may still have reached the broker.
- **Claimed rows waiting their turn** in a retry batch are renewed on the same cadence; a failed renewal logs EventId 4135 `ClaimedRetryLeaseRenewalFailed`.
- **Delivery stays at-least-once.** Lease expiry lets a successor take a row, and a process paused past its lease can resume work already running.
- **Graceful shutdown** releases a lease early only for work known to be complete or not started. A handler still running at the deadline keeps its lease until it expires. When `DispatchTimeout - InitialDispatchGrace` exceeds two minutes, startup logs EventId 97: measure the longest valid handler before you shorten `DispatchTimeout`.
- **`LeaseHolderMaxLength`** on the storage options (default 512, at least 405, at most 850) sizes the column that records the lease holder (`cluster/node@incarnation` when Coordination is active). The cluster prefix keeps services under different `ClusterName`s from revoking each other's leases. See [fencing.md § Lease on your own row](../fencing.md#lease-on-your-own-row).

## Change marker and change reads

Every published and received row carries a change marker (`changed_at` / `ChangedAt`, indexed on `(changed_at, id)`), stamped from the database clock whenever a write changes what an operator sees: an insert, a state or retry write, a deferral, a requeue, a hold or release, a replay, a poison mark, or the delayed claim. Lease writes leave it alone. A deleted row has no marker, so change reads do not report deletions. The marker rises strictly per row.

- `IDataStorage.GetMessageChangeStartCursorAsync` and `GetMessageChangesAsync(cursor, overlapWindow, pageSize)` page changed rows. Each `MessageChange` carries `Id`, `ChangedAt`, `Status`, and `Version`. A `MessageChangeCursor` round-trips through `ToString` / `TryParse`, so another node can resume it.
- **The overlap window must exceed your longest transaction that writes an outbox row.** A marker is stamped when its statement runs, not when it commits, so each read re-reads `overlapWindow` (`MessageChangeCursor.DefaultOverlapWindow`, 5 seconds) behind the last one. A repeat has the same `Version`; drop it. A row that commits later than the window is missed by change reads.
- An additional outbox's storage reports only its own published rows.

## Failure text and fingerprint

Every row stores the failure that last ended or delayed it: `exception_info` / `ExceptionInfo` (the exception text) and `failure_fingerprint` / `FailureFingerprint`, a versioned value that groups failures by cause. `MediumMessage.ExceptionInfo`, `MediumMessage.FailureFingerprint`, and the monitoring API return both.

The fingerprint comes from `Headless.FailureFingerprint.Compute`: the innermost exception type and the top three non-framework stack frames, never the message, so failures that differ only by an id group together. Failures caused by the delivery rather than the code store a fixed value from `MessagingFailureFingerprints`:

| Value | Stored for |
| --- | --- |
| `v1:expired` | A publish or consume whose `DeliverWithin` deadline, or a request's deadline, passed before it ran |
| `v1:deserialization` | A received payload that could not be deserialized |
| `v1:poison` | A message rejected on arrival (no subscriber, contract version mismatch, receive-middleware reject), or a stored row a retry cannot deserialize |

Group by the fingerprint plus a hash of the consumer identity, never by the raw identity or text: SQL Server ignores trailing spaces when it compares strings.

---

## Headless.Messaging.Storage.InMemory

Process-local storage for development and tests.

```bash
dotnet add package Headless.Messaging.Storage.InMemory
```

```csharp
setup.UseInMemoryStorage();
```

- Declares `InboxGuarantee.ProcessLocal`. Durable consumers then need `setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal`. State and duplicate suppression are lost on restart.
- An enlisted publish joins any unit, relational or resource-less. The row is held on the unit and stored when the unit completes, after its commit; a rollback drops it. A crash between the commit and that store loses the row.
- Uses the registered `TimeProvider` for due times, leases, and history age.
- Retry pickup takes due rows in `NextRetryAt` order.
- Supports `IMessageRevoker`.

**Verify:** no startup signal; a test publishes and asserts the message with `MessagingTestHarness.WaitForPublishedAsync`.

## Headless.Messaging.Storage.PostgreSql

Raw ADO.NET PostgreSQL storage, with no EF Core dependency.

```bash
dotnet add package Headless.Messaging.Storage.PostgreSql
```

```csharp
setup.UsePostgreSql(builder.Configuration.GetConnectionString("Messaging")!);
// or reuse the connection from services.AddPostgreSqlSql(connectionString):
setup.UsePostgreSql();
```

- `UsePostgreSql(...)` takes a connection string, an `IConfiguration` section, `Action<PostgreSqlOptions>`, or `Action<PostgreSqlOptions, IServiceProvider>`. The parameterless overload reads the connection `AddPostgreSqlSql` registered; see [sql.md § Shared connection and schema](../sql.md#shared-connection-and-schema-for-storage-features).
- Declares `InboxGuarantee.Transactional` by default; see [Raw storage handlers](#raw-storage-handlers).
- Registers the `messaging-storage-postgresql-default` readiness check (`messaging-storage-postgresql-outbox-{name}` per additional outbox). It does not register EF Core.
- Retry pickup and the delayed claim retry a transient fault (deadlock, serialization failure, dropped connection) raised before commit, never one from the commit.
- Supports `IMessageRevoker`.

### Schema steps

- `Messaging/1` creates the tables, constraints, and key indexes. `Messaging/2` creates retry-pickup, owner, and history indexes. `Messaging/3` installs `pg_trgm` and the trigram indexes for dashboard content search. `Messaging/4` adds the correlation and causation columns and the correlation indexes.
- Indexes are built with plain `CREATE INDEX`, which blocks writes to a large existing table until it finishes. `SchemaRunnerOptions.CommandTimeout` (default 10 minutes) bounds each statement.
- **`pg_trgm` on managed PostgreSQL** (AWS RDS, Azure, Neon, Supabase): the app role usually cannot `CREATE EXTENSION`. The step then warns, skips the trigram indexes, and still records itself, so only dashboard content search is slow. Installing `pg_trgm` later does not add the indexes: have the DBA install the extension before the first start, or create the two trigram indexes by hand.
- A recorded step is never re-applied. A database created by an earlier pre-release build needs recreating, with its `headless_schema_history` rows.

**Verify:** `resources provision` applies the steps, and the `messaging-storage-postgresql-default` readiness check reports `Healthy`.

## Headless.Messaging.Storage.PostgreSql.EntityFramework

Adds `setup.UsePostgreSqlEntityFramework<TContext>()`, which derives the connection from your registered context and registers `Headless.UnitOfWork`. Install it for an EF-backed transactional outbox; it brings the raw PostgreSQL package.

```bash
dotnet add package Headless.Messaging.Storage.PostgreSql.EntityFramework
```

- **Connection.** When the context holds an Npgsql data source (passed to `UseNpgsql`, registered in DI, or built by EF for `ConfigureDataSource` or a plugin such as NetTopologySuite), the storage shares it, with its password provider. Otherwise it uses the context's connection string. A context not on Npgsql fails at startup.
- **Unit of work.** Nothing opens a unit for you; see [publishing.md § Publish atomically with your data](publishing.md#publish-atomically-with-your-data).
- **Transactional inbox.** See [Transactional inbox](#transactional-inbox). `setup.UsePostgreSqlEntityFramework<TContext>(o => o.EnableTransactionalInbox = false)` (default `true`) turns it off, with the same consequences as on the raw storage. It does not change `unit.Outbox`.

**Verify:** the `messaging-storage-postgresql-default` readiness check reports `Healthy`, and a test that publishes through `unit.Outbox` then rolls back records no message.

## Headless.Messaging.Storage.SqlServer

Raw ADO.NET SQL Server storage, with no EF Core dependency.

```bash
dotnet add package Headless.Messaging.Storage.SqlServer
```

```csharp
setup.UseSqlServer(builder.Configuration.GetConnectionString("Messaging")!);
// or reuse the connection from services.AddSqlServerSql(connectionString):
setup.UseSqlServer();
```

- `UseSqlServer(...)` takes the same overloads as PostgreSQL, over `SqlServerOptions`. The parameterless overload reads the connection `AddSqlServerSql` registered.
- Declares `InboxGuarantee.Transactional` by default; see [Raw storage handlers](#raw-storage-handlers).
- Registers the `messaging-storage-sqlserver-default` readiness check (`messaging-storage-sqlserver-outbox-{name}` per additional outbox). It does not register EF Core.
- Every transaction it opens sets READ COMMITTED itself, so a pooled session left at a stricter level and `READ_COMMITTED_SNAPSHOT` on or off behave the same. Retry pickup, the delayed claim, and purges retry a transient fault (deadlock, lock timeout, dropped connection) raised before commit.
- **Monitoring skips locked rows** (`READPAST`), so dashboard counts can dip briefly under load. PostgreSQL shows the last committed version instead.
- **Inbox operations** that share an operation id serialize on an `sp_getapplock`; the wait is bounded by `MessagingOptions.CommandTimeout`, and a timed-out request can be retried with the same id.
- **Schema steps.** `Messaging/1` (tables, constraints, indexes), `Messaging/2` (history indexes), and `Messaging/3` (correlation and causation columns, with a binary-collated correlation index on each table). The history-index builds of `Messaging/2` and the correlation-index builds of `Messaging/3` run offline, block writes to their table until done, and are bounded by `SchemaRunnerOptions.CommandTimeout` (default 10 minutes). Peer replicas wait on the runner's lock.
- Supports `IMessageRevoker`.

**Verify:** `resources provision` applies the steps, and the `messaging-storage-sqlserver-default` readiness check reports `Healthy`.

## Headless.Messaging.Storage.SqlServer.EntityFramework

Adds `setup.UseSqlServerEntityFramework<TContext>()`, which derives the connection from your registered context and registers `Headless.UnitOfWork`. Install it for an EF-backed transactional outbox; it brings the raw SQL Server package.

```bash
dotnet add package Headless.Messaging.Storage.SqlServer.EntityFramework
```

- **Connection.** The storage uses the context's connection string, checked by `GetReusableConnectionString` (see [sql.md](../sql.md#headlesssqlsqlserver)). A connection that authenticates with credentials no connection string carries (an access token or its callback, a `SqlCredential`, or a password SqlClient removed from an opened connection) fails at startup naming the cause. Give that host's storage its own connection string through the raw `UseSqlServer(...)`. A token an interceptor attaches while the connection opens is not detected. A context not on SqlClient fails at startup.
- **Unit of work and transactional inbox** behave as on PostgreSQL; `EnableTransactionalInbox = false` turns the inbox transaction off.

**Verify:** the `messaging-storage-sqlserver-default` readiness check reports `Healthy`.
