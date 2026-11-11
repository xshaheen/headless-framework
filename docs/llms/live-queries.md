---
domain: Live Queries
packages: LiveQueries.Abstractions, LiveQueries, LiveQueries.Endpoints
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

An HTTP API does not call `ILiveQueries`. It marks a GET endpoint with `.WithLiveUpdates()` from [Headless.LiveQueries.Endpoints](#headlesslivequeriesendpoints), and a browser or any HTTP client subscribes with an `Accept` header.

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
- Make an endpoint live only with `.WithLiveUpdates()` on a `MapGet` call. Do not write a second streaming endpoint beside the ordinary one: the same URL answers both, by `Accept`.
- A live endpoint handler is read-only and returns materialized values (a list or an object, not a deferred query). It runs again after every relevant change, for every open stream.
- Do not put a token in the query string to authenticate a stream. A stream authenticates with the cookies or headers of the request that opened it, and every re-run authenticates and authorizes that request again.
- Serve live endpoints over HTTP/2. Browsers allow six HTTP/1.1 connections per origin, and each open stream holds one.
- On reconnect a client receives a full result. `Last-Event-ID` is ignored; do not build resumption on it.

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

`UseEndpoints` has the usual overloads: none, `IConfiguration`, `Action<LiveEndpointsOptions>`, and `Action<LiveEndpointsOptions, IServiceProvider>`. The host fails at startup when `IProblemDetailsCreator` is not registered. `WithLiveUpdates()` throws `InvalidOperationException` when the endpoints are built, naming the endpoint, for an endpoint that accepts any method but GET and for a host that did not call `UseEndpoints`.

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

event: failure
id: 9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d
data: {"type":"…","title":"Forbidden","status":403,"detail":"…","error":{"code":"g:live_query_unauthorized","description":"…"}}
```

| Part | Meaning |
| --- | --- |
| `retry:` | Sent once, first. `LiveEndpointsOptions.ReconnectDelay` in milliseconds. |
| `event: result` | The endpoint's response body, on one `data:` line even when the host's serializer indents. |
| `event: failure` | A problem-details body. It is the last event; the server then ends the response. |
| `id:` | The update's opaque version token. Compare for equality only. |
| `: heartbeat` | A comment line, written whenever `HeartbeatInterval` passes with nothing sent. `EventSource` ignores it. |

Newline-delimited JSON (`Accept: application/x-ndjson`), for callers that read a plain stream. One object per line:

```text
{"kind":"result","version":"5f1c0c1d8a4e4b0f9d2a7c3e6b1f0a9d","value":[{"id":1,"title":"milk"}]}
{"kind":"heartbeat"}
{"kind":"failure","version":"9a8b7c6d5e4f3a2b1c0d9e8f7a6b5c4d","value":{"title":"Forbidden","status":403,"error":{"code":"g:live_query_unauthorized","description":"…"}}}
```

A client ignores a `kind` it does not know. Both formats send `Cache-Control: no-cache, no-store` and `X-Accel-Buffering: no`, and turn response buffering off, so each update leaves when it is written. `UseHeadless` does not buffer either format: neither media type is in its response-compression list.

A re-run whose result equals the last one sent sends nothing. A client that reconnects opens a new stream and receives a full result; `Last-Event-ID` is ignored.

Error codes, as constants on `LiveEndpointsErrorCodes`:

| Code | Sent | Meaning |
| --- | --- | --- |
| `g:live_query_no_dependencies` | 500 response when the stream is opened; failure event later | A run recorded no dependency, so no change could ever update the stream. |
| `g:live_query_run_failed` | Failure event | A re-run threw (`status` 500) or answered outside 200 to 299 (`status` is that status). The exception is logged, never sent. |
| `g:live_query_unauthorized` | Failure event | A re-run found the caller no longer authenticated (`status` 401) or no longer authorized (`status` 403). |
| `g:live_query_stream_limit_reached` | 429 response with `Retry-After` | The caller or the host already holds the allowed number of streams. |

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
- **Authentication and authorization are checked on every run.** A run authenticates the default scheme again, which is what notices an expired token, and assigns the fresh principal to the run's request, so `ICurrentUser` and `HttpContext.User` in the handler are the run's. It then evaluates what the authorization middleware evaluates: `[Authorize]` data, `AuthorizationPolicy` metadata, requirement data, and the fallback policy when the endpoint has none of them. `AllowAnonymous` skips the check. A refusal ends the stream with a failure event.
- **A stream authenticates with what the opening request carried.** Cookies and headers are sent once, when the stream opens, and every run authenticates them again. There is no query-string token. A client that renews its token opens a new stream.
- **Tenancy.** Runs execute on the opening request's own execution flow, so the ambient tenant the tenancy middleware set stays in effect; a stream of tenant B re-runs under tenant B whoever wrote. A run whose freshly authenticated principal carries a different `tenant_id` claim than the principal that opened the stream, or none, is refused, and the stream ends with a `g:live_query_unauthorized` failure. With catalog identifier resolution, a principal whose tenant claim stops matching the identifier is refused the same way. A host that keeps the tenant in a claim type of its own gets neither comparison on endpoints without the catalog check; there the stream keeps the tenant it opened under.
- **The first run decides whether there is a stream.** A first run that answers outside 200 to 299 is sent to the client as it is (status, headers, body). A first run that throws takes the host's ordinary error handling. A first run that records no dependency is refused with a 500 problem. None of them opens a stream.
- **A failed re-run ends the stream.** The client receives one failure event and the response ends. The host keeps serving; the client may reconnect.
- **Limits gate the stream, nothing else.** The minimum interval and the re-run limit make a stream's own loop wait. No run ever executes on a writer's flow or a shared worker.
- **Host shutdown.** When the host begins to stop, every open stream ends and the response completes normally. A server-sent events client reconnects after `ReconnectDelay`.
- **`IHttpContextAccessor`.** Inside a run it returns the run's request. For a streaming request, code outside the endpoint that reads the accessor after the first run (response middleware, for example) reads `null`; use the `HttpContext` it was given.
- **HTTP/2.** Each open stream holds one connection on HTTP/1.1, and browsers allow six per origin. Serve live endpoints over HTTP/2, where streams share a connection.
- **One process.** The limit at the top of this guide applies: a change announced in another process does not reach these streams.

### Metrics

Live endpoints add two instruments to the `Headless.LiveQueries` meter. An open stream is one of its active subscriptions.

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
            await httpContext.Response.WriteAsync($"{update.Kind}|{update.Version}|{payload}\n", cancellationToken);
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
- `Payload` is the endpoint's response body, unchanged. It contains line breaks when the host's serializer indents; a line-based format removes them between tokens, as the built-in transports do, and the sample above does not.
- The transport starts the response: content type, cache headers, and flushing after each update. `Vary: Accept` is already set.
- `cancellationToken` is cancelled when the client disconnects or the host stops. Return or let `OperationCanceledException` leave.
- A heartbeat is the transport's to write. Read `IOptions<LiveEndpointsOptions>.Value.HeartbeatInterval` and wait real time.
- New members of `ILiveTransport` arrive as default interface members, so a transport keeps compiling across versions.
