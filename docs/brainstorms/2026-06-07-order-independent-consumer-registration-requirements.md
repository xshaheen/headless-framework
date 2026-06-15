---
date: 2026-06-07
topic: order-independent-consumer-registration
---

# Order-Independent Consumer Registration

## Summary

Make `IServiceCollection.ForMessage<T>` consumer registration independent of
call order relative to `AddHeadlessMessaging` by deferring the `ConsumerRegistry`
drain from `AddHeadlessMessaging`-time to host start. This dissolves the
distributed-lock wake-up ordering constraint (#390) at its root and removes the
fail-fast ordering guard framework-wide. The fix lives entirely in messaging core;
the distributed-locks package is untouched.

## Problem Frame

`AddDistributedLock` / `AddDistributedSemaphore` register the shared lock-released
wake-up consumer through `ForMessage<DistributedLockReleased>`. That call emits a
`MessageRegistration` singleton which `AddHeadlessMessaging` drains into the
`ConsumerRegistry` **synchronously, once, inside its own call**
(`_DiscoverMessageRegistrations`, `src/Headless.Messaging.Core/Setup.cs:214`).

Consequence: the lock consumer only lands if `AddDistributedLock` runs **before**
`AddHeadlessMessaging`. `AddHeadlessMessaging`-first is a natural setup order, so
this is a live footgun. PR #368 made the failure loud rather than silent — a
fail-fast `InvalidOperationException` when `ForMessage` is called after the
registry exists (`src/Headless.Messaging.Core/Setup.cs:147`) — but loud-crash is
still an ordering constraint the consumer must learn and obey.

The issue framed this as unfixable at the DI layer because `MessagingOptions`
(message-name mapping + prefix) is captured in a `Configure(options.CopyTo)`
delegate rather than a retrievable singleton, so a late registration can't resolve
a consistent message name. There is a real kernel here: the eager drain doesn't
just populate the registry — it also **back-propagates explicit type→name mappings
into `MessagingOptions`** (`Setup.cs:439`), and the publish path resolves names from
that frozen snapshot (`IMessagePublishRequestFactory`). Naively relocating the drain
would leave late mappings out of the snapshot, silently breaking publish-side name
resolution. The fix is therefore not "just defer the drain": the type→name map moves
off `MessagingOptions` and onto the `ConsumerRegistry` as the single source of truth,
fed by both writers (the user-facing mapping API and the deferred drain) and read by
all name-resolution sites — making the registration order irrelevant for every
consumer, not just locks.

## Key Decisions

- **Defer the drain, don't add a second mechanism (Approach B over A).** The
  alternative (issue's proposal) was a lock-specific runtime subscription via
  `IRuntimeSubscriber` at host start. Rejected: it moves lock wake-up off the
  declared-topology path into a dynamic subscription that must declare transport
  bindings after the transport starts, and it leaves the `ForMessage` ordering
  trap in place for the next consumer. Deferring the drain keeps lock wake-up on
  the same static path everything else uses and fixes the root cause once.

- **Registry as the single source of truth for type→name mappings (B2).** B
  requires late mappings to be visible to runtime name resolution. Rather than
  mutate the frozen options instance at startup (contained but a smell), the
  type→name map moves onto the `ConsumerRegistry`; publish, runtime-subscribe, and
  the startup collision-check all resolve through it. Chosen to collapse the
  options/registry name-resolution duplication to one authority, accepting a wider
  messaging-core blast radius than a drain-only relocation. (See plan for unit
  breakdown.)

- **The wake-up is an optimization signal, not a correctness signal.** A lost
  `DistributedLockReleased` only means waiters fall back to polling
  (`PollingCadenceFraction`) and wake slightly later — nothing breaks. This is
  why the localized Approach A would have been *sufficient*; it is not why B was
  chosen. B was chosen for carrying cost, not for delivery strength.

- **Validation moves to host start with the drain.** Deferring the drain defers
  the registration-conflict validation it performs. Errors surface at `StartAsync`
  — before serving traffic — rather than at `AddHeadlessMessaging`-time. Accepted:
  eager partial validation would defeat the order-independence being bought.

- **`ForMessage` stays; only the guard is removed.** `ForMessage` is the
  registration mechanism, not the bug. The fail-fast `InvalidOperationException`
  guard is what becomes obsolete and is deleted.

## Requirements

**Messaging core — order independence**

- R1. A `ForMessage<T>` registration is drained into the `ConsumerRegistry`
  regardless of whether it is called before or after `AddHeadlessMessaging`,
  provided both run during service configuration (before the host is built).
- R2. Drained registrations resolve their message name and prefix against the
  final `MessagingOptions`, so names are identical regardless of registration
  order.
- R3. The drain completes before any consumer of the `ConsumerRegistry` reads it
  to build transport topology at host start (the registry must be populated before
  it is first frozen / enumerated).

**Messaging core — guard removal and validation**

- R4. The fail-fast ordering guard in `ForMessage<T>`
  (`src/Headless.Messaging.Core/Setup.cs:147`) is removed.
- R5. Registration-conflict validation (duplicate / conflicting consumer
  registrations) still runs and fails fast at host start, before message
  processing begins.

**Distributed-locks path**

- R6. Lock and semaphore wake-ups work with `AddDistributedLock` /
  `AddDistributedSemaphore` registered before **or** after `AddHeadlessMessaging`,
  with no source change in `Headless.DistributedLocks.Core`.
- R7. The polling fallback is preserved unchanged when messaging is not registered.

**Cleanup**

- R8. Tests asserting the old fail-fast guard behavior (added in #368) are removed
  or inverted to assert order-independence instead.

## Acceptance Examples

- AE1. **Covers R1, R6.** **Given** an app that calls `AddHeadlessMessaging` and
  then `AddDistributedLock`, **when** the host starts and a lock is released,
  **then** waiters on that resource are woken via the released message (not by
  polling timeout).
- AE2. **Covers R1, R2, R6.** **Given** the reverse order (`AddDistributedLock`
  before `AddHeadlessMessaging`), **when** the host starts, **then** behavior is
  identical to AE1 — same message name, same wake-up.
- AE3. **Covers R4.** **Given** any `ForMessage<T>` call after
  `AddHeadlessMessaging`, **when** services are configured, **then** no
  `InvalidOperationException` is thrown.
- AE4. **Covers R7.** **Given** an app with `AddDistributedLock` and no messaging
  registered, **when** a lock is held by another waiter, **then** acquisition
  proceeds via the polling cadence as it does today.
- AE5. **Covers R5.** **Given** two conflicting consumer registrations for the
  same message, **when** the host starts, **then** startup fails with a clear
  error before any message is processed.

## Scope Boundaries

**Outside this change**

- Approach A (runtime `IRuntimeSubscriber` subscription for lock wake-up) and any
  dynamic transport-topology declaration it would require.
- Any behavioral or API change in `Headless.DistributedLocks.Core` — it keeps its
  static `ForMessage<DistributedLockReleased>` registration.
- Mutating the frozen `MessagingOptions` instance at startup to inject late name
  mappings — rejected in favor of moving the type→name map onto the registry
  (single source of truth).

**Boundary clarification**

- "Order-independent" means *any order within service configuration*. Registration
  after the host is built remains the job of `IRuntimeSubscriber` and is unchanged.

## Dependencies / Assumptions

- A1. **No synchronous consumer of `ConsumerRegistry` contents exists inside
  `AddHeadlessMessaging` after the current drain (`Setup.cs:214`).** Confirmed
  (~95%): the only `GetAll()` readers are `IConsumerServiceSelector` (resolved
  post-build) and the `Bootstrapper` hosted service (host start). Config-time code
  touches only `registry.Descriptors` (the non-freezing path), never `GetAll()`.
- A2. The `Bootstrapper` hosted service (`Setup.cs:364`) and `ConsumerRegister`
  are the first readers of the registry at host start; the deferred drain must be
  ordered ahead of them.
- A3. This is a greenfield framework with no deployed external consumers, so
  removing the guard and changing drain timing are acceptable breaking changes.

## Outstanding Questions

**Deferred to planning**

- Q1. Drain placement mechanism: a lazy drain on the registry's first
  `GetAll()`/freeze (pulling `MessageRegistration` singletons from the
  already-registered `IServiceCollection` snapshot at `Setup.cs:271` plus
  `IOptions<MessagingOptions>`), versus a dedicated startup step ordered ahead of
  the `Bootstrapper`. Trade-off: lazy-on-freeze avoids hosted-service ordering but
  couples drain to first read; explicit step is more visible but adds ordering.
- Q2. Where conflict validation (R5) runs once the drain moves — inside the
  deferred drain itself, or as a separate startup check.

## Sources / Research

- `src/Headless.Messaging.Core/Setup.cs:136` — `ForMessage<T>` and the fail-fast
  guard at `:147`.
- `src/Headless.Messaging.Core/Setup.cs:214` / `:390` — `_DiscoverMessageRegistrations`,
  the eager drain to relocate.
- `src/Headless.Messaging.Core/Setup.cs:351` — `MessagingOptions` registered via
  `Configure(options.CopyTo)` (the issue's stated blocker; moot under deferral).
- `src/Headless.Messaging.Core/Setup.cs:271`, `:364` — service-collection snapshot
  singleton and `Bootstrapper` hosted-service registration.
- `src/Headless.Messaging.Core/ConsumerRegistry.cs` — build, freeze-on-first-`GetAll`,
  post-freeze `Register` throw.
- `src/Headless.Messaging.Core/Registration/MessageRegistration.cs` — the singleton
  the drain consumes.
- `src/Headless.DistributedLocks.Core/RegularLocks/DistributedLockConsumerRegistration.cs`
  — `TryAddLockReleasedConsumer` (the lock-side `ForMessage` call, unchanged here).
- `src/Headless.DistributedLocks.Core/Setup.cs` — `AddDistributedLock`; `#390`
  tracking comment near `:124`.
- Related: #368 (introduced the guard), #287 (distributed-locks program), #390
  (this issue).
</content>
</invoke>
