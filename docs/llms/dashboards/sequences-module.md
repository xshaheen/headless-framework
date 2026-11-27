---
domain: Dashboards
packages:
---

# Sequences module

> Part of [Dashboards](../dashboards.md). The Sequences module from `Headless.Sequences.Dashboard`: registration, options, the read-only endpoints and their answers, the counter id, and why the module has no command.

## Register the module

```bash
dotnet add package Headless.Sequences.Dashboard
```

`AddSequences()` adds the Sequences module under the id `sequences`, in the Platform product, with a Sequences sidebar entry whose count is the number of stored counters. It reads through the `ISequenceInspector` that `AddHeadlessSequences(...)` registers, so a host without that call fails to start with a message naming it.

```csharp
using Headless.Dashboard;
using Headless.Sequences;
using Headless.UnitOfWork;

builder.Services.AddUnitOfWork();
builder.Services.AddHeadlessSequences(setup =>
{
    setup.UsePostgreSql(builder.Configuration.GetConnectionString("App")!);
    setup.Policy("invoice", new SequencePolicy
    {
        Mode = SequenceMode.GapFree,
        Reset = SequenceReset.Year,
        Format = "INV-{yyyy}-{seq:D6}",
    });
});
builder.Services.AddHeadlessDashboard(dashboard => dashboard
    .WithHostAuthentication(host => host.ViewPolicy = "ops.view")
    .AddSequences());

var app = builder.Build();
app.MapHeadlessDashboard();
```

**Verify:** a signed-in viewer's `GET {base}/api/sequences/summary` (by default `/headless/api/sequences/summary`) answers `200` with `total` equal to the number of stored counters, and the Platform tab's sidebar shows Sequences with that count.

| `SequencesDashboardOptions` member | Default | Description |
| --- | --- | --- |
| `ViewPolicy`, `OperatePolicy` | the shell's | As for every module. The module has no operate action, so only `ViewPolicy` guards a route. A white-space name fails options validation at startup. |

`AddSequences(Action<SequencesDashboardOptions>)`, `AddSequences(Action<SequencesDashboardOptions, IServiceProvider>)`, and `AddSequences(IConfiguration)` configure them.

The screen shows every tenant's counters, because the inspector reads across tenants. Give `ViewPolicy` only to operators who may see every tenant's numbering.

## Read-only

The module offers no command and maps no route that changes a counter. Advancing or resetting a production counter risks a duplicate or a skipped document number, and the inspector has no write member. Every route reads through `ISequenceInspector`, which never issues, reserves, or changes a number. Do not add a "next number" button that calls `NextAsync`: on a gap-free counter it leaves a real gap.

## Counter ids

A counter's id is its tenant id, name, and partition, each escaped with `Uri.EscapeDataString` and joined by `/`: `acme/invoice/2026`, or `/ticket/` for a host counter with no partition. A `/` or `%` inside a part stays escaped, so every key has one id. The id travels as the `id` query value, never a path segment.

| Object kind | Id | Opens |
| --- | --- | --- |
| `sequence-counter` | `{tenant}/{name}/{partition}`, escaped | `{base}/sequences?open=<id>`, the list with the counter's panel |

The shell's object search resolves an exact id to its counter and answers nothing for a fragment of a name. It finds the counter among the newest 1,000 partitions of its name.

## API

Every route is a `GET` under the module's view policy.

| Route | Answers |
| --- | --- |
| `{base}/api/sequences/summary` | `{ total, fast, gapFree, reported, capped, current, currentCapped, nextResetAt }` for the scope and search. `current` counts the counters in the partition their policy chooses now. `nextResetAt` is the earliest next reset across every registered policy and the default policy, whatever the filters, or `null` when none resets. |
| `{base}/api/sequences/counters` | `{ items, nextCursor }`: one page of counters in key order (tenant, name, partition). Pass `nextCursor` back as `after` for the next page; it is `null` on the last. |
| `{base}/api/sequences/counter?id=<id>` | One counter, or `404` when it is not among the newest 1,000 partitions of its name. |
| `{base}/api/sequences/counter/partitions?id=<id>` | `{ items, nextCursor }`: every partition of the counter's name in its tenant, newest first. Pass `nextCursor` back as `before`. |

Query parameters:

| Parameter | Routes | Values |
| --- | --- | --- |
| `scope` | `summary`, `counters` | `host` for the host scope, `tenant` for one tenant. Left out, every tenant. |
| `tenant` | `summary`, `counters` | The exact tenant id; required with `scope=tenant` and refused otherwise. |
| `q` | `summary`, `counters` | Text the name or the tenant id contains, ignoring ASCII case only, at most 128 characters. |
| `mode` | `counters` | `Fast`, `GapFree`, or `Reported`, in any case. A number is refused. |
| `current` | `counters` | `true` for only the partition each policy chooses now; a policy that never resets keeps every partition. |
| `limit` | `counters`, `counter/partitions` | 1 to 200, default 50; a value outside the range is clamped. |

A counter reads `{ id, tenantId, isHost, name, partition, value, updatedAt, lastNumber, isCurrentPartition, nextResetAt, mode, reset, policySource, policy }`. `tenantId` and `partition` are empty for the host scope and for no partition. `policySource` is `registered` when `SequencesOptions.Policies` holds the name and `default` when it numbers with the default policy. `policy` is `{ start, step, mode, blockSize, reset, format, fiscalYearStartMonth, timeZone }`, with `timeZone` as the zone id. `lastNumber`, `nextResetAt`, and `format` are written as `null` when they have no value: a reported counter or a policy with no format has no `lastNumber`.

- A filter, cursor, or id the module cannot read answers `400` naming the field (`mode`, `scope`, `tenant`, `current`, `limit`, `after`, `before`, `id`).
- A key part the inspector refuses, such as a 200-character search, answers `400` on the field that carried it, with the inspector's message.
- Any other failure of the counter store answers `503` whose `detail` is `The sequence store is unavailable.`, and is logged. The answer carries no key text or SQL.
- A cursor is a position in key order, not a filter: reusing it under another filter starts that filter's list after the key.

## The screen

Sequences states the stored counters, the counters in their current partition, the gap-free, fast, and reported counts, and the earliest next reset, then lists the counters with tenant (Host for the host scope), name, mode, reset, partition, value, last issued number, next reset, and last update. A counter under an unregistered name reads Default policy beside its mode, and a closed partition reads Closed. The mode chips keep their counts while one is pressed, because the summary leaves the mode and current filters out.

The scope filter is All, Host, or one exact tenant id. The inspector serves no tenant list, so the id is typed and submitted; until then the list stays as it was and nothing is read. Every filter lives in the URL, and `?open=<id>` opens a counter's panel: its value, last issued number, partition, last update, and next reset, the policy fields, a read-only note, and the partitions of its name. For a fast policy with a block size above 1, the panel notes that the value is the end of the last reserved block.

The inspector has no change feed, so the screen reads again on Refresh and when the window regains focus, and says when it last read. A failed read shows the error with Retry and never a zero count.

On SQL Server the inspector skips a counter an open gap-free unit is writing (`READPAST`), so a page can miss it until the unit ends. On SQLite with the default rollback journal a read waits while a writer commits; set `PRAGMA journal_mode=WAL` on the database. See [Inspecting counters](../sequences.md#inspecting-counters).
