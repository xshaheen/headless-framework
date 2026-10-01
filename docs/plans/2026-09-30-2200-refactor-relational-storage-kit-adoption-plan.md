---
title: Relational Storage Kit Adoption - Plan
type: refactor
date: 2026-09-30
topic: relational-storage-kit-adoption
artifact_contract: x-unified-plan/v1
artifact_readiness: ready
product_contract_source: maintainer interview
execution: code
---

# Relational Storage Kit Adoption - Plan

## Goal Capsule

- **Objective:** each relational feature's store is written once against a small SQL dialect, decides every guarded write with one fenced conditional statement, refuses keys and instants that some engine would store differently, and is checked step by step against a reference model.
- **Means:** a runtime kit in `Headless.Sql.*` (statement shapes, fenced results, portable text and time, error classification, retry and enlistment helpers), feature stores rewritten onto it, and a seeded differential oracle per migrated feature.
- **Product authority:** the framework maintainer. Greenfield: public APIs, schemas, and stored types change in place with no shims or migrations.
- **Depends on:** the schema runner (#1037), which owns every feature's DDL, initialization, verify mode, and deploy scripts. The kit renders no DDL.
- **Open blockers:** none.

---

## Product Contract

### Problem Frame

Every relational feature ships a PostgreSQL package and a SQL Server package that are near copies. The copies diverge by accident and each defect is fixed per provider. The Fencing proof of concept (#1034) showed the cost and the payoff:

- A seeded oracle found that call validation accepted keys no provider stores unchanged: PostgreSQL fails on NUL, and a lone surrogate is refused by Npgsql while SqlClient silently rewrites it to U+FFFD, merging distinct keys on SQL Server.
- Lease durations were stored up to 9 ticks shorter on PostgreSQL than on SQL Server and in memory.
- One relational lease store replaced 999- and 1,064-line twins with every existing test passing, with one engine difference crossing into the store (PostgreSQL's lost insert race).

### Decisions

- The schema runner (#1037) owns DDL. Contributions stay per-dialect SQL files; the kit keeps no table model.
- Fully migrated onto kit statements: Fencing, Idempotency, Sequences, Coordination.
- Shared helpers only: Features, Permissions, Settings, AuditLog (portable keys, retry, enlistment, list parameters); Messaging and Jobs (claim and upsert shapes, retry); DistributedLocks (lock-safety patterns, no statement kit).
- SQL Server list parameters use `OPENJSON`; table-valued parameter types are removed.
- SQL Server timestamps are `datetimeoffset(7)` everywhere.
- Messaging reads the database clock for due times as well as leases.
- Coordination's legacy column-rename blocks are removed.
- Where a migrated feature has no in-memory provider, the oracle's model is test-only, in the feature's Tests.Harness.

### Out of scope

- Coordination membership redesign (table-version compare-and-swap, generation in the key, observer-lag failure grace). These change membership semantics and are separate work.
- A query builder. Plain CRUD statements stay hand-written.
- Schema diffing and migrations beyond what #1037 provides.

### Acceptance bar, per PR

- Every existing unit and integration suite of the touched features passes locally on PostgreSQL and SQL Server, with exact commands and counts in the PR.
- Each fully migrated feature carries a seeded model-versus-engine oracle with no divergence at exact timestamp precision.
- Changed code meets line coverage of at least 85% and branch coverage of at least 80%.
- `dotnet build -c Release -v:minimal` is clean and scoped analyzers report nothing on every changed project.
- PRs open ready for review; they are never merged by the implementer.

---

## Implementation Units

U1 merged as #1040. The schema runner and U2 through U10 ship together in #1037. The three follow-ups found along the way also ship in #1037: a delay-based retry contract in Messaging storage, one transient-error classifier shared by the unit of work and the kit (retrying only before a commit starts), and cleanup of a PostgreSQL session lock granted to a failed non-blocking try.

| Unit | Scope | Kit additions |
| --- | --- | --- |
| U1 | Test containers scoped to the checkout as well as the project | none |
| U2 | Kit core and Fencing: dialects, fenced transition, `SqlPortable`, retry and enlistment helpers, one relational lease store, the oracle harness | statement shapes: locked read, fenced transition, insert-if-absent, skip-locked claim, batch delete |
| U3 | Portable key text in every keyed feature's shared validation | none |
| U4 | Idempotency onto the kit, with its oracle | unlocked clocked read |
| U5 | Sequences onto the kit, with a test-only model oracle; `datetimeoffset(7)` | upsert that updates and returns |
| U6 | Coordination onto the kit, with a test-only model oracle; rename blocks removed; `datetimeoffset(7)` | clocked plain reads and deletes |
| U7 | Features, Permissions, Settings, AuditLog adopt the helpers; `OPENJSON` lists; `datetimeoffset(7)` | list parameters |
| U8 | Messaging: bound version parameter, claim and upsert on the kit, database clock for due times, `OPENJSON` | batch claim |
| U9 | Jobs: raw claim statements and retry on the kit | none beyond U8 |
| U10 | DistributedLocks: recheck after timeout, release after cancel, savepoint in a caller transaction, reentrancy check | none |

### Test harness, across units

- The Fencing oracle's generator, session, and differential runner become the pattern each migrated feature follows, with its own operations.
- An exact-count parallel scenario joins each migrated feature's conformance suite: N racers on one key yield exactly one insert and exactly K accepted transitions.
