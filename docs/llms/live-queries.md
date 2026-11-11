---
domain: Live Queries
packages: LiveQueries.Abstractions, LiveQueries, LiveQueries.Endpoints, LiveQueries.EntityFramework
---

# Live Queries

> A watched query runs again when a dependency it recorded is announced as changed, and its caller receives each result that differs from the last one.

**One process only.** An announced change reaches the subscriptions of the process that announced it, and no other. There is no backplane. On a second instance of the same service, or on a separate job host that writes the same data, a subscription keeps its last result: the stream stays open, stays stale, and reports no error. Run live queries where every writer of the watched data and every subscriber share one process, or accept that a change written elsewhere shows only after something in the subscriber's process announces the same key. Entity Framework Core tracking does not lift the limit: it announces the writes of its own process, and a row another process committed re-runs nothing here.

## Orientation

Register the hub once. It starts no background work and does no I/O.

```csharp
builder.Services.AddHeadlessLiveQueries(setup => { });
```

Three singletons carry the feature:

- `ILiveQueries.WatchAsync` runs a query delegate now and again after each announced change, and yields the results as an `IAsyncEnumerable<LiveSnapshot<T>>`.
- `ILiveQueryDependencies.DependsOn` is called inside the query to name what its result depends on.
- `ILiveQueryInvalidator.InvalidateAsync` is called by a writer, after its change is durable, to announce the keys it changed.

An HTTP API does not call `ILiveQueries`. It marks a GET endpoint with `.WithLiveUpdates()` from [Headless.LiveQueries.Endpoints](#headlesslivequeriesendpoints), and a browser or any HTTP client subscribes with an `Accept` header.

An application on Entity Framework Core writes neither `DependsOn` nor `InvalidateAsync` for what its context does. [Headless.LiveQueries.EntityFramework](#headlesslivequeriesentityframework) records the tables a query read and announces the tables a committed write changed.

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
- Never call `InvalidateAsync` from inside a live query: it throws `InvalidOperationException`. A query that announced a key it read would be out of date the moment it returned and would run again without end. Announce from the command that made the change.
- With a tracked EF Core context, a live query that writes through it fails: `SaveChanges`, `ExecuteUpdate`, `ExecuteDelete`, and raw SQL that names a mapped table throw `InvalidOperationException`.
- A delete through a tracked EF Core context also announces the tables whose rows refer to the deleted ones through a foreign key the model maps as cascading or set-null, because the database changes those rows itself. A foreign key the model does not describe that way is not followed: call `InvalidateAsync` for the referring tables.
- A tracked EF Core context announces only what EF Core sends. After a bulk-copy library, Dapper, a trigger, or another service changes a table, call `InvalidateAsync` with the table's key (`table:{schema}.{name}`), after the commit.
- In raw SQL through a tracked context, write each table name the way the provider's own SQL writes it (`"Todos"`, `[Todos]`). A name written another way is not found, and the query or write is not tracked.
- Turn on `RowLevelMatching` to stop one user's write from re-running every other user's stream of the same table. A filter narrows a stream only on a primary-key column or a column configured with `SetAfterSaveBehavior(PropertySaveBehavior.Throw)`; a filter on any other column still matches by table.
- A column configured as fixed after insert must never change, by any writer. `ExecuteUpdate`, raw SQL, a trigger, or another model that rewrites it leaves an entity loaded earlier matching on the old value, and the stream that now holds the row is not re-run.
- From code outside an HTTP request, set the tenant before you enumerate. Every run executes on the flow of the caller that enumerates the sequence and sees whatever tenant is ambient there; nothing captures a tenant when `WatchAsync` is called.
- Compare `Version` tokens for equality only. They have no order and mean nothing across subscriptions.
- The result type must serialize with `System.Text.Json` defaults. Two results are equal when their JSON is byte-for-byte equal.
- Make an endpoint live only with `.WithLiveUpdates()` on a `MapGet` call. Do not write a second streaming endpoint beside the ordinary one: the same URL answers both, by `Accept`.
- A live endpoint handler is read-only and returns materialized values (a list or an object, not a deferred query). It runs again after every relevant change, for every open stream.
- A live endpoint returns JSON. Return an object, a list, or a JSON result (`TypedResults.Ok(value)`, `Results.Json`); never a `string`, `Results.Text`, or `Results.Content` with another type. A successful run whose body is not `application/json` or a `+json` type fails with `g:live_query_response_not_json`.
- Do not put a token in the query string to authenticate a stream. A stream authenticates with the cookies or headers of the request that opened it, and every re-run authenticates and authorizes that request again.
- Serve live endpoints over HTTP/2. Browsers allow six HTTP/1.1 connections per origin, and each open stream holds one.
- On reconnect a client receives a full result. `Last-Event-ID` is ignored; do not build resumption on it.
- For optimistic updates, tag the command with `X-Headless-Live-Session` and `X-Headless-Live-Command` and call `app.UseHeadlessLiveCommands()` after `UseAuthentication()`. Do not invent a version comparison: a client holds its optimistic result until one of its own streams reports the command id, as [Command acknowledgement](#command-acknowledgement) specifies.
- Open a stream that must report command ids with `fetch`, not `EventSource`: `EventSource` cannot send the session header.
- Never put data or a secret in a command id. It is echoed to the sender's other streams, and only ASCII letters, digits and hyphens are accepted.

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
| `LiveUpdate` | One update as a transport forwards it: `Kind`, `Payload` (the result as UTF-8 JSON bytes), the `Version` token, and `AcknowledgedCommands`, the ids of the subscriber's own commands that the result reflects (empty for most updates). |
| `LiveUpdateKind` | `Initial`, `Changed`, `Failed`, or `Acknowledged`. A `Failed` update carries the failure body and is the last update of its subscription. An `Acknowledged` update carries no result: its `Payload` is empty, its `Version` repeats the last result's token, and it exists only for its `AcknowledgedCommands`. |
| `LiveQueryHasNoDependenciesException` | Thrown while enumerating when a run recorded no dependency. |

`LiveSnapshot<T>` and `LiveUpdate` are records built with object initializers; both reject an empty `Version`. `ILiveQueries.WatchAsync` never yields for an `Acknowledged` update: a subscription opened from .NET code has no client session, so no command is acknowledged to it.

### Design and runtime behavior

- `DependsOn` and `InvalidateAsync` throw `ArgumentNullException` for a null sequence and `ArgumentException` for a null or empty key. An empty sequence does nothing.
- `InvalidateAsync` completes once the affected subscriptions are signalled. It does not wait for their queries to run, and it never runs a query on the writer's flow.
- `InvalidateAsync` throws `OperationCanceledException` and announces nothing when its token is already cancelled.
- `InvalidateAsync` throws `InvalidOperationException` and announces nothing when a live query calls it with at least one key, on the query's own execution flow. The message names the keys and the read-only rule. A live endpoint treats it as any exception from the handler: the host's error response when the stream opens, or a `g:live_query_run_failed` event on a re-run.
- **A run ends when the query returns.** Work the query started and left running (a `Task.Run`, a timer, a lazily started loop in a service it touched) is no longer part of the live query once the run has returned: `DependsOn` and tracked reads on it record nothing, `InvalidateAsync` on it is an ordinary announcement, and a write through a tracked EF Core context is an ordinary write that is announced. Do not start such work from a live query on purpose; this rule only keeps it from failing later with the read-only error.

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

The meter is named `Headless.LiveQueries`, exposed at compile time as `LiveQueriesDiagnostics.SourceName`, and carries the framework's version. Subscribe with the typed helper `metrics.AddLiveQueriesInstrumentation()` (it surfaces in the `OpenTelemetry.Metrics` namespace and requires only `OpenTelemetry.Api`) or by name with `AddMeter(LiveQueriesDiagnostics.SourceName)`; both are equivalent, and one subscription also covers the instruments live endpoints and tracked data sources add. The meter comes from the host's `IMeterFactory` when one is registered (`AddMetrics()`, which ASP.NET Core hosts call); a host without one records on a meter shared by the process, which a process-wide listener still hears. No instrument carries a tag.

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddLiveQueriesInstrumentation());
```

| Instrument | Kind | Counts |
| --- | --- | --- |
| `headless.live_queries.subscriptions.active` | Up-down counter | Subscriptions currently open in this process |
| `headless.live_queries.runs` | Counter | Runs of a live query, first runs included |
| `headless.live_queries.runs.failed` | Counter | Runs that threw or reported a failure |
| `headless.live_queries.wakes` | Counter | Times an announcement signalled a subscription |
| `headless.live_queries.rows.ruled_out` | Counter | Changed rows that a subscription's row filters showed could not affect its result, one count per row and subscription. Stays at zero unless a tracked EF Core context has `RowLevelMatching` on. |
| `headless.live_queries.tracking.fallbacks` | Counter | Times a tracked data source failed while it captured or announced a write, and announced the tables it knew instead. Stays at zero without a tracked data source. |

Runs far above wakes mean queries are racing writes; wakes far above the changes you expect mean dependency keys are too coarse. A tracking fallback that is not zero means a live query may have missed a write: read the error the tracker logged with it.

---

## Headless.LiveQueries.Endpoints

Makes a Minimal API GET endpoint live for HTTP clients: the endpoint streams its result and sends it again when a dependency it recorded is announced as changed.

### Setup

```bash
dotnet add package Headless.LiveQueries.Endpoints
```

```csharp
builder.AddHeadless(); // or services.AddHeadlessProblemDetails(): refusals and failures are problem details
builder.Services.AddHeadlessLiveQueries(setup => setup.UseEndpoints());

var app = builder.Build();

app.MapGet(
        "/todos",
        async (ITodoReader todos, ILiveQueryDependencies dependencies, CancellationToken cancellationToken) =>
        {
            dependencies.DependsOn("todos");

            return await todos.ListAsync(cancellationToken);
        }
    )
    .RequireAuthorization()
    .WithLiveUpdates();
```

The map call stays ordinary Minimal API; `.WithLiveUpdates()` is the only addition, and an endpoint without it is unchanged. On a route group it applies to every endpoint of the group.

- A request that names no stream format in `Accept` (`application/json`, `*/*`, none) receives the endpoint's ordinary single response and opens no subscription.
- A request whose `Accept` names `text/event-stream` or `application/x-ndjson` receives a stream: the result now, and a new result after each announced change that alters it. A wildcard never selects a stream.
- The ordinary response and every stream carry `Vary: Accept`, because one URL answers with two representations.

`UseEndpoints` has the usual overloads: none, `IConfiguration`, `Action<LiveEndpointsOptions>`, and `Action<LiveEndpointsOptions, IServiceProvider>`. The host fails at startup when `IProblemDetailsCreator` is not registered, or when an `IHttpContextAccessor` registered after `UseEndpoints` replaced the one it wrapped (see [Design and runtime behavior](#design-and-runtime-behavior-2)). `WithLiveUpdates()` throws `InvalidOperationException` when the endpoints are built, naming the endpoint, for an endpoint that accepts any method but GET and for a host that did not call `UseEndpoints`.

### What a client sees

Server-sent events (`Accept: text/event-stream`), for browsers and `EventSource`:

```text
retry: 3000

event: result
id: 5f1c0c1d8a4e4b0f9d2a7c3e6b1f0a9d
data: [{"id":1,"title":"milk"}]

: heartbeat

event: result
id: 0b7e2a9c4d6f8e1a3c5b7d9f2e4a6c8b
data: [{"id":1,"title":"milk"},{"id":2,"title":"eggs"}]

event: result
id: 3c9d1e5f7a2b4c6d8e0f1a3b5c7d9e2f;2f6c1a52-7d0e-4c58-9f0a-6f1f3b1d9a11
data: [{"id":1,"title":"milk"},{"id":2,"title":"eggs"},{"id":3,"title":"bread"}]

event: acknowledged
data: ["8f1c44a0-3b7e-4d0e-b7a5-0a1e5d2c9f43"]

event: failure
id: 9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d
data: {"type":"…","title":"Forbidden","status":403,"detail":"…","error":{"code":"g:live_query_unauthorized","description":"…"}}
```

| Part | Meaning |
| --- | --- |
| `retry:` | Sent once, first. `LiveEndpointsOptions.ReconnectDelay` in milliseconds. |
| `event: result` | The endpoint's response body, on one `data:` line even when the host's serializer indents. |
| `event: failure` | A problem-details body. It is the last event; the server then ends the response. |
| `id:` | The update's opaque version token. Compare for equality only. On a result that holds commands of this client, the token is followed by `;` and the command ids, separated by `,`. Neither character occurs in a token or an id. |
| `event: acknowledged` | Commands of this client that the last result already reflects, with no new result: `data:` is a JSON array of the command ids. It has no `id:`. Sent only on a stream opened with a session id; see [Command acknowledgement](#command-acknowledgement). |
| `: heartbeat` | A comment line, written whenever `HeartbeatInterval` passes with nothing sent. `EventSource` ignores it. |

Newline-delimited JSON (`Accept: application/x-ndjson`), for callers that read a plain stream. One object per line:

```text
{"kind":"result","version":"5f1c0c1d8a4e4b0f9d2a7c3e6b1f0a9d","value":[{"id":1,"title":"milk"}]}
{"kind":"heartbeat"}
{"kind":"result","version":"3c9d1e5f7a2b4c6d8e0f1a3b5c7d9e2f","acknowledged":["2f6c1a52-7d0e-4c58-9f0a-6f1f3b1d9a11"],"value":[{"id":1,"title":"milk"},{"id":3,"title":"bread"}]}
{"kind":"acknowledged","acknowledged":["8f1c44a0-3b7e-4d0e-b7a5-0a1e5d2c9f43"]}
{"kind":"failure","version":"9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d","value":{"title":"Forbidden","status":403,"error":{"code":"g:live_query_unauthorized","description":"…"}}}
```

A result line has an `acknowledged` member only when it holds commands of this client; a failure line never has one. A client ignores a `kind` it does not know. Both formats send `Cache-Control: no-cache, no-store` and `X-Accel-Buffering: no`, and turn response buffering off, so each update leaves when it is written. `UseHeadless` does not buffer either format: neither media type is in its response-compression list.

A re-run whose result equals the last one sent sends nothing, unless the stream has a command to acknowledge, and then it sends the acknowledgement alone. A client that reconnects opens a new stream and receives a full result; `Last-Event-ID` is ignored.

Error codes, as constants on `LiveEndpointsErrorCodes`:

| Code | Sent | Meaning |
| --- | --- | --- |
| `g:live_query_no_dependencies` | 500 response when the stream is opened; failure event later | A run recorded no dependency, so no change could ever update the stream. |
| `g:live_query_response_not_json` | 500 response when the stream is opened; failure event later | A run answered 200 to 299 with a body that is not declared as JSON. The content type is logged, the body is never sent. |
| `g:live_query_run_failed` | Failure event | A re-run threw (`status` 500) or answered outside 200 to 299 (`status` is that status). The exception is logged, never sent. |
| `g:live_query_unauthorized` | Failure event | A re-run found the caller no longer authenticated (`status` 401) or no longer authorized (`status` 403). |
| `g:live_query_stream_limit_reached` | 429 response with `Retry-After` | The caller or the host already holds the allowed number of streams. |

### Command acknowledgement

A client that shows the expected result of a command at once (an optimistic update) needs to know when the server's result holds that command, so it can drop its own guess in the same step and never show the change twice or not at all. The client tags the command, and the server reports the command id on that client's own streams. No other client is told.

```csharp
app.UseAuthentication();
app.UseAuthorization();
app.UseHeadlessLiveCommands(); // after UseAuthentication, before the endpoints
```

`UseHeadlessLiveCommands()` reads the headers below from every request and passes a request without them through untouched. Streams need no middleware: a host that acknowledges no command leaves the call out. Place it after `UseAuthentication()`: the user is read when the request reaches it, so ahead of authentication every command counts as anonymous and a signed-in client's streams acknowledge nothing. It is not part of `UseHeadless()`; calling it twice adds it once.

The headers, as constants on `LiveCommandHeaders`:

| Header | On | Value |
| --- | --- | --- |
| `X-Headless-Live-Session` | Request: every stream and every command of one client | An id the client generates once, for itself: 22 to 64 ASCII letters, digits and hyphens, random. `crypto.randomUUID()` fits. |
| `X-Headless-Live-Command` | Request: a command | An id the client generates for this command: 1 to 64 ASCII letters, digits and hyphens. |
| `X-Headless-Live-Acknowledgement` | Response: a command whose two ids were accepted | `pending`: the command committed a tracked write, and each open stream of the client reports the command id. `none`: it committed none, and no stream will. |

What the server does:

- **An id of any other shape is ignored.** So is a header sent more than once, and a command id without a usable session id. The command still runs; the response carries no `X-Headless-Live-Acknowledgement`, and nothing is acknowledged. The shape is enforced because a command id is written to another connection's stream.
- **A tracked write is one announced on the command's own request.** That is a commit of a tracked EF Core context, or a call to `ILiveQueryInvalidator.InvalidateAsync`, made on the request's execution flow before the request ends. A commit completed elsewhere, such as an ambient `TransactionScope` that finishes on another thread, or work the handler leaves running after it returns, is announced as nobody's command: streams still receive the new result, with no command id.
- **Only the sender's streams are told.** A stream reports a command id when it was opened with the same session id by the same user (the user defined under [Configuration](#configuration-1); two anonymous requests are the same user). Another user who learned the session id is told nothing, and neither is the same user's other tab or device, which has its own session id.
- **A stream the write could change reports the id with the result that holds it.** The stream runs again after the commit. If the result changed, the id travels on that result (`id:` in server-sent events, `acknowledged` in NDJSON). If the result is the same as the last one sent, the id travels alone, as an `acknowledged` event or line.
- **A stream the write does not touch reports the id alone, without running again.** So a client can drop an optimistic result it applied to a query the command turned out not to change.
- **The response header is decided when the response starts.** A handler that streams its response before it commits answers `none` and is acknowledged on the streams all the same.
- **A command that commits more than once** may be reported after its first commit and again after a later one. Commit once, in one unit of work, when the client must see one step.
- **A stream holds at most 1,024 ids it has not sent yet.** Past that, which takes a client that stopped reading its stream, the oldest are dropped.
- **One process.** The command and the stream must be served by the same process, as for every announcement. On a second instance the response still says `pending` and no stream reports the id.

What a client does:

1. Generate a session id once, when the application starts. Send it on every stream and every command. Open streams with `fetch`, because `EventSource` cannot send a header.
2. For a command with an optimistic result: generate a command id, apply the patch to each query it should change, and send the command with both headers. A query's displayed value is the last server result with its pending patches applied on top.
3. When the command's response arrives:
   - failed: remove its patches (roll back);
   - `X-Headless-Live-Acknowledgement: pending`: keep each patch until that query's stream reports the command id;
   - `none`, or no such header: remove its patches now. No stream will report the id.
4. On a stream: for a result that carries command ids, replace the server result and remove those commands' patches from that query in the same step. For an `acknowledged` event or line, remove the patches and keep the server result.
5. When a stream opens or reopens, its first result reflects every command committed before it opened, and the ids reported to the stream it replaces are gone. So for each patch of that query whose command was sent before the first result arrived, remove the patch once both the first result and the command's response are in, whether or not an id came. In the rare case that the command committed just after the stream opened, the result without it shows for a moment, and the stream sends the result with it next.

Version tokens play no part: a client never compares them to decide whether a result holds a command.

```ts
const session = crypto.randomUUID();

async function watch(url: string, onResult: (value: unknown) => void, acknowledged: (ids: string[]) => void) {
  const response = await fetch(url, {
    headers: { Accept: "application/x-ndjson", "X-Headless-Live-Session": session },
  });
  const lines = response.body!.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = "";

  for (;;) {
    const { value, done } = await lines.read();
    if (done) return;
    buffer += value;

    for (let end = buffer.indexOf("\n"); end >= 0; end = buffer.indexOf("\n")) {
      const line = JSON.parse(buffer.slice(0, end));
      buffer = buffer.slice(end + 1);

      // The result first, then the ids it holds, with no render in between.
      if (line.kind === "result") onResult(line.value);
      if (line.acknowledged) acknowledged(line.acknowledged);
    }
  }
}

async function send(url: string, body: unknown, patch: { remove(): void; command: string }) {
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Headless-Live-Session": session,
      "X-Headless-Live-Command": patch.command,
    },
    body: JSON.stringify(body),
  });

  // Kept only while a stream is going to report the command.
  if (!response.ok || response.headers.get("X-Headless-Live-Acknowledgement") !== "pending") patch.remove();
}
```

A browser client on another origin needs both request headers allowed and `X-Headless-Live-Acknowledgement` exposed by the host's CORS policy.

### OpenAPI and generated clients

`.WithLiveUpdates()` also attaches `LiveUpdatesMetadata` (`Headless.Api.Abstractions`) to the endpoint. A host that uses `Headless.OpenApi` then documents each live operation with no further code: the operation carries the `x-headless-live` extension, and its success response lists `text/event-stream` and `application/x-ndjson` beside `application/json`, each typed with the same schema as the single response. [OpenAPI](openapi.md#design-constraints) shows the exact shape. There is no separate manifest of live endpoints; the OpenAPI document is the contract a client is generated from.

```json
{
  "paths": {
    "/todos": {
      "get": {
        "operationId": "GetTodos",
        "x-headless-live": { "mediaTypes": ["text/event-stream", "application/x-ndjson"] }
      }
    }
  }
}
```

A TypeScript client comes from that document with any OpenAPI generator. `openapi-typescript` is one example; this framework neither ships nor requires it:

```bash
npx openapi-typescript https://localhost:5001/openapi/v1.json --output src/api/schema.d.ts
```

The generated types give every operation its response body type. The generator does not read `x-headless-live`, so the client decides per operation from the marker: an operation without it is one request, and an operation with it may be opened as a stream whose `result` events carry the same body type.

```ts
import type { paths } from "./api/schema";

type Todos = paths["/todos"]["get"]["responses"][200]["content"]["application/json"];

// A one-time read: the ordinary request, as for any operation.
const todos: Todos = await (await fetch("/todos", { headers: { Accept: "application/json" } })).json();

// A live read: the same URL, opened as server-sent events. EventSource sends Accept: text/event-stream.
const stream = new EventSource("/todos", { withCredentials: true });

stream.addEventListener("result", (event) => {
  render(JSON.parse(event.data) as Todos);
});

stream.addEventListener("failure", (event) => {
  // A problem-details body. It is the last event; close so the browser does not reconnect.
  stream.close();
  report(JSON.parse(event.data));
});
```

- **Find the live operations from the document, not from a list kept by hand.** A build step that reads the document (`Object.entries(document.paths)`, then each method's `x-headless-live`) yields the operations a client may open as streams, and `mediaTypes` yields the `Accept` values to choose from.
- **Reuse the generated type.** A `result` event's `data`, and a `result` line's `value` in NDJSON, parse to the operation's `application/json` type. The generated `application/x-ndjson` type is the union of the three line kinds.
- **`EventSource` cannot send an `Authorization` header.** It suits cookie authentication. A client that authenticates with a bearer token reads the stream with `fetch` and `Accept: text/event-stream` or `Accept: application/x-ndjson`, and parses the body itself.
- **A published client package and UI-framework hooks are not part of this repository.** They belong with the framework's web packages, generated from this same document. There is no .NET client either.

### Configuration

| Option | Default | Effect |
| --- | --- | --- |
| `HeartbeatInterval` | 20 seconds | Silence after which a heartbeat is written. It waits real time. More than zero, at most 1 hour. |
| `ReconnectDelay` | 3 seconds | The server-sent events `retry:` value and the `Retry-After` of a refused stream. |
| `MaxStreamsPerUser` | 50 | Streams one user may hold. An anonymous stream counts against its remote address. The next stream gets 429. |
| `MaxStreams` | 10,000 | Streams the host holds across all users. The next stream gets 429. |
| `MaxConcurrentReruns` | 32 | Re-runs executing at a time across the host. A signalled stream past the limit waits for its turn. A stream's first run is not counted. |
| `MinRerunInterval` | 100 milliseconds | Least time between the starts of two runs of one stream. Changes announced inside it are answered by one re-run when it ends. Zero turns the wait off. |

A user is the `user_id` claim (then the name identifier, then the identity name) within its `tenant_id` claim. Behind a reverse proxy, anonymous callers share the proxy's address unless forwarded headers are applied, which `UseHeadless` does.

`MinRerunInterval` is measured on a clock of its own, the keyed `TimeProvider` registered under `LiveEndpointsOptions.RerunClockServiceKey`, which defaults to `TimeProvider.System`. A fake application clock therefore never stalls streams. A test that wants to step through the interval registers a fake clock under that key.

Invalid values fail options validation when the host starts.

### Design and runtime behavior

- **A run is the whole endpoint, executed again.** Every run, the first included, sends a synthetic request through the endpoint: arguments are bound again (route values, query, `[AsParameters]` types, services, keyed services), endpoint filters such as validation run again, and services come from a new scope that is disposed when the run ends. Middleware does not run again: the synthetic request sees what middleware established for the request that opened the stream (the endpoint, route values, request features, the resolved tenant) and has its own response, `Items` copy, user, and service scope. The run's response is buffered in memory and sent whole.
- **The handler is read-only and returns materialized values.** It runs once per relevant change for every open stream. Writes in it would repeat without bound, and a deferred query would run outside the handler.
- **A run's body must be JSON.** Both built-in formats place the body in the stream as a JSON value, as the endpoint wrote it. A successful run with a non-empty body whose `Content-Type` is not `application/json` or a `+json` media type (a handler that returns a `string` answers `text/plain`) is therefore a failed run, and its body is never sent: the request that opens the stream gets a 500 problem response with `g:live_query_response_not_json` and no stream, and a later run ends the stream with one failure event carrying that code. An empty body is the value `null`. A request to the same URL that asks for no stream is not checked.
- **Authentication and authorization are checked on every run.** A run authenticates the default scheme again, which is what notices an expired token, and assigns the fresh principal to the run's request, so `ICurrentUser` and `HttpContext.User` in the handler are the run's. It then evaluates what the authorization middleware evaluates: `[Authorize]` data, `AuthorizationPolicy` metadata, requirement data, and the fallback policy when the endpoint has none of them. `AllowAnonymous` skips the check. A refusal ends the stream with a failure event.
- **A stream authenticates with what the opening request carried.** Cookies and headers are sent once, when the stream opens, and every run authenticates them again. There is no query-string token. A client that renews its token opens a new stream.
- **Tenancy.** Runs execute on the opening request's own execution flow, so the ambient tenant the tenancy middleware set stays in effect; a stream of tenant B re-runs under tenant B whoever wrote. A run whose freshly authenticated principal carries a different `tenant_id` claim than the principal that opened the stream, or none, is refused, and the stream ends with a `g:live_query_unauthorized` failure. With catalog identifier resolution, a principal whose tenant claim stops matching the identifier is refused the same way. A host that keeps the tenant in a claim type of its own gets neither comparison on endpoints without the catalog check; there the stream keeps the tenant it opened under.
- **The first run decides whether there is a stream.** A first run that answers outside 200 to 299 is sent to the client as it is (status, headers, body). A first run that throws takes the host's ordinary error handling. A first run that records no dependency is refused with a 500 problem. None of them opens a stream.
- **A failed re-run ends the stream.** The client receives one failure event and the response ends. The host keeps serving; the client may reconnect.
- **Command acknowledgement rides the same loop.** A stream reads its session id from the request that opened it, once. Command ids reach it through the hub and are written by the stream's own loop, with the result of a run that started after the command's commit, so an id never arrives ahead of the write it stands for.
- **Limits gate the stream, nothing else.** The minimum interval and the re-run limit make a stream's own loop wait. No run ever executes on a writer's flow or a shared worker.
- **Host shutdown.** When the host begins to stop, every open stream ends and the response completes normally. A server-sent events client reconnects after `ReconnectDelay`.
- **`IHttpContextAccessor`.** Inside a run it returns the run's request, so `ICurrentUser` and anything else that reads the accessor in the handler sees the run's freshly authenticated principal. Everywhere else it returns what the host's accessor returns: middleware above the endpoint, an exception handler, problem-details creation, and a transport keep reading the request that opened the stream, before and after every run. Work a run started and left running reads `null` from the accessor once that run has returned, because the run's request is gone with its service scope. `UseEndpoints()` does this by wrapping the registered accessor (it calls `AddHttpContextAccessor()` itself), so register a custom `IHttpContextAccessor` before `AddHeadlessLiveQueries`; `AddHttpContextAccessor()` may be called before or after. A host whose accessor was replaced after `UseEndpoints()` fails at startup, since its runs would read the opening request and the principal it opened with.
- **HTTP/2.** Each open stream holds one connection on HTTP/1.1, and browsers allow six per origin. Serve live endpoints over HTTP/2, where streams share a connection.
- **One process.** The limit at the top of this guide applies: a change announced in another process does not reach these streams.

### Metrics

Live endpoints add two instruments to the `Headless.LiveQueries` meter (`LiveQueriesDiagnostics.SourceName`), so `AddLiveQueriesInstrumentation()` subscribes to them too. An open stream is one of its active subscriptions.

| Instrument | Kind | Counts |
| --- | --- | --- |
| `headless.live_queries.streams.rejected` | Counter | Streaming requests refused with 429 by a stream limit |
| `headless.live_queries.reruns.queued` | Counter | Re-runs that waited because `MaxConcurrentReruns` were already executing |

Queued re-runs that keep rising mean writes wake more streams than the host re-runs at once: narrow the dependency keys or raise the limit.

### Writing a transport

`ILiveTransport` adds a wire format without changing any endpoint. A streaming request is answered by the first transport that accepts it: the added ones in the order they were added, then server-sent events, then newline-delimited JSON, so an added transport also replaces a built-in one for the requests it accepts.

```csharp
using System.Text;
using Headless.LiveQueries;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

public sealed class LineLiveTransport : ILiveTransport
{
    public const string MediaType = "application/x-live-lines";

    public bool CanHandle(HttpContext httpContext)
    {
        return httpContext
            .Request.GetTypedHeaders()
            .Accept.Any(accepted => accepted.MediaType.Equals(MediaType, StringComparison.OrdinalIgnoreCase));
    }

    public async Task WriteAsync(
        HttpContext httpContext,
        IAsyncEnumerable<LiveUpdate> updates,
        CancellationToken cancellationToken
    )
    {
        httpContext.Response.ContentType = MediaType;
        httpContext.Response.Headers[HeaderNames.CacheControl] = "no-store";

        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            var payload = Encoding.UTF8.GetString(update.Payload.Span);
            var commands = string.Join(',', update.AcknowledgedCommands);
            var line = $"{update.Kind}|{update.Version}|{commands}|{payload}\n";
            await httpContext.Response.WriteAsync(line, cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
    }
}
```

```csharp
builder.Services.AddHeadlessLiveQueries(setup => setup.UseEndpoints().AddTransport<LineLiveTransport>());
```

- A transport is a singleton shared by every stream. Keep per-stream state in locals.
- Enumerate `updates` once, and await each step from `WriteAsync` itself. A re-run executes inside that step and must stay on the request's execution flow; do not move the enumeration to `Task.Run` or a channel reader on another flow.
- The first update is available at once. A `Failed` update is the last one; its payload is a problem-details body.
- `AcknowledgedCommands` holds the ids of the client's own commands that the update's result reflects; see [Command acknowledgement](#command-acknowledgement). Carry them in the same record as the result, never in one before or after it: a client that learned them apart would show its optimistic result twice or not at all in between. Each id is a token of ASCII letters, digits and hyphens, at most 64 characters, and needs no escaping. The list is empty unless the stream was opened with a session id.
- An `Acknowledged` update has ids and no result: its `Payload` is empty and its `Version` repeats the last result's. Write the ids, or skip the update in a format that carries no acknowledgement. Do not write its empty payload as a result.
- `Payload` is the endpoint's response body, unchanged. It contains line breaks when the host's serializer indents; a line-based format removes them between tokens, as the built-in transports do, and the sample above does not.
- The transport starts the response: content type, cache headers, and flushing after each update. `Vary: Accept` is already set.
- `cancellationToken` is cancelled when the client disconnects or the host stops. Return or let `OperationCanceledException` leave.
- A heartbeat is the transport's to write. Read `IOptions<LiveEndpointsOptions>.Value.HeartbeatInterval` and wait real time.
- New members of `ILiveTransport` arrive as default interface members, so a transport keeps compiling across versions.

---

## Headless.LiveQueries.EntityFramework

Tracks an Entity Framework Core context for live queries: a query through the context depends on the tables it read, and a committed write through the context re-runs the live queries that read the written tables. The application writes no `DependsOn` and no `InvalidateAsync` for what the context does.

### Setup

```bash
dotnet add package Headless.LiveQueries.EntityFramework
```

```csharp
using Headless.LiveQueries;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHeadlessLiveQueries(setup => setup.UseEndpoints().UseEntityFramework<AppDbContext>());

var app = builder.Build();

// Depends on the Todos table, because that is what the query read.
app.MapGet(
        "/todos",
        async (AppDbContext db, CancellationToken cancellationToken) =>
            await db.Todos.OrderBy(todo => todo.Id).ToListAsync(cancellationToken)
    )
    .WithLiveUpdates();

// Re-runs every stream that read Todos, once the save has committed.
app.MapPost(
    "/todos",
    async (Todo todo, AppDbContext db, CancellationToken cancellationToken) =>
    {
        db.Todos.Add(todo);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created();
    }
);
```

`UseEntityFramework<TContext>` has the usual overloads: none, `IConfiguration`, `Action<LiveQueriesEntityFrameworkOptions>`, and `Action<LiveQueriesEntityFrameworkOptions, IServiceProvider>`. Call it once for each context type to track. Each context type keeps its own options, registered as options named after the context type's full name; a second call for the same type applies its options on top and attaches nothing twice.

The call attaches interceptors to that context type only, through `ConfigureDbContext<TContext>`, so it works in either order with `AddDbContext`, `AddDbContextPool`, `AddDbContextFactory`, and `AddPooledDbContextFactory`. A context built by hand from options the service collection did not build is not tracked. Registration starts no background work and does no I/O.

### Dependency keys

A table's dependency key is `table:{schema}.{name}`, or `table:{name}` when the model maps the table without a schema. Both parts are the model's names, case-sensitive, not the database's default schema: an entity mapped with `ToTable("Orders", "sales")` has the key `table:sales.Orders`, and one mapped with `ToTable("Todos")` has `table:Todos` on every provider. A mapped view has a key of the same form.

These are the keys to pass when the tracker cannot see a read or a write:

```csharp
// A writer EF Core does not issue: announce after its commit.
await bulkCopy.WriteToServerAsync(rows, cancellationToken);
await invalidator.InvalidateAsync(["table:sales.Orders"], cancellationToken);

// A read the tracker cannot attribute: a view, a function, a cache in front of the table.
dependencies.DependsOn("table:sales.Orders");
```

### What is tracked

| Operation through the tracked context | Tracked as |
| --- | --- |
| A LINQ query, `FromSql`, `SqlQuery`, inside a live query | A read of every mapped table or view the SQL names |
| `SaveChanges` | A write of every table of each added, modified, or deleted entity, and of every table the save's commands name. For a deleted entity, also every table whose rows refer to it |
| `ExecuteUpdate`, `ExecuteDelete` | A write of every mapped table the SQL names. For a delete, also every table whose rows refer to those tables |
| `ExecuteSql`, `ExecuteSqlRaw`, `ExecuteSqlInterpolated` | A write of every mapped table the SQL names. For SQL that holds the keyword `DELETE`, `TRUNCATE`, or `REPLACE`, also every table whose rows refer to those tables |

A table is found in SQL by its name as the provider writes it: `"Todos"` on PostgreSQL and SQLite, `[Todos]` on SQL Server. EF Core's own SQL always matches. The schema is not part of the search, so hand-written SQL may qualify the table or leave the schema to the search path. An entity stored in several tables (table-per-type inheritance, entity splitting) reports all of them on every save, because a query may read any one of them alone.

**A delete announces the tables that refer to the deleted rows.** When a row is deleted, the database removes the rows that refer to it (`ON DELETE CASCADE`) or clears their foreign key (`ON DELETE SET NULL`) by itself, with no command the tracker could read, and the context may never have loaded them. The tracker reads the model instead: a delete also announces the tables of every entity type with a foreign key to the deleted type whose delete behavior is `Cascade`, `ClientCascade`, `SetNull`, or `ClientSetNull`, and, through the cascading ones, the tables that refer to those, to any depth. Those tables are announced whole, also with [row-level matching](#row-level-matching) on. A delete is an entity saved in the `Deleted` state, `ExecuteDelete`, or SQL whose text holds `DELETE`, `TRUNCATE`, or `REPLACE` as a word anywhere. `REPLACE` counts because SQLite's `INSERT OR REPLACE` and `REPLACE INTO` (and MySQL's `REPLACE INTO`) delete the row that holds the key before they insert, and that delete runs the foreign keys' actions: the dependents are removed or cleared although the replaced row is still there. Matching a word is deliberately coarse: an update of a column named `Delete`, or one that calls the `replace()` string function, is taken for a delete and costs a re-run, while `deleted_at` or `replaced_by` is not. A foreign key configured as `Restrict` or `NoAction` announces nothing.

Not tracked, with what to do instead:

- **Writes EF Core does not issue.** Bulk-copy libraries, Dapper or ADO.NET on the same connection, triggers, other services, and another process. Call `ILiveQueryInvalidator.InvalidateAsync` with the table keys after the commit.
- **Referential actions the model does not describe.** A foreign key that exists only in the database, one the model maps as `Restrict` or `NoAction` while the database cascades, `ON UPDATE CASCADE` after a key changed, and a delete made inside a stored procedure or a trigger. The rows the database changes then are announced by nobody. Call `InvalidateAsync` with the keys of the referring tables after the commit.
- **Raw SQL that writes a table name another way.** `FROM Todos` without quotes on SQL Server or SQLite is not found, as a read or as a write. Quote the name as the provider does, or declare it: `DependsOn` for a read, `InvalidateAsync` for a write. PostgreSQL leaves an all-lowercase name unquoted, so there the bare name matches.
- **A query that writes.** `FromSql` or `SqlQuery` over `INSERT … RETURNING` or a procedure that writes is treated as a read. Announce the write.
- **Views, functions, and stored procedures.** A query over a mapped view depends on the view's own key, and a write to the view's base tables does not announce it. Declare the base tables with `DependsOn`.
- **Read replicas.** A live handler must read from the database its writers commit to. A re-run that reads a replica before the replica applied the write shows the old data, and nothing re-runs it again.
- **Migrations, scaffolding, and value generators.** Their commands are ignored.

Tracking is per table unless [row-level matching](#row-level-matching) is on: any write to a table re-runs every live query that read it, whichever rows it changed. Two tables with one name in two schemas are reported together.

### Row-level matching

With `RowLevelMatching` on for a context, a saved row re-runs a live query only when the row can pass the filter the query read the table through. It is off by default, set per context type, and never loses a re-run: wherever a row or a query cannot be trusted to say enough, matching falls back to the table.

```csharp
using Headless.LiveQueries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

builder.Services.AddHeadlessLiveQueries(setup =>
    setup.UseEndpoints().UseEntityFramework<AppDbContext>(options => options.RowLevelMatching = true)
);

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // A todo never moves to another owner. EF Core now refuses to update the column, and a filter on it
        // narrows a live query.
        modelBuilder
            .Entity<Todo>()
            .Property(todo => todo.OwnerId)
            .Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class Todo
{
    public int Id { get; set; }

    public int OwnerId { get; set; }

    public string Title { get; set; } = "";
}
```

```csharp
// Re-runs when a todo of this owner is added, changed, or deleted, and not for another owner's todo.
app.MapGet(
        "/owners/{ownerId:int}/todos",
        async (int ownerId, AppDbContext db, CancellationToken cancellationToken) =>
            await db.Todos.Where(todo => todo.OwnerId == ownerId).OrderBy(todo => todo.Id).ToListAsync(cancellationToken)
    )
    .WithLiveUpdates();
```

**Which filters narrow a query.** A condition narrows when it compares a column of the queried entity for equality with a constant or a captured value, and all of these hold:

- The column is a primary-key property, or a property whose `AfterSaveBehavior` is `Throw`. A column that can change after insert does not narrow, because the copy of a row a context holds may be older than the stored row. This includes a tenant or owner column until it is configured as above.
- The column's type is an integer type, `bool`, an enum, or `Guid`, nullable or not, and the property has no value converter. Text, dates, and fractions do not narrow.
- The comparison is `==`, a bare `bool` column, or its negation. Conditions joined by `&&`, several `Where` calls, and the predicate of `Count`, `Any`, `First`, `Single`, or `Last` (and their `OrDefault` forms) are combined. A condition that does not qualify is ignored, and the ones beside it still narrow.

**Which queries qualify.** The filter is used only when the query is one chain over one `DbSet` made of `Where`, the ordering operators, `Select`, `Skip`, `Take`, `Distinct`, `Sum`, `Min`, `Max`, `Average`, the tracking and tagging operators, and the terminal operators above, with a filter placed before any `Select`, `Skip`, `Take`, or `Distinct`. `Find` and a precompiled query (`EF.CompileAsyncQuery`) qualify too. A query matches by table when it:

- uses a navigation, `Include`, a join, a set operator, `GroupBy`, `All`, a subquery, `FromSql`, or a function mapped with `HasDbFunction`;
- reads an entity type with a navigation loaded by default (`AutoInclude`, an owned type), in an inheritance hierarchy, stored in several tables, or sharing its table with another entity type;
- reads an entity type whose global query filter runs a query of its own or reads a navigation (a filter on the row's own columns, such as soft delete or a tenant id, is fine);
- reads a navigation by name with `EF.Property`;
- compares with a captured value that is `null`;
- ends in SQL that names any table but the entity's own.

**Which writes are matched by row.** Only rows saved by `SaveChanges`:

| Write | Matched on |
| --- | --- |
| An added entity | Its key and fixed columns as stored, database-generated values included |
| A modified or deleted entity that EF Core loaded with a tracking query | Its key, and the fixed columns as they were read |
| A modified or deleted entity the application built or attached, loaded without tracking, or changed while detached | Its key alone; a filter on a fixed column treats the row as a possible match |
| A modified or deleted entity whose key has a part of a type rows are not matched on, such as text | The matched parts of its key alone |
| An entity added by this context and saved again without being read back | Its key alone |
| `ExecuteUpdate`, `ExecuteDelete`, raw SQL | The table |
| A row the database removes or clears because the row it refers to was deleted | The table |
| A save through another context type or model that maps the same table | The table |
| More than 256 rows of matched tables in one commit | The tables |
| A commit whose outcome is in doubt | The tables |
| An entity another interceptor, a cascade on save, or change detection adds to the save after the tracker read it | That entity's tables |

Rows travel with the write: they are held by the transaction or the unit of work and announced with its commit, and a rollback drops them.

**How to see that a filter narrows.** `headless.live_queries.rows.ruled_out` counts each saved row that a stream's filter excluded. When it stays flat while `headless.live_queries.wakes` rises with every write to the table, the query or the column does not qualify and the stream is matching by table. Check the two lists above.

**Limits and costs.**

- **A fixed column must stay fixed.** EF Core enforces it for `SaveChanges` only. Changing the column with `ExecuteUpdate`, raw SQL, a trigger, or a model that maps it as changeable announces the table at that moment, but an entity a context loaded before the change keeps matching on the old value when it is saved later.
- **A key names one row for good.** If a row is deleted and another is inserted under the same key with different fixed columns while a context still holds the first, a later save of that entity matches on the first row's values. Database-generated keys and GUIDs never repeat; application-assigned keys must not be reused.
- **One process.** Matching by row changes which streams re-run, not who hears a write.
- **A comment in the SQL.** A query that qualifies carries `-- live-query-shape:<number>` above its SQL, in every execution, live or not. The number links the SQL to the analysis made when EF Core compiled the query.
- **A step per entity.** Every entity the context builds from a query passes through a materialization interceptor, which remembers the fixed columns of a tracked entity whose type has any. Other entities cost a check.
- **No rows without a reader.** A save keeps rows only for a table some open stream reads through a filter at that moment; otherwise it announces the table, at the cost of table-level tracking.
- **4,096 query shapes per model.** Past it, a newly compiled query matches by table. An application that builds queries dynamically without bound reaches it; one with a fixed set of queries does not.
- **Shared across hosts.** Two hosts in one process that use one model share the compiled queries and their analysis, as they share EF Core's query cache. Each host still matches only its own streams against its own writes.

### When a write is announced

A write is announced after it is durable, by whatever owns its commit:

| The write runs in | Announced |
| --- | --- |
| No transaction (`ExecuteUpdate`, `ExecuteDelete`, raw SQL) | When the command completes |
| `SaveChanges` with no transaction | After the transaction EF Core opens for the save commits, or after the save's single command completes |
| An EF Core transaction (`Database.BeginTransaction`) | When a tracked context commits it. A rollback, or a dispose without a commit, announces nothing |
| A Headless unit of work, begun on the context or on its connection | When the unit completes, after its commit. A rollback, an abandoned unit, or a commit the database refused announces nothing |
| A `TransactionScope`, or a transaction passed to `Database.EnlistTransaction` | When that transaction completes as committed. An aborted one announces nothing |

- **Several saves, one announcement.** Everything written inside one transaction or unit of work is announced together, once.
- **Contexts that share a transaction.** A second context that joined a unit of work, or adopted a transaction with `Database.UseTransaction`, has its writes announced with the owner's commit. Both context types must be tracked.
- **An in-doubt commit is announced.** When a commit throws after it may have reached the database (a unit of work that fails as `InDoubt`, an EF Core commit that throws, a `TransactionScope` that ends in doubt), the write may be stored, so the tables are announced. The cost is one re-run that may find nothing new.
- **A crash loses the announcement.** A process that dies between a commit and its announcement never announces it. This is accepted for one process: its subscribers died with it, and they receive a full result when they reconnect.
- **A transaction committed behind EF Core's back is not seen.** When a context adopts a `DbTransaction` with `UseTransaction` and the application commits that transaction itself, outside a unit of work, no commit is observed. Run the work in a unit of work, or announce the tables.
- **A failed or cancelled save announces nothing**, with one exception: a save whose command had already run outside a transaction of its own before the save threw is announced, because the write may be stored.
- **A rollback to a savepoint is not followed.** The tables written before it stay pending and are announced with the commit: an extra re-run, never a missed one.
- **`TransactionScope` on SQLite.** SQLite cannot enlist in a `TransactionScope`, and EF Core refuses the save unless `AmbientTransactionWarning` is ignored. With the warning ignored the write is stored at once and is still announced only for a completed scope. Do not combine the two.

The announcement runs on the thread that completed the commit. It stamps the keys and signals the subscriptions that read them; it runs no query and does no I/O.

### The read-only rule

A live query must not write through a tracked context. `SaveChanges` with a pending change, `ExecuteUpdate`, `ExecuteDelete`, and raw SQL that names a mapped table throw `InvalidOperationException` before anything is sent, with a message that names the table key and the rule. A live endpoint treats it as any exception from the handler: the host's error response when the stream opens, or a `g:live_query_run_failed` event on a re-run. Raw SQL that names no mapped table, such as a session setting, is allowed.

`ILiveQueryInvalidator.InvalidateAsync` inside a live query is refused the same way, with or without a tracked context: a query that announces a write makes its own run out of date, so it would run again after every run.

The rule covers a run, not the tasks it leaves behind. Once the query has returned, work it started and left running writes through a tracked context like any other code, and the write is announced.

### Configuration

| Option | Default | Effect |
| --- | --- | --- |
| `LiveQueriesEntityFrameworkOptions.MaxCachedCommands` | `1024` | How many SQL command texts the tracker remembers the tables of, per model. Past the limit the memory is emptied and fills again, which costs repeated searches and never a missed table. Must be at least 1. |
| `LiveQueriesEntityFrameworkOptions.RowLevelMatching` | `false` | Whether a saved row re-runs only the live queries whose filters it can pass. See [Row-level matching](#row-level-matching). |

Invalid values fail options validation when the host starts.

### Design and runtime behavior

- **Tracking never fails the application's write.** An exception while the tracker reads the change tracker, holds a write, or announces it is caught, logged as an error, and counted on `headless.live_queries.tracking.fallbacks`. The tables it knows are then announced whole, and the save or commit goes on. The read-only rule is the one exception that reaches the caller.
- **An error while recording a read fails the run.** Unlike a write, a query is not allowed to continue untracked, because it would stay stale without any sign. A query whose SQL names no mapped table is not an error: it records nothing.
- **Reads are recorded on the live query's own flow.** A query started on a detached flow, or through a context that is not tracked, records nothing; a run that records nothing at all fails with `LiveQueryHasNoDependenciesException`.
- **Pooled contexts carry nothing over.** Pending writes belong to the transaction or the unit of work, not to the context instance, so a context returned to the pool with an unfinished transaction announces nothing later.
- **One more change detection per save.** The tracker reads the change tracker before the save, which runs `DetectChanges` once more than EF Core does by itself. With `AutoDetectChangesEnabled` off it does not.
- **Other interceptors.** Tables an interceptor adds to a save after the tracker read the change tracker are still found, from the text of the save's commands. With row-level matching, an entity added or changed after that point has its tables announced whole.
- **A failure while reading a saved row announces the table.** It is counted and logged like any other tracking failure, and the save is not affected.
- **One process.** The limit at the top of this guide applies: only this process's writes are announced, and only to this process's subscriptions.
