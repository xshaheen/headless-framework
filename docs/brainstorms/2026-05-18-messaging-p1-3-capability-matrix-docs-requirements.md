---
date: 2026-05-18
topic: messaging-p1-3-capability-matrix-docs
---

# Messaging P1.3 — Capability Matrix and Phase 1 Docs Refresh

## Summary

Refresh `docs/llms/messaging.md` so it accurately represents the current Phase 1 messaging surface — envelope and tenant rules, the new retry contract from #229, OTel tag-enrichment from #230, and a two-table provider capability matrix (transport / storage) — while marking Phase 2 work (send/broadcast names, `DeliveryKind`, rename migration) as explicitly deferred via inline callouts plus a consolidated appendix. Provider READMEs are audited for stale capability claims and corrected against the live DI registrations.

---

## Problem Frame

The current canonical messaging doc and several provider READMEs predate the retry-contract rework (#229) and the OTel enricher extension point (#230). A contributor or coding agent reading them today cannot reliably tell what is part of the Phase 1 surface versus what is scheduled for the Phase 2 publisher-intent split (#232) or the typed-behavior decision (#218).

The risk is concrete and recurring: AI agents emit code that references legacy CAP-style retry knobs (`FailedRetryCount`, `FailedRetryInterval`) that no longer exist; sample code assumes `ISendPublisher` / `IBroadcastPublisher` / `DeliveryKind` are current APIs; and provider README capability claims diverge from what each package's `Setup*.cs` actually registers. Each drift event compounds because downstream consumers and downstream agents trust the docs.

---

## Requirements

**Canonical doc content (`docs/llms/messaging.md`)**

- R1. Document the envelope fields and tenant-header rules for the current Phase 1 surface (`IDirectPublisher`, `IOutboxPublisher`, `IScheduledPublisher`) — message identity, headers, tenant propagation behavior, and which fields are publisher-supplied versus framework-supplied.
- R2. Document the retry contract from #229 in a dedicated section: `MessagingOptions.RetryPolicy` as the single composition point, first-level inline retry (`MaxInlineRetries`), second-level persisted delayed retry (`MaxPersistedRetries`) with explicit next-attempt time, attempt counting (total including original execution), exception classification (retryable, non-retryable, cancellation), the `OnExhausted` callback, and the legacy CAP-style knobs that are gone.
- R3. Document the OTel enrichment surface from #230: the `IActivityTagEnricher` extension point, the default Headless tags (`headless.messaging.tenant_id`, completion-mode tags where determinable), tenant-tag suppression for shared trace backends, ordered enricher registration, and enricher-exception isolation.
- R4. Examples and prose in the canonical doc use `IDirectPublisher` and `IOutboxPublisher` (and `IScheduledPublisher` where scheduling is the use case). No example, snippet, or interface reference presents `ISendPublisher`, `IBroadcastPublisher`, or `DeliveryKind` as current behavior.

**Provider capability matrix**

- R5. The doc includes a "Provider Capabilities" section containing two matrices: a transport-provider matrix and a storage-provider matrix.
- R6. The transport matrix has one row per transport package — RabbitMq, Nats, AzureServiceBus, AwsSqs, Kafka, Pulsar, RedisStreams, InMemoryQueue — with at minimum these columns: Direct publish, Consume, Native scheduled/delayed delivery, Ordering guarantee shape, Tenant header passthrough.
- R7. The storage matrix has one row per storage package — PostgreSql, SqlServer, InMemoryStorage — with at minimum these columns: Outbox storage, Persisted retry queue, Subscription tracking.
- R8. Every matrix cell value is derived from the live source of truth — the package's `Setup*.cs` registrations, the concrete classes registered for each abstraction, and the interfaces actually wired up — not from existing README prose. Where the matrix and an existing README disagree, the README is corrected to match the matrix (which matches the code).
- R9. The matrix does not include columns for Phase 2 concepts (`DeliveryKind`, send vs broadcast intent). The transport matrix's "Native scheduled" column reflects transport-native delayed delivery only; it does NOT claim a transport "lacks" scheduling when scheduling is available framework-wide via the outbox decorator.

**Phase 2 deferral**

- R10. The Phase 2 publisher-intent split (`ISendPublisher`, `IBroadcastPublisher`), `DeliveryKind` envelope metadata, the typed-behavior pipeline decision, the outbox-decorator telemetry tags, and the rename migration guide appear in the doc only as: (a) inline "Deferred to Phase 2" callouts at the sections where readers would expect them, and (b) a consolidated "Deferred to Phase 2" appendix near the end of the doc.
- R11. The appendix is a table listing each deferred item with its tracking issue (#232 for send/broadcast publishers, `DeliveryKind`, outbox decorator telemetry tags, and the rename migration; #218 for the typed-behavior pipeline decision). The appendix replaces, not supplements, any inline migration-guide content.
- R12. The doc does not include a written Phase 2 migration guide. The rename-migration entry in the appendix is a placeholder pointing to #232.

**Provider READMEs**

- R13. `src/Headless.Messaging.Abstractions/README.md` is audited so its key-features list, quick-start, and any interface references match the actual abstractions surface today (which already includes `IScheduledPublisher`, `IOutboxTransaction`, `IRuntimeSubscriber`, and `IRetryBackoffStrategy` as customization seam). Stale references — including any legacy CAP-style retry knobs — are removed.
- R14. Each README under `src/Headless.Messaging.*/README.md` is audited for capability claims: which publisher interfaces it registers, retry behavior, telemetry tags emitted, ordering guarantees, multi-tenancy handling. READMEs whose claims already match live registrations are left unchanged. READMEs that overstate or understate capabilities are corrected.

**Verification discipline**

- R15. Before writing or revising any capability claim, the author opens each affected package's `Setup*.cs` (and any related configuration types) and confirms which interfaces are registered, which options are validated at startup, and which extension seams are wired up. Claims that cannot be traced back to a registration or a public type are not made.

---

## Acceptance Examples

- AE1. **Covers R8, R14.** Given the existing README for a transport provider claims "supports native scheduled delivery", when the author opens that package's `Setup*.cs` and finds no registration for transport-native delayed delivery (only the framework-level outbox path is used), the transport-matrix cell records "no" for native scheduled delivery and the README is corrected to describe how scheduling works for that transport (via the outbox decorator) rather than claiming a native broker feature that does not exist.
- AE2. **Covers R10, R11.** Given a reader scans the doc looking for "what changes in Phase 2", when they read the "Deferred to Phase 2" appendix they see a table with rows for send/broadcast publishers, `DeliveryKind`, outbox decorator telemetry tags, the rename migration, and the typed-behavior pipeline decision — each row linked to #232 or #218 — and nowhere in the body does the doc present the deferred names as if they were live APIs.
- AE3. **Covers R2.** Given a reader is configuring retry behavior, when they read the retry section they see `MessagingOptions.RetryPolicy` as the single composition point with `MaxInlineRetries`, `MaxPersistedRetries`, `BackoffStrategy`, and `OnExhausted` — and they do NOT see any reference to `FailedRetryCount`, `FailedRetryInterval`, or `Added`-plus-lookback retry selection as current behavior.

---

## Success Criteria

- A new contributor reading `docs/llms/messaging.md` end-to-end can answer "what is safe to use today?" and "what is deferred to Phase 2?" without grepping the codebase.
- A downstream coding agent (planning, codegen, code-review) that uses the doc as context never emits a reference to `ISendPublisher`, `IBroadcastPublisher`, `DeliveryKind`, `FailedRetryCount`, or `FailedRetryInterval` as current behavior.
- Spot-checking any three random capability claims (one from each matrix, one from a provider README) against the corresponding `Setup*.cs` returns three matches — no drift between docs and live registrations.
- The dev-plan handoff has no open product question. Planning chooses sequencing and editing strategy; it does not need to invent which APIs are current, which columns the matrix has, or how Phase 2 is marked.

---

## Scope Boundaries

- No retry, telemetry, or publisher-shape code changes. Production code is treated as fixed within this issue; if the audit surfaces a code-level inconsistency, it is recorded as a new issue, not patched here.
- No full Phase 2 rename/migration guide. The appendix points to #232 as the placeholder.
- No edits to provider READMEs whose capability claims already match the live registrations — audit-and-fix only, not blanket rewrite.
- No new doc file. `docs/llms/messaging-envelope.md` is not created; envelope material lives inside `docs/llms/messaging.md`.
- No restructuring of unrelated sections in `docs/llms/messaging.md`. The existing "Quick Orientation", "Agent Instructions", and inlined per-package READMEs stay as-is except where they make stale capability claims that the audit catches.
- No documentation of the Phase 2 `IConsumeBehavior<T>` / `IPublishBehavior<T>` design (#218). The appendix only records that the decision is deferred.

---

## Key Decisions

- **Single combined doc, not a split envelope/capability file.** Chosen to minimize sync points across the canonical doc and the individual READMEs; a third file would force three-way reconciliation every time a capability changed. Matches the issue's "only if we deliberately split" framing.
- **Two separate matrices (transport / storage), not one combined table.** Chosen because transport and storage are independent axes in this framework — a single table would have many misleading N/A cells, and readers would struggle to compare like-for-like.
- **Phase 2 marked via inline callouts plus a consolidated appendix.** Chosen because top-to-bottom readers hit the marker contextually (right where they would otherwise expect the deferred behavior), and scanners get a single appendix index of everything that is not yet shipped.
- **Live `Setup*.cs` registrations are the source of truth for capability claims.** Chosen explicitly to prevent legacy README prose from re-entering the matrix. If an existing README overstates support, the README is wrong, not the matrix.

---

## Dependencies / Assumptions

- Depends on #229 and #230 being complete — both are CLOSED as of brainstorm time (verified on GitHub).
- Does not depend on #232 (Phase 2 publisher split) or #218 (typed-behavior pipeline). Both are referenced only via the deferred appendix and may evolve independently.
- Assumes the live retry-contract code from #229 and the OTel enricher code from #230 are the final shapes for Phase 1 and will not be revised before the docs land.
- Assumes `IScheduledPublisher` belongs to the documented Phase 1 surface — verified: it is defined in `src/Headless.Messaging.Abstractions/IScheduledPublisher.cs` and registered in `src/Headless.Messaging.Core/Setup.cs:109` (as the same instance as `OutboxPublisher`).
- Assumes the `IRetryBackoffStrategy` interface remains a public customization seam under `RetryPolicyOptions.BackoffStrategy` (verified at `src/Headless.Messaging.Core/Configuration/RetryPolicyOptions.cs:83`) and that the legacy top-level CAP-style retry knobs are no longer accessible from `MessagingOptions`.
- Assumes the dashboard packages (`Headless.Messaging.Dashboard`, `Headless.Messaging.Dashboard.K8s`) and the testing harness (`Headless.Messaging.Testing`) are NOT part of the provider capability matrices — they are tools, not transports or storages. The audit still checks their READMEs for stale Phase 2 claims.

---

## Outstanding Questions

### Resolve Before Planning

(None — all product decisions are settled.)

### Deferred to Planning

- [Affects R6][Technical] Final exact column set for the transport matrix beyond the five committed minimums (Direct, Consume, Native scheduled, Ordering, Tenant header). Candidate additions surfaced during dialogue but not committed: "Native dead-letter handling", "Native consumer prefetch / concurrency", "Native message TTL". Each costs documentation churn; planning should pick based on whether the column distinguishes providers meaningfully.
- [Affects R7][Technical] Whether the storage matrix should include a "Schema migration story" or "Multi-tenancy partitioning" column. Same decision shape as above.
- [Affects R14][Needs research] Which specific provider READMEs currently overstate or understate capabilities. The audit will find this by reading each `Setup*.cs` and comparing to its sibling README; the count of touched READMEs is not predictable from this brainstorm.
- [Affects R3][Technical] Whether the OTel section should include a default-tag reference table or only prose. Planning chooses based on how many default tags exist after #230 lands.
- [Affects R10][Technical] Visual form of the inline "Deferred to Phase 2" callouts — markdown blockquote, admonition, callout block, or plain bold paragraph. Pick whatever renders cleanly in the existing `docs/llms/` toolchain.
