---
title: Messaging P1.3 — Capability Matrix and Phase 1 Docs Refresh
type: feat
status: completed
date: 2026-05-18
origin: docs/brainstorms/2026-05-18-messaging-p1-3-capability-matrix-docs-requirements.md
---

# Messaging P1.3 — Capability Matrix and Phase 1 Docs Refresh

## Summary

Refresh `docs/llms/messaging.md` and the sibling messaging-package READMEs to match the live Phase 1 surface, deriving every capability claim from each package's `Setup.cs` and transport/storage class implementations rather than from existing prose. The implementation is structured as additive section authoring (envelope/tenant, retry, OTel), two new capability matrices (transport and storage) anchored in a verification pass, a cross-cutting Phase 2 deferral sweep, and a README audit that also corrects stale entry-point API examples uncovered during planning research.

---

## Problem Frame

The origin doc (see Sources & References) carries the full pain narrative: the canonical LLM messaging doc and several provider READMEs predate the Phase 1 retry contract (#229) and OTel enricher (#230), and there is no existing capability matrix to compare transports or storages, so readers cannot reliably tell what is Phase 1 today versus what is Phase 2 (#232) or open RFC (#218). Planning research surfaced one widening finding: several READMEs additionally use stale entry-point APIs (`AddMessages` / `ScanConsumers`) where the live API is `AddHeadlessMessaging` / `SubscribeFromAssembly`. The audit pass in this plan corrects those in the same sweep.

---

## Requirements

This plan satisfies origin R1–R15. R-IDs are referenced by each Implementation Unit's `Requirements` field below; no plan-local IDs are introduced.

**Origin actors:** none.
**Origin flows:** none.
**Origin acceptance examples:** AE1 (covers R8, R14), AE2 (covers R10, R11), AE3 (covers R2).

---

## Scope Boundaries

- No retry, telemetry, or publisher-shape code changes. Production code in `src/Headless.Messaging.*` is treated as fixed. Audit findings that reveal code-side inconsistencies are logged as follow-up issues, not patched here.
- No full Phase 2 rename / migration guide. The deferred appendix is a placeholder table pointing to #232 and #218.
- No edits to provider READMEs whose capability claims and entry-point API examples both already match live registrations — audit-and-fix only, not blanket rewrite.
- No new doc file. `docs/llms/messaging-envelope.md` is not created; envelope material lives inside `docs/llms/messaging.md`.
- No restructuring of unrelated sections in `docs/llms/messaging.md`. Existing Quick Orientation, Agent Instructions, and inlined per-package sections stay as-is except where the audit catches stale claims.
- No documentation of the Phase 2 `IConsumeBehavior<T>` / `IPublishBehavior<T>` design (#218). The appendix only records that the decision is deferred.

### Deferred to Follow-Up Work

- Align all 17 messaging package READMEs to the `Headless.Messaging` transport-provider-guide's README checklist (`docs/solutions/guides/messaging-transport-provider-guide.md`). The learnings researcher flagged this as desirable; it is broader than #231's audit scope and is better tracked separately.
- Any code-side bugs surfaced by the audit (e.g., a transport class that does not deliver a capability its README claims). Open as new issues; do not patch in this plan.
- A CI guard or fixture-derived matrix that fails the build when `src/Headless.Messaging.*/Setup.cs` changes without `docs/llms/messaging.md` being touched in the same PR. Evaluate at the next observed drift event; "note the carry-forward rule in the matrix preamble" is the stopgap mitigation in this plan, not a final solution.

---

## Context & Research

### Relevant Code and Patterns

- `docs/llms/messaging.md` — the canonical doc; current heading map: TOC at line 8, Quick Orientation at 190, Agent Instructions at 238, per-package H1s starting at 264 (`# Headless.Messaging.Abstractions`), retry subsection at 538–630, OTel package block starting at 1047.
- `src/Headless.Messaging.Core/Setup.cs` — publisher interfaces register centrally at lines 108–111: `OutboxPublisher` is bound to both `IOutboxPublisher` and `IScheduledPublisher` as the same instance; `DirectPublisher` is bound to `IDirectPublisher`. No transport or storage package registers any publisher interface.
- `docs/llms/messaging.md` is hand-maintained (no generator); its frontmatter `packages:` list inlines content from all 17 sibling package READMEs. Stale-API or capability corrections in U7 therefore need to land in BOTH the sibling README AND the corresponding inlined section in `docs/llms/messaging.md`.
- `src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs` — the new retry contract from #229; `BackoffStrategy` at line 83, `RetryPolicyOptionsValidator` at line 173.
- `src/Headless.Messaging.Abstractions/IScheduledPublisher.cs` — confirms scheduled publishing is part of the abstractions surface, not a Core-only type.
- `src/Headless.Messaging.OpenTelemetry/` — the `IActivityTagEnricher` extension point and default enricher from #230.
- Transport packages (capability source of truth): `src/Headless.Messaging.{RabbitMq,Nats,AzureServiceBus,AwsSqs,Kafka,Pulsar,RedisStreams,InMemoryQueue}/Setup.cs` plus each package's `*Transport.cs` and `*ConsumerClient.cs`.
- Storage packages (capability source of truth): `src/Headless.Messaging.{PostgreSql,SqlServer,InMemoryStorage}/Setup.cs` plus the registered `IDataStorage`, `IOutboxTransaction`, and `IStorageInitializer` implementations.
- `docs/llms/{caching,blobs,jobs}.md` — sibling LLM docs that establish the doc-conventions to mirror: YAML frontmatter with `domain:` + `packages:`, H1 = domain name once, H2 = TOC / Quick Orientation / Agent Instructions then one H2 per package, GFM pipe tables for comparative data, blockquote callouts for inline notices (`> **Security Note:** …` at `src/Headless.Messaging.RabbitMq/README.md:43`). No admonition syntax (`> [!NOTE]`) is in use anywhere in `docs/llms/`.
- No prior capability matrix exists in the repo. The two matrices in this plan are net-new prior art.

### Institutional Learnings

- `docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md` — direct precedent for the recurring failure mode this plan prevents. Treat wrapper code as contract code; never reuse raw broker connection strings for `BrokerAddress` / operator surfaces; greenfield rule — split runtime defects from migration work and drop speculative migration content. Carry-forward rule for future PRs: when reshaping a public messaging API, update human docs AND generated LLM docs in the same change.
- `docs/solutions/guides/messaging-transport-provider-guide.md` — canonical transport-provider contract. It supplies the capability dimensions the transport matrix uses: `ITransport.SendAsync` semantics, sanitized `BrokerAddress`, `FetchTopicsAsync` auto-provisioning, commit/reject mapping (ack/nack, delete/abandon, complete/DLQ/requeue), reject capability (some brokers can't reject), `PauseAsync`/`ResumeAsync` idempotency, broker-side delayed publishing, ordering under `ConsumerThreadCount`, header round-trip with `TenantId` four-case integrity, topic naming restrictions, payload limits.

### External References

- None. Internal-only doc refresh; no version-sensitive framework documentation consulted.

---

## Key Technical Decisions

- **Source-of-truth discipline.** Every capability claim is read from code in each package's `Setup.cs`, `ITransport`, and `IConsumerClient` (or storage equivalents), not from existing prose. When live code and an existing README disagree, the README is corrected, not the matrix. Rationale: prevents legacy README drift from re-entering the matrix, per the `transport-wrapper-drift-and-doc-sync` precedent.
- **Transport matrix gets seven columns:** Direct publish, Consume, Native scheduled, Ordering shape, Tenant header (the five origin minimums) + Broker reject + Auto-provisioning. Rationale: each addition reflects a contract dimension in the provider-guide that genuinely distinguishes brokers (some can't reject; some auto-create resources, some don't). Collapsing to five would hide capability differences readers must care about.
- **Storage matrix refined from origin's three-column commitment to two columns.** The brainstorm proposed Outbox / Persisted retry / Subscriptions; the live code has a single combined `IDataStorage` contract that covers both outbox and persisted retry, and no `ISubscriptionStorage` abstraction exists. Plan substitutes: Outbox + Retry storage (one column with a footnote naming the combined contract) and Schema initializer (`IStorageInitializer`). An earlier draft included a third "Diagnostic observer" column (SqlServer registers `DiagnosticProcessorObserver` / `DiagnosticRegister`; PostgreSql does not), but that variation is an internal DI-wiring detail rather than a decision-grade storage capability; dropped to keep the matrix at the "varies AND is decision-relevant" bar. Rationale: vacuously-"n/a" columns and label-only variations are both noise; concrete columns that meaningfully change a chooser's decision are the matrix's whole point.
- **README audit extends to stale entry-point API examples** (`AddMessages` → `AddHeadlessMessaging`, `ScanConsumers` → `SubscribeFromAssembly`) discovered during research. Rationale: touching a README's capability claims while leaving its quick-start example stale is half a job; same audit pass, same affected files. Origin's "audit-and-fix only where claims drifted" is preserved — claims-drifted is read inclusively to cover any drift the audit-pass surfaces.
- **Phase 2 callout form: GFM blockquote with bold lead** (`> **Deferred to Phase 2 (#232).** …`), matching the existing `> **Security Note:** …` convention. Rationale: matches established doc convention; no admonition syntax is in use across `docs/llms/`.
- **No separate verification artifact.** Audit findings flow directly into the matrix cells (U4, U5) and README edits (U7). Verification per origin R15 happens as the implementer reads each `Setup.cs` while authoring the cell. Rationale: a parallel artifact (CSV, sub-doc) would itself become a sync target; the matrix and corrected READMEs ARE the verification output.

---

## Open Questions

### Resolved During Planning

- Final transport matrix column set (origin Deferred to Planning, [Technical]): seven columns — Direct publish, Consume, Native scheduled, Ordering shape, Tenant header, Broker reject, Auto-provisioning. See Key Technical Decisions.
- Storage matrix column set (origin Deferred to Planning, [Technical]): two refined columns — Outbox+Retry storage (combined `IDataStorage`) and Schema initializer (`IStorageInitializer`). Origin's "Subscriptions" column is dropped because no such abstraction exists; an earlier-considered "Diagnostic observer" column was also dropped (see Key Technical Decisions) because the variation is an internal-wiring detail, not a chooser-relevant capability. The substitution is footnoted in the doc.
- Provider README audit scope (origin Deferred to Planning, [Needs research]): verification across `src/Headless.Messaging.*/README.md` found that 12 packages contain stale `AddMessages` / `ScanConsumers` entry-point examples — AwsSqs, AzureServiceBus, InMemoryQueue, InMemoryStorage, Kafka, Nats, OpenTelemetry, PostgreSql, Pulsar, RabbitMq, RedisStreams, SqlServer. `Headless.Messaging.Abstractions/README.md` already uses the live API (`AddHeadlessMessaging`, `SubscribeFromAssemblyContaining<Program>()`) and is NOT in the stale set. The audit still covers all 17 messaging package READMEs to discover capability drift not yet seen; the 12 known-stale entries are explicit starting work.
- OTel default-tag reference form (origin Deferred to Planning, [Technical]): a small reference table at the end of the OTel section, matching the styling of the existing `RetryPolicy options` table at `src/Headless.Messaging.Core/README.md:313`.
- Visual form of Phase 2 inline callouts (origin Deferred to Planning, [Technical]): GFM blockquote with bold lead. See Key Technical Decisions.

### Deferred to Implementation

- Exact wording of each inline Phase 2 callout. Reads better when authored in-place, sensitive to the surrounding section's tone.
- Whether the OTel default-tag table includes a "Default value" or "When set" column. Depends on what the live default enricher actually emits per tag.
- Whether any single provider README needs structural changes beyond inline corrections. Implementer decides per file based on audit findings.
- Final cell values for Broker reject and Auto-provisioning per transport. Each requires reading the transport's `RejectAsync` / `FetchTopicsAsync` implementation; planned but not pre-decided here.

---

## Implementation Units

### U1. Author envelope and tenant rules section in messaging.md

**Goal:** Add the envelope-fields-and-tenant-rules content for the current Phase 1 publisher surface (`IDirectPublisher`, `IOutboxPublisher`, `IScheduledPublisher`) to `docs/llms/messaging.md`.

**Requirements:** origin R1, R4 (Phase 1 publishers only); partial R15 (verification).

**Dependencies:** None.

**Files:**
- Modify: `docs/llms/messaging.md`

**Approach:**
- Add the content under the existing `# Headless.Messaging.Abstractions` H1 block (around line 264). Cover: message identity headers (`Headers.MessageId`, `Headers.CorrelationId`, etc.), tenant propagation behavior (when `Headers.TenantId` is set, when it is `null`, the four-case strict-publish-tenancy contract), publisher-supplied vs. framework-supplied fields, and the three current Phase 1 publishers — noting that `IScheduledPublisher` is bound to the same instance as `IOutboxPublisher` per the central registration in `src/Headless.Messaging.Core/Setup.cs:108-111`.
- Source claims from `src/Headless.Messaging.Abstractions/Headers.cs`, `MessageHeader.cs`, `PublishOptions.cs`, and the existing Strict Publish Tenancy section at `src/Headless.Messaging.Core/README.md:631-682`.
- No example, snippet, or interface reference may present `ISendPublisher`, `IBroadcastPublisher`, or `DeliveryKind` as current behavior.

**Patterns to follow:**
- Strict Publish Tenancy prose at `src/Headless.Messaging.Core/README.md:631-682`.
- Publisher Options H3-subsection structure at `docs/llms/messaging.md:447-493`.

**Test scenarios:**
- Test expectation: none — this unit produces prose documentation. Verification is the doc-content check below and the cross-cutting sweep at U7.

**Verification:**
- A reader can identify the three current Phase 1 publishers without scrolling to the matrices, and the four-case `TenantId` integrity contract is named or referenced.

---

### U2. Author retry contract section in messaging.md

**Goal:** Ensure the retry section in `docs/llms/messaging.md` describes `MessagingOptions.RetryPolicy` (the #229 contract) exclusively, with no residual references to the legacy CAP-style knobs (`FailedRetryCount`, `FailedRetryInterval`, `Added`+lookback retry selection) as current behavior.

**Requirements:** origin R2, R4 (partial), R15 (partial).

**Dependencies:** None.

**Files:**
- Modify: `docs/llms/messaging.md` (Retry Policy subsection currently at lines 538–630)

**Approach:**
- Audit the existing retry subsection against `src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs` and the Core README retry section at `src/Headless.Messaging.Core/README.md:285-368`.
- The existing content covers `MaxInlineRetries`, `MaxPersistedRetries`, `BackoffStrategy`, and `OnExhausted` — verify completeness and add any missing piece: attempt counting (total including original execution, not retries-after-first-failure), exception classification (retryable / non-retryable / cancellation), and `RetryDecision.Stop` / `Continue(delay)` / `Exhausted` semantics.
- Mirror the Core README's three-subsection structure (Global Configuration, Exhausted vs Stop, Inline vs Persisted Retry Paths) but keep `messaging.md` self-contained — do not assume readers cross-reference the package README.
- **Preserve the existing pre-1.0 migration content.** `docs/llms/messaging.md` deliberately mentions the legacy retry-knob names in two places: a Quick Orientation bullet around line 260 ("The 5 removed pre-1.0 primitives — `FailedRetryCount`, `FailedRetryInterval`, `FallbackWindowLookbackSeconds`, `RetryBackoffStrategy`, `FailedThresholdCallback`") and a migration table around lines 616–627 mapping each old knob to its replacement. These are intentional migration aids, NOT stale claims — they appear with explicit "removed" / "replaces" framing. Leave both intact; the U2 verification rule below applies only to references presenting these names as current behavior.

**Patterns to follow:**
- `src/Headless.Messaging.Core/README.md:285-368` retry policy structure.

**Test scenarios:**
- Covers AE3. Reading the retry section: `MessagingOptions.RetryPolicy` is named as the single composition point; `MaxInlineRetries`, `MaxPersistedRetries`, `BackoffStrategy`, and `OnExhausted` are present; no reference to `FailedRetryCount`, `FailedRetryInterval`, or `Added`+lookback retry selection appears as current behavior.

**Verification:**
- Searching the doc for the legacy CAP-style retry-knob names returns no matches that present them as current behavior. The Quick Orientation bullet (~line 260) and migration table (~lines 616–627) are intentional migration aids; both must survive this unit unchanged.

---

### U3. Author OTel enrichment section in messaging.md

**Goal:** Document the OTel surface from #230 — `IActivityTagEnricher` extension point, default Headless tags, tenant-tag suppression, ordered enricher registration, enricher-exception isolation — including a small default-tag reference table.

**Requirements:** origin R3, R15 (partial).

**Dependencies:** None.

**Files:**
- Modify: `docs/llms/messaging.md` (the `# Headless.Messaging.OpenTelemetry` H1 block starting at line 1047, including the existing Built-in tag names subsection at line 1122)

**Approach:**
- Refresh the OpenTelemetry section to describe the `IActivityTagEnricher` extension point: how applications register custom enrichers, ordering semantics, default-enricher behavior, the tenant-tag suppression option, and enricher-exception isolation.
- Reshape or replace the existing Built-in tag names subsection with a small reference table at the end of the section: columns for tag name, source, and when emitted. Cover at minimum `headless.messaging.tenant_id` and the completion-mode tags the default enricher determines.
- Source claims from `src/Headless.Messaging.OpenTelemetry/` (Setup.cs, the enricher abstractions, the default enricher implementation).

**Patterns to follow:**
- The `RetryPolicy options` reference table at `src/Headless.Messaging.Core/README.md:313-321` for column-styling discipline.

**Test scenarios:**
- Test expectation: none — prose documentation. Verification is the doc-content check below and the cross-cutting sweep at U7.

**Verification:**
- Applications can find from this section: how to register one or more enrichers, how to suppress the tenant tag, what default tags appear in their traces, and the enricher-exception isolation contract.

---

### U4. Audit transports and build the transport capability matrix

**Goal:** Inspect each transport package's `Setup.cs`, `ITransport` implementation, and `IConsumerClient` implementation; place a seven-column transport capability matrix in a new `## Provider Capabilities` H2 section of `docs/llms/messaging.md`.

**Requirements:** origin R5, R6, R8, R9, R14 (partial), R15.

**Dependencies:** None.

**Files:**
- Modify: `docs/llms/messaging.md`
- Read-only audit source: `src/Headless.Messaging.{RabbitMq,Nats,AzureServiceBus,AwsSqs,Kafka,Pulsar,RedisStreams,InMemoryQueue}/Setup.cs` plus each package's `*Transport.cs` and `*ConsumerClient.cs`

**Approach:**
- Walk all eight transport packages. For each, derive cell values from the live code, not from existing README prose:
  - **Direct publish** — does `ITransport.SendAsync` exist and work? (uniformly yes; the column anchors the row)
  - **Consume** — does `IConsumerClient.ListeningAsync` exist? (uniformly yes; anchor)
  - **Native scheduled** — does this transport offer broker-side delayed delivery, or does scheduling fall through to the framework's outbox decorator? Inspect the transport implementation for any explicit delayed-publish path; the Nats README already states it does not add broker-native scheduling (`src/Headless.Messaging.Nats/README.md:69`), useful as a worked example.
  - **Ordering shape** — what guarantee shape does the transport advertise? (per-queue, per-partition, per-subject, per-key, per-session, FIFO-only, none)
  - **Tenant header** — header round-trip preserves `TenantId` per the four-case contract? (uniformly yes — Core enforces it. Anchor column; footnote any known gap.)
  - **Broker reject** — does `RejectAsync` map to a broker-native reject (nack, abandon, complete-to-DLQ, etc.) or to a fallback? Per provider-guide, some brokers can't reject.
  - **Auto-provisioning** — does `IConsumerClient.FetchTopicsAsync` create topics/streams/queues/subscriptions, or is it pass-through?
- Place the matrix in a new `## Provider Capabilities` H2 section between `## Agent Instructions` (currently ends at line 263) and the first per-package H1 (`# Headless.Messaging.Abstractions` at line 264).
- Include a one-paragraph preamble that explains how to read the matrix, what each column means, and that every cell is sourced from the corresponding package's `Setup.cs` and transport class (per origin R8).

**Execution note:** Read-then-write. Derive cell values by reading code first; never transcribe existing README claims. When a current README and the live code disagree, the matrix records the code's behavior and U7 corrects the README. **Falsifiability check during authoring**: if any column ends up with 7-of-8 rows reading "yes with broker-specific label" (e.g., Broker reject yielding "nack / abandon / xack / seek / requeue" — all variants of "yes, has a reject path"), demote that column to a footnote on the Consume anchor column instead of carrying it as its own column. Decision-grade variation, not label variation, earns column slots.

**Technical design:**

> *Directional guidance for the matrix shape — implementer may refine column wording, ordering, or footnotes during authoring. The pre-filled cell values below are illustrative; the implementer MUST re-derive every cell (including the pre-filled ones) from the corresponding transport's source before committing the matrix.*

```
| Provider          | Direct | Consume | Native sched | Ordering shape | Tenant hdr | Broker reject | Auto-prov |
|-------------------|--------|---------|--------------|----------------|------------|---------------|-----------|
| RabbitMq          | yes    | yes     | plugin       | per-queue      | yes        | nack          | queues    |
| Nats              | yes    | yes     | no           | per-subject    | yes        | ...           | ...       |
| AzureServiceBus   | yes    | yes     | yes          | per-session    | yes        | abandon/DLQ   | ...       |
| AwsSqs            | yes    | yes     | yes          | FIFO only      | yes        | ...           | ...       |
| Kafka             | yes    | yes     | no           | per-partition  | yes        | seek/commit   | topics    |
| Pulsar            | yes    | yes     | yes          | per-key        | yes        | ...           | ...       |
| RedisStreams      | yes    | yes     | no           | per-stream     | yes        | xack          | streams   |
| InMemoryQueue     | yes    | yes     | yes          | per-queue      | yes        | yes           | n/a       |
```

The `...` cells are placeholders — implementer fills from code during the audit.

**Patterns to follow:**
- `RetryPolicy options` table styling at `src/Headless.Messaging.Core/README.md:313-321`.
- Provider-guide commit/reject mapping section at `docs/solutions/guides/messaging-transport-provider-guide.md:107-116` for Broker-reject cell values.

**Test scenarios:**
- Covers AE1. For at least one transport whose existing README overstates support, the matrix records the code-truthful value and the README correction is queued for U7.
- Integration — every cell in the 8×7 matrix can be traced to a method on the transport or consumer-client class, or to a `Setup.cs` registration. Verified by re-reading code while writing each cell.

**Verification:**
- 56 cells, each derived from the corresponding package's source.
- No cell value uses the strings `ISendPublisher`, `IBroadcastPublisher`, `DeliveryKind`, or `broadcast` as if they were live behavior.

---

### U5. Audit storage packages and build the storage capability matrix

**Goal:** Inspect each storage package's `Setup.cs` and registered storage classes; place a two-column storage capability matrix as the second table in the `## Provider Capabilities` section established by U4.

**Requirements:** origin R5, R7 (refined per Key Technical Decisions — Subscriptions column substituted, Diagnostic observer dropped), R8, R14 (partial), R15.

**Dependencies:** U4 (the `## Provider Capabilities` H2 must exist).

**Files:**
- Modify: `docs/llms/messaging.md`
- Read-only audit source: `src/Headless.Messaging.{PostgreSql,SqlServer,InMemoryStorage}/Setup.cs` plus their `*DataStorage.cs`, `*Initializer.cs`, and `*OutboxTransaction.cs` files

**Approach:**
- Walk all three storage packages. For each, verify which abstractions register:
  - `IDataStorage` (the combined outbox + persisted-retry contract — single column)
  - `IStorageInitializer` (schema initialization)
- Columns: Outbox+Retry storage, Schema initializer.
- Footnote the matrix to explain the column refinement against origin R7: no `ISubscriptionStorage` abstraction exists; outbox and persisted-retry share the combined `IDataStorage` contract; internal-wiring differences (e.g., SqlServer's diagnostic observer registration) are intentionally not surfaced because they are not chooser-relevant for storage selection.
- Place the storage matrix directly under the transport matrix, both inside `## Provider Capabilities`.

**Patterns to follow:**
- Same table styling as U4.

**Test scenarios:**
- Integration — every storage cell traces to a registration in the corresponding `Setup.cs`. Verified by re-reading code while writing each cell.

**Verification:**
- 3×2 = 6 cells, each derived from a registration in the corresponding package's source.
- The substitution footnote is present and explains the column refinement clearly enough that a reader cross-referencing origin R7 can see why "Subscriptions" was dropped and why the matrix is two columns rather than three.

---

### U6. Add Phase 2 inline callouts and deferred appendix

**Goal:** Mark Phase 2 deferred behavior with inline blockquote callouts at the contextually-relevant sections, and add the consolidated `## Appendix: Deferred to Phase 2` near the end of `docs/llms/messaging.md`.

**Requirements:** origin R10, R11, R12.

**Dependencies:** U1, U2, U3, U4, U5, U7 (callouts reference settled section structure, matrix rows, and U7's inlined-section corrections — Phase 2 references the U7 audit may surface inside messaging.md inlined sections need to be marked, not removed).

**Files:**
- Modify: `docs/llms/messaging.md`

**Approach:**
- Place inline callouts at the points where Phase 2 names would otherwise be expected:
  - Top of the envelope/tenant section authored in U1 — note that `DeliveryKind` envelope metadata is deferred.
  - Top of any publisher-listing subsection — note that the send/broadcast intent split is deferred.
  - In the transport matrix preamble (from U4) — note that the matrix does not include `DeliveryKind` or broadcast columns by design.
  - Anywhere in the existing doc that the audit (U1–U5, U7) surfaces a Phase 2 concept presented as current behavior.
- Each callout follows the Key Technical Decision shape: `> **Deferred to Phase 2 (#232).** [one-sentence reason; link to the appendix].`
- Append the consolidated appendix near the end of the doc as a five-row table:

```
| Item                                  | Tracked in |
|---------------------------------------|------------|
| Send/broadcast publisher intent split | #232       |
| DeliveryKind envelope metadata        | #232       |
| Outbox-decorator telemetry tags       | #232       |
| Rename / migration guide              | #232       |
| Typed-behavior pipeline decision      | #218       |
```

- The appendix is a table only — no written migration guide content.

**Patterns to follow:**
- Existing blockquote-callout style: `> **Security Note:** …` at `src/Headless.Messaging.RabbitMq/README.md:43`.

**Test scenarios:**
- Covers AE2. The appendix table contains five rows for send/broadcast, `DeliveryKind`, outbox-decorator tags, rename migration, and typed-behavior pipeline — each linked to the correct tracking issue.
- Integration — body-text references to Phase 2 concepts appear only inside callouts or the appendix; nowhere in prose are they presented as current behavior.

**Verification:**
- Five appendix rows present, each linked to #232 or #218.
- Every body-text reference to a Phase 2 concept lives inside a callout or the appendix.

---

### U7. Audit and correct provider READMEs and inlined sections in messaging.md

**Goal:** Audit each `src/Headless.Messaging.*/README.md` against its package's `Setup.cs` and the corresponding transport/storage/consumer classes; correct stale capability claims AND stale entry-point API examples; mirror every correction in the corresponding inlined section of `docs/llms/messaging.md`.

**Requirements:** origin R13, R14, R15.

**Dependencies:** U4, U5 (audit findings already drive matrix values; this unit applies them back to the READMEs and to messaging.md's inlined per-package sections).

**Files:**
- Confirmed-stale per source verification (12 READMEs containing `AddMessages(` or `ScanConsumers(`, all will be modified):
  - `src/Headless.Messaging.AwsSqs/README.md`
  - `src/Headless.Messaging.AzureServiceBus/README.md`
  - `src/Headless.Messaging.InMemoryQueue/README.md`
  - `src/Headless.Messaging.InMemoryStorage/README.md`
  - `src/Headless.Messaging.Kafka/README.md`
  - `src/Headless.Messaging.Nats/README.md`
  - `src/Headless.Messaging.OpenTelemetry/README.md`
  - `src/Headless.Messaging.PostgreSql/README.md`
  - `src/Headless.Messaging.Pulsar/README.md`
  - `src/Headless.Messaging.RabbitMq/README.md`
  - `src/Headless.Messaging.RedisStreams/README.md`
  - `src/Headless.Messaging.SqlServer/README.md`
- Modify (sync inlined sections): `docs/llms/messaging.md` — every per-package H1 block whose source README is modified above must have its inlined version updated to match. `messaging.md` is hand-maintained; corrections do not propagate automatically.
- Audit (may or may not modify, depending on what the audit finds):
  - `src/Headless.Messaging.Abstractions/README.md` — already uses the live `AddHeadlessMessaging` + `SubscribeFromAssemblyContaining<Program>()`; audit for capability drift only.
  - `src/Headless.Messaging.Core/README.md` — likely already aligned with #229 retry contract; audit for capability drift only.
  - `src/Headless.Messaging.Dashboard/README.md`, `src/Headless.Messaging.Dashboard.K8s/README.md`, `src/Headless.Messaging.Testing/README.md` — non-transport, non-storage packages; audit only for Phase 2 references and any incidental stale-API mentions.
- Read-only audit source: each package's `Setup.cs` plus its registered transport / storage / consumer classes.

**Approach:**
- For each README in the modify-or-audit set:
  1. Read its Key Features, Quick Start, and Configuration sections.
  2. Open the package's `Setup.cs` and verify which interfaces are registered, which options are validated, and which extension seams are wired.
  3. Correct any capability claim that overstates or understates what is actually wired (U4 / U5 audit findings are the primary input).
  4. Correct stale entry-point API examples (`AddMessages(...)` → `AddHeadlessMessaging(...)`, `ScanConsumers(...)` → `SubscribeFromAssemblyContaining<...>()`) anywhere in the README.
  5. Remove any reference to `ISendPublisher`, `IBroadcastPublisher`, `DeliveryKind`, `FailedRetryCount`, or `FailedRetryInterval` as current behavior. Leave the documented migration table at `docs/llms/messaging.md` lines ~616-627 intact — it intentionally mentions the removed knobs with "replaces" framing.
  6. Apply the same corrections to the inlined version of this package's content in `docs/llms/messaging.md`.
  7. Leave the README (and its inlined section) unchanged if the audit finds no drift.
- Track every touched file in the commit message body so scope discipline is reviewable at a glance.

**Patterns to follow:**
- The Abstractions README's existing structure (already mentions `IScheduledPublisher` at line 13) — preserve, do not rewrite.
- The Core README's retry section as the reference for retry-claim phrasing.

**Test scenarios:**
- Integration — for each touched README, every capability claim traces to a `Setup.cs` registration or to the registered class's behavior. Random spot-check (per origin Success Criteria 3): pick three claims across three different READMEs (one transport, one storage, one Abstractions) and trace each to its source.
- Edge — a README with no drift is not modified (zero-byte diff).
- Edge — a README whose only drift was its entry-point API example (no capability drift) IS modified; "claims drifted" is read inclusively per Key Technical Decisions.

**Verification:**
- Searching `src/Headless.Messaging.*/README.md` for `AddMessages(` and `ScanConsumers(` returns no matches that present them as current behavior. (The 12 known-stale READMEs are all corrected; any not previously known to be stale is unchanged if the audit confirms it was already clean.)
- Searching the same set for `ISendPublisher`, `IBroadcastPublisher`, `DeliveryKind`, `FailedRetryCount`, or `FailedRetryInterval` returns no matches that present them as current behavior.
- For every README modified, the corresponding inlined section in `docs/llms/messaging.md` carries the same corrections — a diff inspection shows both surfaces moving together.
- The random three-claim spot-check (one transport, one storage, one Abstractions) returns three live-code matches.

---

## System-Wide Impact

- **Interaction graph:** No code changes. The surface is documentation: `docs/llms/messaging.md` plus the 17 sibling messaging-package READMEs. Downstream consumers: AI coding agents that ingest `docs/llms/`, contributors landing on a package README via NuGet, and the published NuGet readme surface itself.
- **Error propagation:** N/A — no runtime paths touched.
- **State lifecycle risks:** N/A.
- **API surface parity:** Audit may surface code-side mismatches (a documented capability the code does not deliver, or vice versa). When surfaced, the mismatch is logged as a follow-up issue per Scope Boundaries → Deferred to Follow-Up Work, not patched here.
- **Integration coverage:** There are no code-level integration tests for this plan. The integration scenario worth defending is "downstream coding agent reads the refreshed doc and emits current-API code." This is validated by Success Criteria #2 (no downstream agent emits `ISendPublisher` / `IBroadcastPublisher` / `DeliveryKind` after the refresh lands).
- **Unchanged invariants:** Public messaging API (`IDirectPublisher`, `IOutboxPublisher`, `IScheduledPublisher`, `MessagingOptions`, `RetryPolicyOptions`, `IActivityTagEnricher`) and DI registrations in any `Setup.cs` are NOT changed by this plan. The plan describes them; it does not modify them.

---

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Audit surfaces code-side bugs (capability documented but not delivered, or delivered but not documented). | Log each as a separate issue; preserve origin's "no retry/telemetry/publisher-shape code changes" boundary. |
| Capability matrix becomes stale the moment a transport adds a new feature. | Carry-forward rule (from `transport-wrapper-drift-and-doc-sync`): future PRs that change messaging public surface must update this matrix in the same change. Note this rule in the matrix preamble. |
| README audit across 17 packages plus the inlined messaging.md sections invites scope creep. | U7 Approach: audit each, correct only drift, leave clean READMEs untouched. Touched-file list in commit body provides the discipline signal. The 12 known-stale READMEs are the explicit starting work. |
| Provider-guide's broader README checklist tempts a full rewrite. | Captured under Scope Boundaries → Deferred to Follow-Up Work. |
| `Setup.cs` for a transport package uses indirection (e.g., factory registration) that obscures whether a publisher / capability is wired. | When unclear, the implementer should follow the registration to its concrete class and verify against the contract type. Origin R15 (verification discipline) covers this. |

---

## Documentation / Operational Notes

- After this plan lands, `docs/llms/messaging.md` becomes the discoverable Phase 1 source of truth, and the deferred appendix is the discoverable index of Phase 2 work.
- No rollout, monitoring, or migration impact — pure doc refresh.
- Per `CLAUDE.md` "ALWAYS keep docs/llms synchronized with the code": every future PR that changes messaging public surface (interfaces, options, registered abstractions, transport behavior) must update this doc in the same change. The two new capability matrices are the most-likely future stale points; carry-forward of the `transport-wrapper-drift-and-doc-sync` learning applies.

---

## Sources & References

- **Origin document:** [docs/brainstorms/2026-05-18-messaging-p1-3-capability-matrix-docs-requirements.md](../brainstorms/2026-05-18-messaging-p1-3-capability-matrix-docs-requirements.md)
- Related code: `src/Headless.Messaging.Core/Setup.cs:108-111`, `src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs`, `src/Headless.Messaging.Abstractions/IScheduledPublisher.cs`
- Related issues: [#231](https://github.com/xshaheen/headless-framework/issues/231) (this plan), [#229](https://github.com/xshaheen/headless-framework/issues/229) (retry contract), [#230](https://github.com/xshaheen/headless-framework/issues/230) (OTel enrichment), [#232](https://github.com/xshaheen/headless-framework/issues/232) (Phase 2 publisher split — deferred), [#218](https://github.com/xshaheen/headless-framework/issues/218) (typed-behavior pipeline — deferred)
- Institutional learnings: [docs/solutions/messaging/transport-wrapper-drift-and-doc-sync.md](../solutions/messaging/transport-wrapper-drift-and-doc-sync.md), [docs/solutions/guides/messaging-transport-provider-guide.md](../solutions/guides/messaging-transport-provider-guide.md)
