---
domain: Live Queries
packages: LiveQueries.Abstractions, LiveQueries
---

# Live Queries

> A watched query runs again when a dependency it recorded is announced as changed, and its caller receives each result that differs from the last one.

**One process only.** An announced change reaches the subscriptions of the process that announced it, and no other. There is no backplane. On a second instance of the same service, or on a separate job host that writes the same data, a subscription keeps its last result: the stream stays open, stays stale, and reports no error. Run live queries where every writer of the watched data and every subscriber share one process, or accept that a change written elsewhere shows only after something in the subscriber's process announces the same key.

## Orientation

Register the hub once. It starts no background work and does no I/O.

```csharp
builder.Services.AddHeadlessLiveQueries(setup => { });
```

Three singletons carry the feature:

- `ILiveQueries.WatchAsync` runs a query delegate now and again after each announced change, and yields the results as an `IAsyncEnumerable<LiveSnapshot<T>>`.
- `ILiveQueryDependencies.DependsOn` is called inside the query to name what its result depends on.
- `ILiveQueryInvalidator.InvalidateAsync` is called by a writer, after its change is durable, to announce the keys it changed.

```csharp
using Headless.LiveQueries;
using Microsoft.Extensions.DependencyInjection;

public sealed class OpenOrdersBoard(ILiveQueries liveQueries)
{
    public async Task ShowAsync(CancellationToken cancellationToken)
    {
        var results = liveQueries.WatchAsync(
            async (services, ct) =>
            {
                services.GetRequiredService<ILiveQueryDependencies>().DependsOn("orders");

                return await services.GetRequiredService<IOrderReader>().CountOpenAsync(ct);
            },
            cancellationToken
        );

        await foreach (var snapshot in results)
        {
            Console.WriteLine($"{snapshot.Value} open orders");
        }
    }
}

public sealed class OrderWriter(IOrderStore store, ILiveQueryInvalidator invalidator)
{
    public async Task PlaceAsync(Order order, CancellationToken cancellationToken)
    {
        await store.SaveAsync(order, cancellationToken); // committed when this returns
        await invalidator.InvalidateAsync(["orders"], cancellationToken);
    }
}
```

## Agent Rules

- Do not rely on live queries across processes. Invalidation is in-process; a second instance or a separate job host sees stale streams and no error.
- Announce a change only after it is durable. A query that re-runs before the commit reads the old data, and nothing re-runs it again.
- Every run must record at least one dependency. A run that records none fails the watch with `LiveQueryHasNoDependenciesException`, because no announcement could ever re-run it.
- Call `DependsOn` on the query's own execution flow. Work started on a detached flow, such as `Task.Run` under `ExecutionContext.SuppressFlow()`, is not recorded.
- Keep the query read-only and repeatable. It runs once per relevant announcement, each time in a new service scope.
- From code outside an HTTP request, set the tenant before you enumerate. Every run executes on the flow of the caller that enumerates the sequence and sees whatever tenant is ambient there; nothing captures a tenant when `WatchAsync` is called.
- Compare `Version` tokens for equality only. They have no order and mean nothing across subscriptions.
- The result type must serialize with `System.Text.Json` defaults. Two results are equal when their JSON is byte-for-byte equal.

## Core Concepts

### Dependency key

A dependency key is an ordinal, case-sensitive string the application chooses: `"orders"`, `"order:42"`, `"tenant:7:orders"`. A query declares the keys it read; a writer announces the keys it changed. A subscription is signalled when the two sets share a key. Keys are process-wide and carry no tenant, so put the tenant in the key when tenants must not wake each other's queries. A finer key means fewer runs; a key that is too fine is missed by a writer that does not announce it.

### Run and re-run

The first run happens when the caller starts enumerating. After each run the subscription waits for a signal. Signals that arrive while a run is in flight collapse into one further run, and a change announced during a run always causes that further run, even when no announcement follows. A re-run whose result equals the last one yielded yields nothing.

### Version token

`LiveSnapshot<T>.Version` and `LiveUpdate.Version` identify a result within one subscription: two results of the same subscription carry the same token only when they are equal, so a value that returns to an earlier state carries the earlier token again. Tokens of different subscriptions differ even for equal results.

---

## Headless.LiveQueries.Abstractions

The contracts application code and a transport author reference, with no ASP.NET Core or Entity Framework Core dependency.

### Setup

```bash
dotnet add package Headless.LiveQueries.Abstractions
```

```csharp
using Headless.LiveQueries;

public sealed class StockReader(ILiveQueryDependencies dependencies, IStockStore store)
{
    public ValueTask<int> OnHandAsync(string sku, CancellationToken cancellationToken)
    {
        dependencies.DependsOn($"stock:{sku}"); // does nothing outside a live query
        return store.OnHandAsync(sku, cancellationToken);
    }
}
```

### Contracts

| Type | Role |
| --- | --- |
| `ILiveQueries` | `WatchAsync<T>(query, cancellationToken)` returns the results of a watched query. The query receives a scoped `IServiceProvider` and a token. |
| `ILiveQueryDependencies` | `DependsOn(params IEnumerable<string> keys)` makes the running live query depend on the keys. |
| `ILiveQueryInvalidator` | `InvalidateAsync(keys, cancellationToken)` signals every subscription in this process that depends on one of the keys. |
| `LiveSnapshot<T>` | One result: `Value`, the `Version` token, and `IsInitial` for the first result of the subscription. |
| `LiveUpdate` | One update as a transport forwards it: `Kind`, `Payload` (the result as UTF-8 JSON bytes), and the `Version` token. |
| `LiveUpdateKind` | `Initial`, `Changed`, or `Failed`. A `Failed` update carries the failure body and is the last update of its subscription. |
| `LiveQueryHasNoDependenciesException` | Thrown while enumerating when a run recorded no dependency. |

`LiveSnapshot<T>` and `LiveUpdate` are records built with object initializers; both reject an empty `Version`.

### Design and runtime behavior

- `DependsOn` and `InvalidateAsync` throw `ArgumentNullException` for a null sequence and `ArgumentException` for a null or empty key. An empty sequence does nothing.
- `InvalidateAsync` completes once the affected subscriptions are signalled. It does not wait for their queries to run, and it never runs a query on the writer's flow.
- `InvalidateAsync` throws `OperationCanceledException` and announces nothing when its token is already cancelled.

---

## Headless.LiveQueries

The in-process hub, its registration, options, and meter.

### Setup

```bash
dotnet add package Headless.LiveQueries
```

```csharp
builder.Services.AddHeadlessLiveQueries(setup =>
    setup.ConfigureOptions(options => options.MaxTrackedKeys = 250_000)
);
```

`AddHeadlessLiveQueries` may be called more than once: the first call registers the services and every call's `ConfigureOptions` delegates run in order.

### Configuration

| Option | Default | Effect |
| --- | --- | --- |
| `LiveQueriesOptions.MaxTrackedKeys` | `100000` | How many dependency keys the hub remembers the last change of. Must be at least 1; a lower value fails options validation when the host starts or the options are first read. |

The hub keeps the last change of each announced key so it can re-run a query that raced a write. Past the limit it forgets the keys changed longest ago. A running query that depends on a forgotten key runs once more to be safe, so a low limit costs extra runs and never a missed change.

### Design and runtime behavior

- **Flow.** The hub only signals. Each run executes inside the caller's `await foreach`, on that caller's execution flow, with that flow's ambient state. A hosted service that watches on behalf of a tenant sets the tenant (for example with `ICurrentTenant.Change`) around the enumeration.
- **Scope.** Each run gets a new service scope, disposed when the run ends. Do not keep a scoped service from one run for the next.
- **Failure.** An exception the query throws surfaces from the enumeration and ends the watch. So does `LiveQueryHasNoDependenciesException`. Nothing retries; the caller decides whether to watch again.
- **Ending.** Cancelling the token, or disposing the enumerator while it is not waiting for a result, removes the subscription from the hub.
- **Racing writes.** A change announced while a run is in flight causes one more run without a second announcement, including when the hub has already forgotten the key.
- **Restart.** Subscriptions and the key memory live in process memory. A new process starts empty, and a subscriber that reconnects gets a fresh first result.

### Metrics

The meter is named `Headless.LiveQueries`. It comes from the host's `IMeterFactory` when one is registered (`AddMetrics()`, which ASP.NET Core hosts call), and is created directly otherwise. No instrument carries a tag.

| Instrument | Kind | Counts |
| --- | --- | --- |
| `headless.live_queries.subscriptions.active` | Up-down counter | Subscriptions currently open in this process |
| `headless.live_queries.runs` | Counter | Runs of a live query, first runs included |
| `headless.live_queries.runs.failed` | Counter | Runs that threw or reported a failure |
| `headless.live_queries.wakes` | Counter | Times an announcement signalled a subscription |
| `headless.live_queries.rows.ruled_out` | Counter | Changed rows that a subscription's row filters showed could not affect its result. Stays at zero until a package that records row filters is installed. |

Runs far above wakes mean queries are racing writes; wakes far above the changes you expect mean dependency keys are too coarse.
