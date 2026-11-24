---
domain: Jobs (Background Jobs)
packages:
---

# Implement a custom persistence provider

> Part of [Jobs](../jobs.md). The contract an `IJobPersistenceProvider` implementation must keep, for teams that write their own Jobs store. Applications that use the in-memory or EF store never need this file.

Implementing `IJobPersistenceProvider` is rarely the right choice. The EF store covers PostgreSQL, SQL Server, and SQLite. A custom store must reproduce every guarantee below, or the scheduler double-runs, loses, or misreports work. Many members are default interface members that throw `NotSupportedException` or do nothing, so a provider compiles before it is complete. Implement each one the features you use need.

## Schema and startup

- Supply the mappings, constraints, and indexes the EF model defines: cancellation, cron control, schedule positions, recovery and fingerprint-defer fields, the fingerprint retry index, the occurrence `Disposition` column, keyed metadata, attempt history, progress, `ChangedAt`, `FailureFingerprint`, and `TraceParent`. Create them before any worker or writer starts.
- Validate `Function` and `ContractVersion` exactly as [contract storage](storage.md#contract-storage) describes, and reject an unsupported contract version before deserializing a payload.
- Every new cron occurrence owns its own `Function`, `ContractVersion`, and a copy of the `Request` bytes. Read the current definition under the materialization transaction or lock, also in `InsertCronJobOccurrencesAsync` when the caller already filled the tuple. Pickup, retry, and recovery of an existing row keep its stored tuple.

## Store clock

Claims decide due-ness on the store's clock, so several members must read the store's current statement clock, never a transaction-start clock:

- `AddTimeJobsAsync(jobs, TimeJobDueTimeSeeder, ct)`, `AddIdempotentTimeJobAsync(job, key, ttl, seeder, ct)`, and the coordinated `ICoordinatedJobWriter.WriteTimeJobsAsync` and `WriteIdempotentTimeJobAsync` read the clock once per write (inside the caller's transaction on the coordinated path), pass that instant to the `TimeJobDueTimeSeeder` for every root, set each root's `ExecutionTime` to every non-null result before inserting, and report the instant (`TimeJobDueTimeSeedResult.StoreUtcNow`, or `JobIdempotencyEnqueueResult.StoreUtcNow` on a creation). A null seeder means every row is absolute and no clock is read. A store with no statement clock may use the host's `TimeProvider`.
- `InsertRunNowCronJobOccurrenceAsync` ignores the incoming `ExecutionTime`, stamps the dashboard's on-demand occurrence from the store clock inside the write, and returns the stamp.
- `InsertCronJobsAsync(jobs, CronSchedulePositionSeeder, ct)` reads the current statement clock inside the inserting transaction, hands it to the seeder, persists the result, and returns it. The caller arms its wake from the returned value.
- `GetEarliestTimeJobsAsync` returns `EarliestTimeJobs`, whose `StoreUtcNow` is read in the same statement as the peek, as `CronDispatchCandidates.StoreUtcNow` is. The peek omits rows this node already holds. `GetEarliestAvailableCronOccurrenceAsync` omits occurrences this node holds under a live lease.
- `SettleInterruptedTimeJobAsync` and `RescheduleTimeJobRetryAsync` add their `retryAfter` to the store's own clock.

## Claims and leases

- **Leases.** Every claim makes the row's `Lease` (`RowLeaseSnapshot`: `Generation`, `State`, `ExpiresAt`, `Holder`, `TakeoverCount`) `Active`, sets `Holder` to the claim owner, `ExpiresAt` to now plus `LeaseDuration`, and draws the row's next `Generation`. Chain descendants take the root's deadline with their own generations. Use `RowLeaseRules` for the state rules.
- **Fenced writes.** Start, progress, retry count, completion, failure, and release take the claim's `FencedRow<Guid>` and apply only while the row holds that generation with a live lease. Renewal and the admission to `InProgress` also require the node's live identity to equal `JobExecutionState.ClaimOwner`. Renewal returns `1` (renewed) or `0` (lost), nothing else. Members that take a `claimOwner` throw `ArgumentException` for a null or empty value.
- **Ending a lease.** Completion, failure, or another terminal settle leaves `Settled`. A graceful release leaves `Released`. A stalled reclaim or dead-node sweep leaves `Abandoned`. Cancel of a claimed `Idle` row, the edit guard, a cron skip, a child run-condition release, and the stranded-child sweep leave `Revoked`.
- **Destructive claim.** `QueueTimeJobsAsync` stamps owner, lease, `Queued`, and the refreshed concurrency token onto the caller's own candidate instances and prunes each child tree to the descendants it leased. Never share or reuse a candidate batch across claims. Peek again through `GetEarliestTimeJobsAsync`. Refuse a row whose `ExecutionTime` is more than one second past the store clock (a row with no `ExecutionTime` is due at once). `AcquireImmediateTimeJobsAsync` applies the same store-clock test and leaves a not-yet-due row `Idle`. `QueueTimeJobsAsync` and `QueueCronJobOccurrencesAsync` still accept rows this node holds.
- **Run filter and cluster limits.** Apply the host's `RunOnly` filter to every claim, acquire, timed-out sweep, and next-occurrence query, and count live leased rows against `ClusterMaxConcurrency` under a per-job lock held with the lease write.
- **Retry reschedule.** `RescheduleTimeJobRetryAsync(row, retryCount, retryAfter)` moves an `InProgress` time job whose live lease is still that generation to `Idle`, releases the lease, sets `RetryCount`, and sets `ExecutionTime` to the store clock plus `retryAfter`, whatever its `OnNodeDeath`. The overload that takes a `JobAttemptEnd failedAttempt` also sets `FailureFingerprint` and closes the open attempt in the same write. It returns `1` when it wrote and `0` when the fence excluded the row, commits even after the host stop token fires, and matches nothing when repeated.
- **Graceful stop.** `ReleaseInProgressTimeJobAsync` and `ReleaseInProgressCronJobOccurrenceAsync` hand a fenced `InProgress` row back to `Idle` with `RetryCount` and execution time unchanged. `SettleInterruptedTimeJobAsync` and `SettleInterruptedCronJobOccurrenceAsync` apply `OnNodeDeath` without charging the budget: `Retry` to `Idle`, `MarkFailed` to `Failed`, `Skip` to `Skipped`, recording the supplied reason. Same return and commit rules as the reschedule.
- **Reclaim.** `ReclaimStalledTimeJobsAsync`, `ReclaimStalledCronJobOccurrencesAsync`, `ReleaseDeadNodeTimeJobResourcesAsync`, and `ReleaseDeadNodeOccurrenceResourcesAsync` return `JobReclaimCounts` (`Released`, `MarkedFailed`, `Skipped`, `Total`), counting only rows the sweep's own conditional write moved. A `Retry` reclaim adds one to `RetryCount` and closes the open attempt as `Lost` only for a run with an attempt in flight. A run whose last attempt is a `Failed` one its current holder closed, with none open, was waiting between retries and goes back uncharged with no attempt written. A row that never started is reclaimable once its lease lapses, under every policy. When part of a sweep fails, the scheduler still runs the rest and throws `JobReclaimException`, whose `Reclaimed` carries the rows that moved.

## Cron schedules

### When a row already stands for the instant

One rule decides whether an occurrence may be created at a `(CronJobId, ExecutionTime)` pair, for claims, materialization, and recovery alike: a row accounts for its instant unless it is `Skipped` with `CronOccurrenceDisposition.ReplacementOwed`. Live rows, every terminal status, and any status value a newer version wrote all take the instant. `CronOccurrenceAccounting` implements the rule over the `CronOccurrenceInstantView` projection; use it instead of restating it.

| `Disposition` | Written by | Effect at the instant |
| --- | --- | --- |
| `Accounted` (default) | Every new row, and every ordinary retirement: pause, recovery, dead-node sweep, lapsed lease, user-code skip | Takes the instant |
| `ReplacementOwed` | The seed reconcile, when it retires an old-expression row without creating a replacement | Allows a new occurrence: the fire is still owed |
| `Superseded` | A runtime schedule edit through `ICronJobManager`, which creates its own replacement | Takes the instant, so an edit never double-runs |

`Disposition` is the rule's only input. `SkippedReason` is display text and is never matched. Several rows may share an instant, because the unique index covers only live rows. Report the live row first.

### Applying a recovery pass

Do not re-derive the missed-run decision. Snapshot the window `CronRecoveryPlanner.GetInspectionWindow(request)` asks for, pass those rows to `CronRecoveryPlanner.CreatePlan(...)`, and apply the returned `CronRecoveryPlan` with your own fenced writes in one transaction or critical section. The planner reads and writes nothing.

- **Run steps** walk the missed instants in order. An accounted instant is stepped past. The first unaccounted instant either reuses a still-claimable row there or creates the run under the request's reserved identity. The list is empty under `Skip`, and under `Coalesce` when every instant is accounted for. A create step is always last.
- **A reuse step may fail.** When the row starts running before your compare-and-set lands, zero rows change. Treat that as an accounted instant and continue to the next step.
- **Two resolutions** come with every plan, one for "the walk established a run" and one for "it did not", because you only know which after the writes. Each names the watermark and next-due value to persist and a bound on the rows to retire. Retire by that bound plus your own still-claimable predicate, never by a list of ids: the bound can cover rows the snapshot never held.

### Materialization and dispatch

- `MaterializeCronScheduleOccurrenceAsync` fences the expected revision and watermark, commits a new unclaimed `Idle` occurrence or recognizes an existing one, and advances the schedule in the same transaction. `CronScheduleMaterializationOutcome` distinguishes a lost fence, a future projection, a new row, an existing live row, and an already-terminal row. Every writer that materializes takes the same per-definition lock.
- The compare-and-advance fence covers the observed watermark, the observed next-due value (`CronScheduleAdvance.ObservedNextDueUtc` and `CronRecoveryRequest.ObservedNextDueUtc` must equal the stored `NextDueUtc`), the schedule revision, and a non-paused row.
- Materialization and advance write a non-null `EvaluationFingerprint` and clear the fingerprint defer state. Recovery always writes `CronRecoveryRequest.EvaluationFingerprint`.
- Candidate selection takes a `CronDispatchCandidateCursor? after` between `limit` and the cancellation token. Apply it inside the query, before the limit, on the `(NextDueUtc, CronJobId)` order the result uses.
- Implement bounded stale-fingerprint paging, a fenced defer (`FingerprintFailureCount`, `FingerprintRetryAfterUtc`), and compare-and-advance with the same store-time and lost-fence rules.
- Pause, resume, and update must be atomic. A paused definition's occurrences are never claimed.

### Seed reconcile

`MigrateDefinedCronJobsAsync(seedOwner, cronJobs, ct)` receives the owner's complete, validated seed set. Each `CronSeedDefinition` carries a deterministic `Id` and a `Marker` (`headless.jobs.seed:{owner}:cron:{function}` or `headless.jobs.seed:{owner}:decl:{key}`) to store in `InitIdentifier` on create. The provider must:

- Load only rows whose `InitIdentifier` starts with `headless.jobs.seed:{owner}:`, and match them by `Id`, never by function.
- For an owned row missing from the set, delete its `Idle` and `Queued` occurrences. While any occurrence is `InProgress`, retire the row: set `IsPaused = true`, refresh `UpdatedAt`, and change the marker to `headless.jobs.seed:{owner}:retired:{kind}:{name}` without changing the id. Delete the definition and its remaining occurrences once none is `InProgress`.
- Insert missing entries with every creation value (policies, retries, `Request`, `Description`, `OnNodeDeath`, lineage), and treat a lost insert race on the id as success.
- Restore a drifted expression or zone. Restore `Function`, `Request`, and `ContractVersion` together when the row names another job. For an entry with a `RequestHash`, rewrite `Request` and `ContractVersion` together when the stored version differs, the stored payload has no canonical form, or its canonical hash differs.
- Re-declaring a retired entry restores the live marker and unpauses the row, even without drift. Any restore bumps the revision, resets the schedule position, and retires pending occurrences. Keep existing policies and an operator's pause on a live row.

`CronSeedDefinition` is positional. Code that deconstructs it must include every member.

## Other duties

- **Requeue.** Implement `RequeueTimeJobAsync` and `RequeueCronJobOccurrenceAsync` with the `JobRequeueRules` eligibility rules, the reset [requeue](failures.md#requeue-a-failed-job) describes, and the new `TraceParent` they take.
- **Progress.** Implement `UpdateTimeJobProgressAsync` and `UpdateCronJobOccurrenceProgressAsync` with the lease fence, persist `JobExecutionState.Progress` when a terminal update lists it in `PropertiesToUpdate`, and clear the three progress fields on requeue.
- **Failure fingerprint.** Write `FailureFingerprint` when `PropertiesToUpdate` names it, write the `JobFailureFingerprints` constants for your own sweep failures in the same conditional write that ends the run, and clear it on requeue.
- **Attempt history.** The open members (`OpenTimeJobAttemptAsync`, `OpenCronJobOccurrenceAttemptAsync`) and reads default to doing nothing. A provider that keeps history opens an attempt only while the run is `InProgress` and the claimed row (`JobExecutionState.ClaimedRow`) still holds an active lease under the claim owner. It closes the attempt with `JobExecutionState.AttemptEnd` when `PropertiesToUpdate` names it and the write applied, and in each of its own sweep writes. Only the write that wins the run's conditional update closes it. When no open attempt exists, the close inserts one closed attempt.
- **Trace parent.** Persist `TraceParent` on time jobs and occurrences, replace it on requeue, and copy it into `JobExecutionState.TraceParent` when you project a row for execution.
- **Idempotency.** Enforce reservations atomically. `DeleteExpiredIdempotencyReservationsAsync` defaults to deleting nothing, so a store that keeps reservations implements it.
- **Keyed scheduling.** Enforce atomic key arbitration, generation fencing, and retention across every mutation path. A read-then-insert adapter is not enough. An unsupported store rejects keyed calls before any effect.
- **Run-state columns.** Never take status, owner, lease, retry count, or outcome from a caller's entity on update. `UpdatedAt` comes from the store clock because it is the claim's compare-and-swap token.

## Change marker and change reads

Every time job, cron definition, and occurrence carries `ChangedAt`, a marker the store stamps on each write an operator sees: insert, claim, progress, retry, completion, failure, cancel, requeue, edit, pause, resume, schedule advance, and the parent of a deleted chain member. Lease renewals do not stamp it. The dashboard reads changes through it on every node.

- Stamp `ChangedAt` as the later of the store's statement clock and the row's previous value plus one tick of the column's precision, so it always rises. A row's version is `ChangedAt.Ticks` (`JobChange.Version`). The setter is public only for providers. EF never writes it from an entity.
- Implement `GetLatestChangeMarkerAsync` and `ReadChangedRowsAsync`. `ReadChangesAsync(cursor, pageSize, overlapWindow)` is built on them. Their defaults throw `NotSupportedException`.
- A pass reads markers after `PassAfter` through `PassThrough` in `(ChangedAt, Id)` order, and the next pass starts `overlapWindow` earlier, so a transaction that committed late is read on the next pass. Callers drop repeats by `(Id, Version)`. `GetChangeStartCursorAsync()` returns a cursor at the latest marker. `JobChangeCursor.ToString()` and `JobChangeCursor.TryParse` round-trip it.
- Rows written through a unit of work are re-stamped after the commit, so the overlap window covers only the store's own short writes. A process that stops between the commit and the re-stamp leaves the row visible to a refetch but not to a change read.
- EF backends other than PostgreSQL and SQL Server stamp `CURRENT_TIMESTAMP`. Treat their change reads as best effort.

## Dashboard reads

`IJobPersistenceProvider` answers the dashboard's reads as default members that throw `NotSupportedException`. Implement the per-table members (names ending in `Rows`, `RowsByOwner`, or `Targets`, plus `CountJobRunWindowAsync`). The members that span time jobs and occurrences merge them.

- **Run list.** `ListJobRunsAsync(JobRunQuery, JobRunCursor?, pageSize)` returns `JobRunSummary` rows of both kinds ordered by `JobRunQuery.SortBy` (`JobRunSortKey`: `ExecutionTime` by default, `Function`, or `Status`) and then id, both in `SortDirection`. Statuses sort in lifecycle order, not by name. A chain member without an execution time sorts at `JobRunPosition.Unscheduled`. `JobRunCursor` carries the sort, direction, and a digest of the filters. Check `cursor.Fits(query)` before resuming. A cursor read under another sort or filter throws `ArgumentException`. `Text` is a literal, case-insensitive substring of the function, id, business key, or description; payloads are never searched.
- **Correlation read.** `ReadJobRunsByCorrelationAsync(correlationId, limit, cancellationToken)` returns at most `limit` `JobCorrelatedRun` rows of both kinds, oldest first by `ExecutionTime`, each with its id, `CausationId`, due time, and `ExecutedAt`. A caller that must know whether more exist asks for `limit + 1`. Match `JobRunQuery.CorrelationId` ordinally, also in `ListJobRunsAsync` and `CountJobRunsAsync`. A store whose column compares case-insensitively must drop the rows that differ in case and keep reading until it holds `limit` matches, or the limit fills with another run's rows.
- **Counts.** `CountJobRunsAsync(query, cap)` stops at the cap (`JobRunCount.DefaultCap`, 10,000) and reports `Capped`.
- **Lookups.** `GetJobRunAsync(id)` resolves a run of either kind. `GetCronJobOccurrenceByIdAsync(id)` returns an occurrence.
- **Failure groups.** `GetJobFailureGroupsAsync(recentSince, function?)` groups actionable failures by function and fingerprint, latest first.
- **Operations.** An Operation freezes its scope as a `JobOperationScope` (command, query, `HighWaterMark`, `ConfirmedAt`). `CountJobOperationScopeAsync` and `ReadJobOperationScopeAsync(scope, cursor, chunkSize)` read it. `RequeueTimeJobIfUnchangedAsync`, `RequeueCronJobOccurrenceIfUnchangedAsync`, `RemoveTimeJobIfUnchangedAsync`, and `RemoveCronJobOccurrenceIfUnchangedAsync` write only while the row's marker is unchanged.
- **Force delete.** `RemoveCancelRequestedTimeJobAsync(jobId)` deletes only an `InProgress`, cancel-requested, standalone, unkeyed, childless root. `RemoveIdleCronJobAsync(cronJobId)` deletes a cron job and its occurrences only while none is `InProgress`. Each checks and deletes in one commit. Without them, the dashboard's `jobs.force-delete` and `jobs.delete-cron` commands throw.
- **Figures.** `GetJobOverviewFiguresAsync(now, lateAfter)` returns the 24 hours before `now` and the 24 hours before those: runs that succeeded and failed by `ExecutedAt`, the success share, and runs whose first attempt started more than `lateAfter` after their execution time (from attempt history). `GetJobFunctionStatsAsync(since)` returns per-function counts. `CountInProgressJobRunsByOwnerAsync(cap)` counts running runs per claim owner.
- **Last run per cron job.** `GetCronJobLastRunsAsync()` returns, per cron job, the started occurrence (status neither `Idle` nor `Queued`) with the greatest `ExecutionTime`, ties broken by the store's id order. `ExecutedAt` is `null` while it runs. A cron job that never ran has no entry, and a paused one keeps its last run. Implement `ReadCronJobLastRunRowsAsync` as one read, never one per cron job.
- **Runs per bucket.** `CountJobRunsByBucketAsync(query, boundaries)` counts runs per status between adjacent UTC edges, placing each run by its execution time, not by `ExecutedAt` as the Overview figures do. The edges must be at least two and strictly ascending, else `ArgumentException`. `Counts` is `null` only for a bucket the store could not measure.
