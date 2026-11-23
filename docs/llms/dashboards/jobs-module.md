---
domain: Dashboards
packages:
---

# Jobs module

> Part of [Dashboards](../dashboards.md). The Jobs module from `Headless.Jobs.Dashboard`: registration, options, object kinds, commands, API routes, views, and the migration from `MapHeadlessJobsDashboard`.

## Register the module

```bash
dotnet add package Headless.Jobs.Dashboard
```

`AddJobs()` adds the Jobs module (`Headless.Jobs.Dashboard`) under the id `jobs`. It needs `AddHeadlessJobs(...)` in the same host and takes no entity type arguments.

```csharp
using Headless.Jobs;

builder.Services.AddHeadlessJobs(jobs => jobs.AddModule<MyApp.JobsModule>());
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddJobs(jobs => jobs.OperatePolicy = "jobs.operate"));

app.MapHeadlessDashboard();
```

**Verify:** a signed-in viewer's `GET {base}/api/jobs/functions` (by default `/headless/api/jobs/functions`) answers `200` and lists the functions this host registers, and the shell's sidebar shows a Jobs tab.

| `JobsDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. |
| `ChangeOverlapWindow` | 5 s | How far back each change read reads again. Make it longer than the Jobs store's slowest single write. |
| `ChangePageSize` | 200 | The most changed rows one change read returns, 1 to 1000. |
| `TraceLimit` | 500 | The most runs the trace page lists for one correlation id, oldest first, 1 to 10,000. When more runs carry the id, the page shows `500+` and links to the runs list filtered by it. |

`AddJobs(Action<JobsDashboardOptions>)`, `AddJobs(Action<JobsDashboardOptions, IServiceProvider>)`, and `AddJobs(IConfiguration)` configure them.

## Object kinds and topics

`run` (a time job or a cron occurrence, by its id), `cron-job`, `function` (by name), and `node`. Stream topics are `jobs.run`, `jobs.cron-job`, and `jobs.cron-occurrences`; a change's projection is the run or cron job as its list row shows it, a cron job without its `lastRun`, and its version is the row's change marker in ticks. A deleted row goes out with no projection, so open views refetch it and show `This run no longer exists`; a view whose first fetch lands older than such a change refetches once more instead of showing the older row. Deleting a cron job from the dashboard sends one change on `jobs.cron-occurrences` whose id is the cron job's, however many occurrences it stored, and every open run list drops that cron job's rows in place without reading the list again; a list's exact count drops by the rows it held, and a list with pages still to load is read again for its count. Like every change a node publishes itself, it reaches the streams on the node that ran the delete; a deleted row never appears in the change read, so views on other nodes drop the rows when they next read the list. A cron job deleted through `IJobsManager` announces only the cron job, because whether its stored occurrences go with it depends on the store.

## Commands

Requeue, Cancel, and Delete need a reason and a confirmation (typed past 100 rows) and run over a filter as Operations. Edit, Duplicate, Force delete, and the cron job's Delete need a reason and a confirmation and run on one object at a time. A call on one object the rules refuse changes nothing and answers `409` with the reason, such as `Paused — resume first`.

| Command | On | Allowed when |
| --- | --- | --- |
| `jobs.requeue` | `run` | failed, cancelled, or skipped, and the requeue rules accept it |
| `jobs.cancel` | `run` | a queued or running time job with no cancel requested yet |
| `jobs.delete` | `run` | not running, and not a keyed time job |
| `jobs.run-now` | `cron-job` | not paused, and not running when the cron job forbids overlap; the done row's `reason` is the new run's id |
| `jobs.pause` / `jobs.resume` | `cron-job` | active / paused and not removed from code; each is the other's inverse |
| `jobs.edit-cron` | `cron-job` | not defined in code; arguments `expression` (required) and `timeZoneId` |
| `jobs.edit` | `run` | an idle unclaimed or queued time job that is not keyed and not a chain step; optional `executionTime` (ISO 8601 with offset), `request` (JSON, bound to the function's request type, refused when the stored contract version differs), `retries` (0–100), `retryIntervals` (JSON array of whole seconds); an argument left out keeps its stored value |
| `jobs.duplicate` | `run` | a finished standalone time job whose function this host registers under the stored contract version; the server copies the stored request, retries, and intervals into a new run with no business key; optional `executionTime`, not in the past, absent means now; the done row's `created` is the new run |
| `jobs.force-delete` | `run` | a running time job with cancel requested, not keyed and not in a chain; it removes the stored rows only, and the running code is not stopped; a job that finished after the read is refused |
| `jobs.delete-cron` | `cron-job` | not defined in code (a definition removed from code may be deleted) and no occurrence running; it removes the cron job with every stored occurrence, and the done row's `reason` is `N stored occurrences removed`; open run lists drop those occurrences at once; an occurrence started after the read refuses it |

An Operation's filter is `{ kind, functions, statuses, cronJobId, correlationId, fingerprint, from, before, actionableOnly }`. Requeue and Delete freeze on the store's latest change marker at confirm and write each row only while its marker is unchanged, so a run requeued by one chunk that fails again is not touched again; Cancel freezes on creation at confirm and needs no marker, because a running job advances its marker as it reports progress. The operator log records a filter's `correlationId` as `(redacted)`, and the module writes it in no log line.

## Attention and Overview

One item per function and failure fingerprint (`3 failed runs of billing.charge`), recommending `jobs.requeue` over the group's actionable failures; one item per registered function that no live node's run filter claims (`No live node runs billing.charge`), pointing at its `function` object. The Overview shows the share of runs that succeeded, failed runs, and runs that started later than the scheduler's fallback interval after they were due, each over the last 24 hours against the 24 before. The succeeded share is left out while no run finished in the last 24 hours, so an idle system does not read as 0 % succeeded, and compares with nothing, labelled `no runs finished in the previous 24 h`, while no run finished in the 24 hours before. The Overview's schedule strip gets one track per function that has cron jobs: its occurrences due in the last hour in their status's state role (`sched`, `enq`, `proc`, `ok`, `fail`, or `off`), and the next fires of its active cron jobs in the next hour; a past tick opens its run and a fire opens its cron job.

## Related and payload

A run relates to the other runs with its correlation id (`jobs.same-correlation`), its chain (`jobs.chain`), and its cron job (`jobs.cron-job`); a run with a stored W3C trace parent carries its trace id, so the run offers `Open in tracing tool` when the host set `SetTraceLinkTemplate`. Its payload is its stored request as JSON, or `Unsupported contract version N` when this host registers another version. A run and an attempt carry their exception as `exceptionType`, `exceptionMessage`, and `exceptionStack`: the module reads the JSON the executor stores (the outer exception's type and message, then its frames and every inner exception as a `Type: message` line with its own frames, up to five inner exceptions) into the three, and a reason the store wrote itself, such as `Lease lapsed while running!`, arrives as the message with no type or stack.

## API

The module's routes live at `{base}/api/jobs`, JSON with camelCase names and enums as strings. A route refuses bad input with the same `400` validation problem a command answers: `errors` names each refused field as the query or body spells it, with its messages (a chain step's field by its path, such as `children[0].function`), `rules` names the rule each field broke (`required`, `enum`, `minimum`, `maximum`, `maxLength`, `format`, `json`, or `invalid`), and `detail` repeats the first message. A malformed body is refused as the field `body` with the rule `json`, as a command's is. A query value that does not parse as its type (a GUID, date, number, or boolean) is refused by ASP.NET Core before the module runs, with its own `400`.

| Route | Answer |
| --- | --- |
| `GET runs?kind=&function=&status=&cronJobId=&fingerprint=&from=&before=&actionable=&search=&sort=&direction=&limit=&after=` | `{ items, count, countCapped, nextCursor }`: one keyset page of runs. `count` and `countCapped` come with the first page only, the one read without `after`; a later page leaves both out, so keep the first page's count. `sort` is `executionTime` (the default), `function`, or `status`, each followed by id. `direction` is `ascending` or `descending`; it defaults to `descending` for `executionTime` and to `ascending` for the other keys. `search` is a literal, case-insensitive substring of the function, id, business key, or description, so `%` and `_` match themselves; payloads are never searched. `from` is inclusive and `before` exclusive. `limit` goes up to 200 and the count is capped at 10,000. `after` takes `nextCursor`; a cursor sent with another sort, direction, filter, or search is refused as `after`, so read the first page again. Each run also carries `progressUpdatedAt` (absent until it reports progress), `retries`, and `retryIntervals` |
| `POST runs/search` | The `GET runs` answer for the runs of one correlation id. The body is `{ correlationId, … }` with the list's query fields as body fields (`kind`, `function` and `status` as arrays, `cronJobId`, `fingerprint`, `from`, `before`, `actionable`, `search`, `sort`, `direction`, `limit`, `after`). `correlationId` matches exactly and by ordinal comparison. It travels only in the body, never in a URL, so `GET runs` takes no `correlationId`. A body with no `correlationId` answers `400` with rule `required`, one over 200 characters with rule `invalid`, and a body that is not a JSON search with `body` rule `json` |
| `GET runs/{id}` | `{ run, children }`, or `404` |
| `GET runs/{id}/attempts` | `{ items: [{ number, node, startedAt, endedAt, outcome, exceptionType, exceptionMessage, exceptionStack, failureFingerprint }] }`, or `404` |
| `GET cron-jobs?function=&fires=` | `{ items }`: every cron job with its plain reading and its `lastRun: { id, status, executedAt }`, the latest occurrence that started or ended, by due time (see [Dashboard reads](../jobs/custom-persistence.md#dashboard-reads)). `id` is the occurrence, which opens as a run; `executedAt` is left out while it is in progress; `lastRun` is left out for a cron job that never ran, and a paused one keeps its own. The list reads every last run in one store read. With `fires`, each active one also carries that many next fires (at most 50), as a Function page draws them on its strip |
| `GET cron-jobs/{id}?fires=` | one cron job with its `lastRun` and its next fires (default 5, at most 50; none while paused), or `404` |
| `GET cron-preview?expression=&timeZoneId=&count=` | `{ timeZoneId, reading, nextFires, validationMessage }` for a draft schedule |
| `GET functions` | `{ items: [{ name, contractVersion, priority, maxConcurrency, requestType, cronExpression, claimedBy, succeeded, failed, running, waiting, lastExecutedAt, cronJobs }] }`, counts over 24 hours |
| `GET nodes` | `{ items: [{ identity, state, role, claimedFunctions, isLocal, maxConcurrency, running, runningCapped, lastHeartbeatAt, lastHostException, lastHostExceptionAt }] }`: live nodes; without node membership, this host. `isLocal` marks the node serving the request. `maxConcurrency` is the node's host limit, absent for a peer that publishes none. `running` counts its in-progress runs, capped at 10,000 with `runningCapped`. `lastHeartbeatAt` is the store-clock time of its latest accepted heartbeat, absent without membership (on Redis a node that left reports a backdated time). `lastHostException` and `lastHostExceptionAt` appear only on the serving node's row, in memory and lost on restart; a peer's exception is known only on that node |
| `GET series/daily?from=&to=&zone=&kind=&cronJobId=` | `{ zone, from, to, asOf, exact, days: [{ date, start, end, counts }] }`: one bucket per local day from `from` to `to`, both required and inclusive (`yyyy-MM-dd`), at most 92 days, in the IANA `zone` (default: the scheduler zone). `start` and `end` are the day's UTC bounds, so a daylight-saving day is 23 or 25 hours long. Runs are placed by execution time, not by when they ended as the Overview figures count them, so a future day counts the time jobs and occurrences scheduled on it. `counts` names every status with its count, `0` included, and is `null` (written, never left out) for a day the store could not read; `exact` is `false` when any day is `null`, so show a share only when it is `true`. `kind` (`TimeJob` or `CronJobOccurrence`) and `cronJobId` narrow the runs. A missing date, `to` before `from`, more than 92 days, an unknown zone, or an unknown kind answers `400` |
| `POST time-jobs` | an operator action: `{ function, executionTime, timeZoneId, request, description, retries, retryIntervals, children: [{ ..., runCondition }] }` answers `201 { id }`; a time without an offset is read in `timeZoneId`, else the scheduler's zone, and one a daylight-saving gap skips there, such as 02:30 on the night the clocks jump from 02:00 to 03:00, is refused at that step's `executionTime` with rule `invalid`. `function` is required and registered on this host in every step, `retries` is 0 to 100, each of `retryIntervals` is zero or more, and `request` is at most 65,536 characters and binds to the function's request type; an unknown `timeZoneId` is refused |
| `POST cron-jobs` | an operator action: `{ function, expression, timeZoneId, request, description, retries, retryIntervals }` answers `201 { id }`; each occurrence retries as a time job with the same fields does. It checks the fields a time job body checks, and refuses an `expression` that does not parse or never fires again as `expression` and a zone this host does not know as `timeZoneId` |

## Views

The package embeds the module's front end, so the Jobs tab's sidebar reaches these views:

| View | Shows | Acts |
| --- | --- | --- |
| Dashboard (`{base}/jobs`) | The stat strip (the Idle, Queued, and In progress counts the sidebar shows, then the Overview's succeeded share, failed runs, and late starts over 24 hours with their comparisons), the runs in progress with their reported progress and, in the card's caption, how many workers the live nodes have (`2 running · 14 workers`, said only while every live node published its max concurrency), the next fires of the active cron jobs, and the Jobs attention items | `Cancel` per running run; `Add time job`; `Add recurring job` when no cron job exists |
| Runs (`runs`, each state at `runs?status=<JobStatus>`) | The stat strip, then one live list of time jobs and occurrences, titled by its state with its served count and one sentence on what the state holds, filtered in the URL by kind, function, status, failure fingerprint, and cron job, and by correlation id through history state (`{ "correlationId": "<id>" }`), which a link such as the trace page's `N+` hands it so the id never sits in the URL. The correlation filter shows as a chip to clear, survives Back, Forward, and every other change to the URL, and a link that puts `correlationId` in the URL opens the list unfiltered. One `Due` range with presets filters every list by when each run is due (its execution time), kept as `?range=24h` for a preset, which works out its window when the page opens so a shared link stays relative, or as `?from=2026-11-01&to=2026-11-04` days; a state list offers no range control and shows a range a link set as a chip to clear. The search (`?q=`, over function, id, business key, and description, never a payload) and the sort (`?sort=function&direction=descending`, the server's own names) are in the URL too, so a link or a reload shows the same list; a value the view cannot read falls back to no range or the newest-first order, and Clear filters keeps the status and the sort. The Failed list adds a chip per failure group, labelled by function with its count, and by the exception type of a loaded run of the group once there is one (`export.ledger · TimeoutException`), that filters by that function and its fingerprint, and reads each run by its reason: the exception's type without its namespace, then its message. Every list counts a run's attempts out of the ones it may take (`2 of 4`), blank for a run no node has picked up and for one that never ran: a skipped run, or one cancelled before a node picked it up. The `status` reads case-insensitively, and a value that names no status lists every run. The open run (`?open=<id>`) docks under the list: Overview (its fields in groups, and history from its attempts), Exception for a run that stored one (a failed run opens on it, with the whole stack), Arguments for a run that stored a payload, and Chain for a step or a run with steps after it. Requeue keeps its label; the other actions are icons with tooltips. | `Requeue`, `Cancel`, and `Delete` on the open run and on the checked runs the server allows, each refused row tagged with its reason; over every matching run as an Operation; `Add time job` and `Add chain` open one form in the docked panel (`?create=time-job` or `?create=chain`), switched by `Single job | Chain` without losing what was typed, a chain's later steps beside the first with the step each runs after and its run condition |
| Run page (`runs/{id}`) | The run panel as its own page | The same run actions |
| Recurring jobs and cron job page (`cron-jobs`, `cron-jobs/{id}`) | The stat strip, then each cron job's function, description, expression with its plain reading, last run, next run, or a `Paused` tag, and how its function's runs went in the last 24 hours (`6 succeeded · 2 failed`, the failed part opening the function's failed runs). Those counts cover every run of the function, not only this schedule's occurrences. The last run is the latest occurrence's status as a dot and a word, opening that run, then when it ended (`Succeeded · 12 min. ago`); one in progress shows its status alone, and a cron job that never ran shows a dash. A live change of a cron job carries no last run, so the list is read again once the occurrence changes that can move one settle. The open one (`?open=<id>`) docks under the list: Schedule (its fields beside the next five fires in its zone) and Occurrences (the last 24 hours as outcome bars per hour, then each occurrence newest first). The page is the same panel on its own. | `Run now`, `Pause` / `Resume`; `Add recurring job` opens its form in the docked panel (`?create=cron-job`); the expression and zone edit in place, showing the server's reading and next fires before they commit, and stay read-only for a cron job defined in code |
| Functions and function page (`functions`, `functions/{name}`) | The stat strip, then each function's kind (`cron` with its declared expression, or `time`), request type, priority, running count over its concurrency limit, the live nodes whose run filter claims it (or a `no node runs it` tag), and its 24-hour counts. The open one (`?open=<name>`) docks under the list: Overview (its contract version and facts beside where it runs and the recurring jobs that fire it) and Runs (its latest runs). The page is the same panel on its own. | `Add time job`, `Add recurring job` |
| Servers (`nodes`) | The stat strip, then each live node's role, liveness, busy workers against its max concurrency as a meter (`2 of 14 busy`; a node that published no max concurrency shows its running count alone), and the functions its run filter claims; the open one (`?open=<identity>`) docks under the list with the same facts and links to its functions. The server serves no scheduler state, queues, or registration time per node, so the view shows none. | None |

A run with a pending cancel reads `Cancelling…` until the job stops, and a run an action finished on reads `Requeuing…`, `Cancelling…`, or `Deleting…` until its change arrives. `T` on an open run, and `Open trace` beside its correlation id, open the shell's trace page at `{base}/trace#<id>`. A run with no correlation id shows `Not recorded`, and both are disabled with `No correlation id`. `G R` opens the runs list. The palette adds `Go to runs`, `Requeue failed runs`, `Add time job`, `Add chain`, and `Add cron job`, which opens `Add recurring job`. A viewer sees every action disabled with `Needs operate permission`. Per-node scheduler state, and starting or stopping a scheduler, are not part of the module yet.

## Migrate from `MapHeadlessJobsDashboard`

The Jobs Vue dashboard, its SignalR live-update hub, and its endpoints under `/jobs/dashboard` are removed. The Jobs module replaces them inside the shell, with live changes over the shell's stream. To migrate a host:

1. Delete `AddDashboard(...)` from the `AddHeadlessJobs(...)` delegate, and delete `app.MapHeadlessJobsDashboard()`.
2. Add `AddJobs()` to `AddHeadlessDashboard(...)`, and map the shell with `app.MapHeadlessDashboard()` if the host does not already.
3. Move authentication to the shell: its `WithHostAuthentication`, `WithBasicAuth`, `WithApiKey`, or `WithNoAuth` call covers every module. For separate Jobs rights in host mode, set `ViewPolicy` and `OperatePolicy` on `JobsDashboardOptions`.
4. Point bookmarks and links at `{shell base}/jobs` (by default `/headless/jobs`), the Jobs Dashboard; the runs list is at `{shell base}/jobs/runs`. Nothing serves `/jobs/dashboard` any more.
5. Remove the `SetBasePath`, `SetBackendDomain`, `SetCorsOrigins`, `SetCorsPolicy`, and `ConfigureDashboardJsonOptions` settings: the shell is same-origin, serves every module under its own base path, and owns its JSON. Replace `SignOutHeadlessDashboardAsync("Headless.Jobs.Dashboard")` in the host's sign-out with `SignOutHeadlessDashboardAsync()`, which ends the shell's session.

```csharp
using Headless.Jobs;

builder.Services.AddHeadlessJobs(jobs => jobs.AddModule<MyApp.JobsModule>());
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => { host.ViewPolicy = "ops.view"; host.OperatePolicy = "ops.operate"; })
    .AddJobs());

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapHeadlessDashboard();
```
