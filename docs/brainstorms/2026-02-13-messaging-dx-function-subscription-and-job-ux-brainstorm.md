---
date: 2026-02-13
topic: messaging-dx-function-subscription-and-job-ux
---

# Messaging DX: Function Subscription and Job UX

## What We're Building
We are improving developer experience in Headless.Messaging for consumer registration, message publishing, and scheduling operations. The primary user is application developers. The target is a minimal API that prioritizes cognitive load first, then code footprint, while enforcing strong safety controls.

The product direction is:
- Runtime function subscriptions for regular messages, DI-friendly, broker-attached.
- Scheduled jobs remain class-based, with a simpler wrapper abstraction.
- Shared execution core between class handlers and function handlers so behavior remains consistent.

The desired minimal surface is: `Subscribe`, `Unsubscribe`, `Publish`, `ScheduleOnce`, `DefineRecurring`, `TriggerJob`, `EnableJob`, `DisableJob`, `ListJobs`, and `ListExecutions`.

## Why This Approach
We considered a class-only model, a function-first model, and a hybrid model. Hybrid is the best fit for the stated priorities and constraints. It preserves scheduling reliability and existing framework strengths while removing friction for common message subscription use cases.

This approach aligns with common ecosystem patterns: function ergonomics for quick onboarding plus explicit scheduling primitives with durable state and strict execution guarantees.

## Key Decisions
- Optimize for app developers.
- Priority order: cognitive load, code footprint, guardrails, time-to-first-success.
- Runtime function subscriptions are DI-friendly and broker-attached.
- Runtime subscriptions are ephemeral and not persisted across restarts.
- Lambda/function handlers are for regular messages only.
- For class-based scheduled jobs add a wrapper abstraction to reduce boilerplate.
- Scheduled job execution is local-only.
- Durable storage for jobs is required.
- `topic` and `group` are optional; defaults are deterministic convention-based names.
- Guardrails are strict by default (fail fast), with explicit opt-out.
- Breaking changes are allowed.
- Use a new DI scope per job execution.
- Out of scope for this brainstorm: centralized hub coordination and transport-vs-scheduler routing strategy standardization.

## Resolved Questions
- Route scheduled jobs through broker: no.
- Runtime message subscriptions broker-attached: yes.
- Pure lambda mode: no.
- Lambda scheduling support: no.
- Durable job persistence required: yes.

## Open Questions
- None.

## Next Steps
Proceed to planning with explicit acceptance criteria for:
1. Public API shape and naming.
2. Shared execution-core parity between class and function paths.
3. Strict guardrail behavior and opt-out policy.
4. Migration strategy for planned breaking changes.
