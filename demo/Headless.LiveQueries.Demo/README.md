# Headless.LiveQueries.Demo

One host that shows live queries, row-level matching, conflict replay, and optimistic updates together, on an in-memory SQLite database. The [live queries guide](../../docs/llms/live-queries.md) is the reference; this demo is an example of it.

## Run it

```bash
dotnet run --project demo/Headless.LiveQueries.Demo                           # http://localhost:5290
dotnet run --project demo/Headless.LiveQueries.Demo --launch-profile https    # https://localhost:7290
dotnet run --project demo/Headless.LiveQueries.Demo --urls http://localhost:5291   # another port
```

Open the page in two windows. Over plain HTTP a browser allows six connections to one server and each open stream holds one, so use the `https` profile (HTTP/2, where streams share a connection) or two browsers.

## What it shows

| Feature | Where | What to try |
| --- | --- | --- |
| A live GET endpoint | `.WithLiveUpdates()` in `Program.cs` | `curl -N -H 'Accept: text/event-stream' http://localhost:5290/todos/stats`, then add a todo on the page. The same URL without the header answers once. |
| EF Core tracking | `UseEntityFramework<AppDbContext>()` | No handler calls `DependsOn` or `InvalidateAsync`. A commit through the context re-sends the queries that read what it wrote. |
| Row-level matching | `RowLevelMatching`, and the owner column fixed after insert in `AppDbContext.cs` | Set two windows to different owners. A todo added for owner 1 does not run owner 2's list again, and "Rows ruled out" rises. |
| Conflict replay | `IncrementCounterHandler.cs` | "Send 10 with retry" raises the counter by ten. "Send 10 without retry" loses most of the ten to a conflict (409): nine when all ten truly overlap, fewer in a browser that sends them over the few connections its open streams leave free. |
| Command acknowledgement | `UseHeadlessLiveCommands()`, and `wwwroot/headless-live.js` | With latency on, an added todo shows at once with a "saving" chip, and loses it in the same step as the server's result arrives. An empty title is rejected and rolled back. |
| Client discovery | `x-headless-live` in `/openapi/v1.json` | The page states no route: it reads the operations, and which of them are live, from the document. |
| Watching from server code | `TodoStatsLogger.cs` | The host logs the todo count after every change, through `ILiveQueries.WatchAsync`. |

`DemoSender.cs`, `LiveQueriesMeterReader.cs`, the `/demo/meter` endpoint, and everything under `wwwroot/` and `tools/` are demo code. The framework ships no browser client; an application writes its own from the protocol in the guide, or generates one from the OpenAPI document.

## Scripts

They need Node 22 or later and a running demo.

```bash
cd demo/Headless.LiveQueries.Demo

# TypeScript definitions of every operation, read from the OpenAPI document.
node tools/gen-client.mjs http://localhost:5290

# The optimistic timeline of the page's client, printed and checked, without a browser.
node tools/optimistic-check.mjs http://localhost:5290

# Query runs caused by 80 writes over 40 filtered streams.
node tools/bench-invalidation.mjs http://localhost:5290
```

To compare row-level matching with table-level matching, run the benchmark against a host started each way:

```bash
LiveQueries__RowLevelMatching=false dotnet run --project demo/Headless.LiveQueries.Demo
```

Both deliver the same 80 updates. At row level each write runs the one list it can change; at table level each write wakes all 40.

## Limits of the demo

- **One process.** Live queries reach the streams of the process that committed the write. Two instances of this demo would not update each other.
- **No authentication.** Every caller is the same anonymous user. A host with authentication calls `UseHeadlessLiveCommands()` after `UseAuthentication()`.
- **SQLite.** An ordinary SQLite transaction takes the write lock when it begins, so two requests never read the same row version and no version check is ever lost. The counter handler begins its unit of work read-uncommitted, the SQLite mode that reads without the lock, to produce the conflict PostgreSQL and SQL Server produce at their default isolation level.
