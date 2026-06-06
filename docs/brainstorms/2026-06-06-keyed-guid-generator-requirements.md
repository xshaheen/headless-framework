# Backend-correct GUID generation via keyed DI

**Date:** 2026-06-06
**Status:** Design settled. (Implementation already applied to the working tree — see "Current state" at the end.)
**Scope:** Standard / technical-architectural.

## Problem

`IGuidGenerator` is registered as a single, container-wide service, but the *correct* GUID
ordering depends on the **database a key is persisted into** — and different backends sort
GUID bytes differently. Every module historically did
`TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>()` (the SQL Server comb),
including the PostgreSQL packages, which by the framework's own doc-comment should not use it.

Two distinct defects:

1. **Wrong default for non-SqlServer backends.** PostgreSQL message storage wrote message ids
   with the SQL Server comb (`PostgreSqlDataStorage.cs` → `@Id` parameter), fragmenting the
   `uuid` primary key.
2. **`TryAdd` first-wins collision.** With one global `IGuidGenerator` per container, a process
   hosting multiple backends (SqlServer messaging + Postgres locks + EF on a third DB) gets
   whichever module registered first — there was no way for each persisted store to use its own
   correct ordering simultaneously.

### Where it actually matters

GUID ordering only matters where the GUID becomes a **persisted clustered / primary key**
(index-insert locality). That is **ORM entity ids** and **Messaging outbox/inbox message ids**.

It does **not** matter for **DistributedLocks** — the GUID there is an ephemeral lease id
(`ConnectionScopedDistributedLock.cs` → `guidGenerator.Create().ToString("N")`), never a
clustered key. Any unique GUID is equivalent. Locks are therefore out of scope for *correctness*
and only need to keep compiling.

**In-scope success criterion:** every persisted clustered/primary key gets the ordering its
database sorts on, correct even when one process hosts multiple backends.

## Decisions (with rationale)

| # | Decision | Why |
|---|----------|-----|
| 1 | **Keyed DI**, not a global service or a per-backend registry of injected keys. | The storage knows its own backend, so it names the strategy it needs; keyed singletons let all strategies coexist in one container, eliminating the first-wins collision. |
| 2 | **Collapse the generator classes to an enum** `SequentialGuidType { Version7, SqlServer }` behind one `SequentialGuidGenerator(type)`. | Greenfield; the per-backend choice becomes a value, not a type. |
| 3 | **`Version7` = `Guid.CreateVersion7()`** for Postgres / MySQL-binary / Oracle / general use. | RFC 9562 v7 is time-ordered in standard big-endian byte order; Postgres `uuid` sorts that way and Npgsql writes RFC order, so v7 is genuinely sequential there. Built-in (.NET 9+, repo targets .NET 10), no custom code. |
| 4 | **`SqlServer` keeps the existing comb** (`SequentialGuid.NextSequentialAtEnd`). | v7 fragments on SQL Server: `uniqueidentifier` sorts by the node block (bytes 8–15), where v7 puts random data; the comb puts the monotonic counter there. The existing code is byte-for-byte EF Core's `SequentialGuidValueGenerator` — the canonical implementation. v7-everywhere was rejected on this evidence. |
| 5 | **Unkeyed default = `Version7`**, used by general/backend-agnostic consumers. | Modern cross-platform default; matches DistributedLocks' existing move to v7. |
| 6 | **EF Core deferred** — keep its current SQL Server-comb default (swap type name only). | "Skip EF for now." Flipping EF's default to v7 would silently regress SQL Server clustered keys — the opposite of skipping. EF needs the same provider-derived treatment as a follow-up. |
| 7 | **No DI dependency added to `Headless.Extensions`.** Register the keyed pair inline in `Messaging.Core` (the only site that needs it). | 13 of 16 `Headless.Extensions` dependents (incl. 5 contract-only `*.Abstractions` packages) have no DI dependency; adding `DI.Abstractions` would pollute them. Only messaging storages resolve by key, so keyed registration is needed in exactly one place — no shared helper required. |

### Why backend-derived correctness, in one line
The ordering is a property of the destination database's index sort order, not a global
preference — so the decision must live next to the backend (resolved by key at the storage),
making a wrong ordering unrepresentable rather than relying on a global knob being wired right.

## Target shape

```csharp
// Headless.Extensions (DI-free)
public enum SequentialGuidType { Version7, SqlServer }

public sealed class SequentialGuidGenerator(SequentialGuidType type) : IGuidGenerator
{
    public Guid Create() => type switch
    {
        SequentialGuidType.Version7  => Guid.CreateVersion7(),
        SequentialGuidType.SqlServer => SequentialGuid.NextSequentialAtEnd(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
```

```csharp
// Messaging.Core/Setup.cs — keyed pair + unkeyed default
services.TryAddKeyedSingleton<IGuidGenerator>(SequentialGuidType.Version7,  static (_, _) => new SequentialGuidGenerator(SequentialGuidType.Version7));
services.TryAddKeyedSingleton<IGuidGenerator>(SequentialGuidType.SqlServer, static (_, _) => new SequentialGuidGenerator(SequentialGuidType.SqlServer));
services.TryAddSingleton<IGuidGenerator>(new SequentialGuidGenerator(SequentialGuidType.Version7));
```

```csharp
// Storage ctors self-declare the key they need
PostgreSqlDataStorage(..., [FromKeyedServices(SequentialGuidType.Version7)]  IGuidGenerator g, ...)
SqlServerDataStorage (..., [FromKeyedServices(SequentialGuidType.SqlServer)] IGuidGenerator g, ...)
// InMemoryDataStorage → unkeyed default (no persistence)
```

## Implementation steps

**Core types (`Headless.Extensions`)**
1. `Abstractions/IGuidGenerator.cs` — add `SequentialGuidType` enum + `SequentialGuidGenerator(type)`; delete `Version7GuidGenerator`, `SequentialAtEndGuidGenerator`, `SequentialAsStringGuidGenerator`, `SequentialAsBinaryGuidGenerator`.
2. `Core/SequentialGuid.cs` — delete `NextSequentialAsString`, `NextSequentialAsBinary`, `_GetNextSequentialAsBinaryBytes`; keep `NextSequentialAtEnd`; update doc.

**In-scope fix (messaging)**
3. `Messaging.Core/Setup.cs` — replace the single `TryAddSingleton<IGuidGenerator, …>` with the keyed pair + unkeyed `Version7` default.
4. `Messaging.Storage.PostgreSql/PostgreSqlDataStorage.cs` — `[FromKeyedServices(Version7)]` on the ctor param (+ `using Microsoft.Extensions.DependencyInjection;`).
5. `Messaging.Storage.SqlServer/SqlServerDataStorage.cs` — `[FromKeyedServices(SqlServer)]` (+ using). InMemory unchanged (uses unkeyed default).

**Keep-compiling swaps (no behavior change unless noted)**
6. `Api.ServiceDefaults/Setup.cs` — unkeyed default → `Version7` (was SqlServer comb; this is the intended general-default change).
7. `Orm.EntityFramework` (`SetupEntityFramework.cs`, `Contexts/HeadlessEntityIdValueGenerators.cs`, `Extensions/DbContextOptionsBuilderExtensions.cs`) — swap deleted type → `SequentialGuidGenerator(SqlServer)`, **preserving** EF's SQL Server default; add migration TODO.
8. `DistributedLocks` Core / Postgres / SqlServer / semaphore / reader-writer — swap to `SequentialGuidGenerator(Version7)` (lease ids; ordering irrelevant, unified for consistency).

**Tests** — migrate all references (greenfield): `new SequentialAtEndGuidGenerator()` → `SequentialGuidGenerator(SqlServer)`; `SequentialAsStringGuidGenerator` registrations → instance form with `Version7`; remove the `NextSequentialAsString`/`NextSequentialAsBinary` test methods from `SequentialGuidTests`.

**Docs** — sync `docs/llms/core.md` + `src/Headless.Extensions/README.md` (public API change: types removed, enum added) per `docs/authoring/AUTHORING.md`.

## Follow-ups (explicitly deferred)

- **EF Core** — derive the GUID strategy from the configured provider (Npgsql → `Version7`, SqlServer → `SqlServer`) instead of a fixed default, the same way messaging storages resolve a keyed generator. TODO left in `SetupEntityFramework.cs`.
- **Permissions / Features / Settings** entity-id stores — currently inherit the unkeyed `Version7` default independently of their backing store; migrate them to depend on the store's backend (keyed/derived) so SQL Server clustered keys don't fragment.

## Open questions

1. For a future MySQL-as-text or non-native-`uuid` Postgres column, a third (string-ordered) strategy may be needed; deferred until such a provider exists (YAGNI).
2. Confirm on-disk index order for Npgsql + `Guid.CreateVersion7()` against a real `uuid` column (round-trip in-app is not proof) before relying on it in production.

## Current state of the working tree

All implementation steps above (core types, messaging, keep-compiling swaps, test migration)
are **already applied** on `main` in the working tree, alongside unrelated in-progress
coordination changes. Verified green: `Headless.Extensions` and all 7 affected source projects
build with 0 errors/0 warnings; test-project compile-check was interrupted. **Not committed.**
Docs sync (step "Docs") and the follow-ups are **not** done.
