---
title: "feat: Add IInitializer completion signal for background initialization services"
type: feat
status: active
date: 2026-04-06
---

# feat: Add IInitializer completion signal for background initialization services

## Overview

Add an `IInitializer` interface to `Headless.Hosting` so that `FeaturesInitializationBackgroundService`, `PermissionsInitializationBackgroundService`, and `SettingsInitializationBackgroundService` can signal initialization completion. `HeadlessTestServer` auto-discovers all `IInitializer` registrations and awaits them, eliminating the current `RemoveHostedService` workarounds that prevent integration tests from exercising initialization code.

## Problem Frame

The three initialization services fire async work in `StartAsync` using Polly retry pipelines configured with `TimeProvider`. In tests, `FakeTimeProvider` is injected but never advanced, causing retry delays to hang indefinitely if the first attempt fails. More fundamentally, there is **no completion signal** — tests cannot know when initialization finishes. The only workaround is `RemoveHostedService<T>()`, which means initialization logic is never tested, and downstream consumers must replicate all three removals.

Related: xshaheen/headless-framework#212

## Requirements Trace

- R1. `IInitializer` interface in `Headless.Hosting` with `IsInitialized` property and `WaitForInitializationAsync` method
- R2. All three initialization services implement `IInitializer` and signal completion via `TaskCompletionSource`
- R3. Each service registered as both `IHostedService` and `IInitializer` via singleton forwarding
- R4. `HeadlessTestServer` auto-awaits all `IInitializer` registrations during `InitializeAsync` with timeout
- R5. `RemoveHostedService` workarounds removed from all framework test bases
- R6. Integration tests pass with services running (not removed)

## Scope Boundaries

- **In scope**: IInitializer interface, three service implementations, test infrastructure, workaround removal
- **Out of scope**: Unifying `IBootstrapper` (Messaging) with `IInitializer` — possible follow-up but different semantics (imperative trigger vs passive wait). Messaging keeps its own pattern.
- **Out of scope**: Fixing Polly/FakeTimeProvider retry hang — with Testcontainers providing a real DB, first attempt succeeds. The timeout on `WaitForInitializationAsync` provides a clear error if it doesn't.

## Context & Research

### Relevant Code and Patterns

- **IBootstrapper precedent** (`src/Headless.Messaging.Core/IBootstrapper.cs`): singleton interface with `IsStarted` + `BootstrapAsync`, registered via singleton forwarding pattern (`TryAddSingleton<Concrete>` + `TryAddSingleton<IInterface>(sp => ...)` + `AddHostedService(sp => ...)`). Tests call `BootstrapAsync` directly.
- **Three initialization services** (`src/Headless.*.Core/Seeders/*InitializationBackgroundService.cs`): identical pattern — implement `IHostedService, IDisposable`, fire-and-forget background task from `StartAsync`, Polly retry with `TimeProvider`. No completion signal. Minor differences in `StopAsync` behavior (Features awaits task, Permissions only cancels, Settings uses linked CTS + WaitAsync).
- **Current registration** (`src/Headless.*.Core/Setup.cs`): plain `AddHostedService<T>()` — services not resolvable by concrete type.
- **HeadlessTestServer** (`src/Headless.Testing.AspNetCore/HeadlessTestServer.cs`): has `WaitForReadiness(Func<IServiceProvider, Task>, TimeSpan?)` callback pattern. Injects `FakeTimeProvider` via `AddTestTimeProvider()`.
- **RemoveHostedService helper** (`src/Headless.Hosting/DependencyInjection/DependencyInjectionExtensions.cs:332`): matches on `ImplementationType == typeof(T)`. Does NOT work for factory-based registrations — relevant for the new singleton forwarding pattern.

### Institutional Learnings

- **Use `TaskCompletionSource` with `RunContinuationsAsynchronously`** — never `ManualResetEventSlim.Wait()` on ThreadPool threads (from `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md`)
- **Fire-and-forget tasks must be observed** — attach fault observers to `Task.Run` calls (same source)
- **Treat startup gates as first-class concerns** — new components added to an already-active system must honor current state (from `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md`)

## Key Technical Decisions

- **New `IInitializer` rather than generalizing `IBootstrapper`**: `IBootstrapper` has imperative "trigger" semantics (`BootstrapAsync`) plus `IAsyncDisposable`. `IInitializer` has passive "wait" semantics — more appropriate for services that start automatically via `IHostedService`. Keeps concerns separate; unification is a future option.
- **`TaskCompletionSource` with `RunContinuationsAsynchronously`**: thread-safe, non-blocking, matches institutional learnings. Concurrent callers share the same in-flight `Task`.
- **Auto-discovery in `HeadlessTestServer`**: safer than opt-in — tests that forget to opt-in currently deadlock silently. Auto-await with timeout converts silent hangs to clear `TimeoutException`.
- **Singleton forwarding registration**: same pattern as Messaging's `IBootstrapper`. Required because `RemoveHostedService<T>` matches on `ImplementationType` — factory registrations need the concrete singleton to be independently registered first.
- **Exception propagation**: if initialization fails permanently (after all retries), `TrySetException` on the TCS so `WaitForInitializationAsync` throws rather than hanging.

## Open Questions

### Resolved During Planning

- **Should `HeadlessTestServer` auto-await or require opt-in?**: Auto-await. Silent deadlocks are worse than explicit timeouts. Tests that need to bypass can use `RemoveHostedService` or a config flag.
- **Should we also fix the Polly/FakeTimeProvider interaction?**: No — with Testcontainers providing a real DB, first attempt succeeds. The timeout on `WaitForInitializationAsync` is the safety net. Fixing Polly timing is orthogonal.

### Deferred to Implementation

- **Exact timeout value for auto-await**: likely 30s (matching existing `WaitForReadiness` default), but confirm during implementation
- **Premature shutdown timeout message**: if `DisposeAsync` is called before initialization completes, cancellation fires and the background task returns early without calling `TrySetResult/Exception`. Consider `TrySetCanceled(cancellationToken)` in `StopAsync` for a cleaner error than a generic timeout

## Implementation Units

- [ ] **Unit 1: Add `IInitializer` interface to `Headless.Hosting`**

  **Goal:** Define the public contract for initialization completion signaling.

  **Requirements:** R1

  **Dependencies:** None

  **Files:**
  - Create: `src/Headless.Hosting/Initialization/IInitializer.cs`

  **Approach:**
  - Interface with `bool IsInitialized { get; }` and `Task WaitForInitializationAsync(CancellationToken cancellationToken = default)`
  - Place in `Headless.Hosting.Initialization` namespace (or just `Headless.Hosting` — follow existing namespace conventions in the package)
  - XML docs explaining: `IsInitialized` returns true only after successful completion; `WaitForInitializationAsync` allows concurrent callers to share the same wait

  **Patterns to follow:**
  - `IBootstrapper` in `src/Headless.Messaging.Core/IBootstrapper.cs` for shape/naming
  - `ISeeder` in `src/Headless.Hosting/Seeders/ISeeder.cs` for placement conventions

  **Test expectation:** none — pure interface, no behavior

  **Verification:**
  - Interface compiles and is publicly visible from `Headless.Hosting`

- [ ] **Unit 2: Implement `IInitializer` on all three initialization services**

  **Goal:** Each service signals completion/failure through `IInitializer`, with updated DI registration using singleton forwarding.

  **Requirements:** R2, R3

  **Dependencies:** Unit 1

  **Files:**
  - Modify: `src/Headless.Features.Core/Seeders/FeaturesInitializationBackgroundService.cs`
  - Modify: `src/Headless.Permissions.Core/Seeders/PermissionsInitializationBackgroundService.cs`
  - Modify: `src/Headless.Settings.Core/Seeders/SettingsInitializationBackgroundService.cs`
  - Modify: `src/Headless.Features.Core/Setup.cs`
  - Modify: `src/Headless.Permissions.Core/Setup.cs`
  - Modify: `src/Headless.Settings.Core/Setup.cs`

  **Approach:**
  - Add `TaskCompletionSource` field with `TaskCreationOptions.RunContinuationsAsynchronously`
  - Implement `IsInitialized => _tcs.Task.IsCompletedSuccessfully`
  - Implement `WaitForInitializationAsync` => `_tcs.Task.WaitAsync(cancellationToken)`
  - In the background initialization method: `_tcs.TrySetResult()` on success, `_tcs.TrySetException(ex)` on permanent failure
  - **Conditional skip / early-return**: if initialization is guarded by an options flag and skipped, call `_tcs.TrySetResult()` in the early-return branch — otherwise `WaitForInitializationAsync` hangs until timeout
  - Use `TryAddSingleton` for concrete and interface registrations (guards against double-registration if `_AddCore` is called more than once), `AddHostedService` for the hosted service:
    ```
    services.TryAddSingleton<ConcreteService>();
    services.TryAddSingleton<IInitializer>(sp => sp.GetRequiredService<ConcreteService>());
    services.AddHostedService(sp => sp.GetRequiredService<ConcreteService>());
    ```
    This matches the `IBootstrapper` registration pattern exactly (see `Setup.cs:210-212`)
  - **StopAsync normalization**: normalize all three services during this unit while the files are open — `PermissionsInitializationBackgroundService.StopAsync` currently only cancels the CTS without awaiting the task; this risks unobserved exceptions when TCS is added. Align all three to: cancel CTS, await background task, handle `OperationCanceledException`
  - Each `.csproj` may need a project reference to `Headless.Hosting` if not already present (check existing references)

  **Patterns to follow:**
  - Messaging's `Bootstrapper` registration in `src/Headless.Messaging.Core/Setup.cs:210-212`
  - `TaskCompletionSource` with `RunContinuationsAsynchronously` per institutional learnings

  **Test scenarios:**
  - Happy path: Service initializes successfully → `IsInitialized` is true, `WaitForInitializationAsync` completes immediately
  - Happy path: `WaitForInitializationAsync` called before initialization completes → awaits until completion
  - Edge case: Multiple concurrent callers of `WaitForInitializationAsync` → all complete when initialization finishes
  - Error path: Initialization fails permanently → `WaitForInitializationAsync` throws the exception
  - Edge case: `WaitForInitializationAsync` with already-cancelled token → throws `OperationCanceledException`

  **Verification:**
  - Each service resolves as both `IHostedService` and `IInitializer` from DI
  - Existing unit tests still pass

- [ ] **Unit 3: Update `HeadlessTestServer` to auto-await `IInitializer` services**

  **Goal:** Test infrastructure automatically waits for all registered initializers during startup, with timeout protection.

  **Requirements:** R4

  **Dependencies:** Unit 1

  **Files:**
  - Modify: `src/Headless.Testing.AspNetCore/HeadlessTestServer.cs`
  - Modify: `src/Headless.Testing.AspNetCore/Headless.Testing.AspNetCore.csproj` — add `ProjectReference` to `Headless.Hosting` (required for `IInitializer` resolution)

  **Approach:**
  - During `InitializeAsync` (after host starts), resolve `IEnumerable<IInitializer>` and await all via `Task.WhenAll` with timeout
  - Use existing `WaitForReadiness` pattern or add a dedicated step — whichever integrates more cleanly
  - Timeout should produce a clear error message naming which initializer(s) haven't completed

  **Patterns to follow:**
  - Existing `WaitForReadiness` callback in `HeadlessTestServer`
  - Messaging's test harness at `src/Headless.Messaging.Testing/MessagingTestHarness.cs:85-86`

  **Test scenarios:**
  - Happy path: All initializers complete within timeout → `InitializeAsync` succeeds
  - Error path: An initializer times out → `TimeoutException` with descriptive message
  - Edge case: No `IInitializer` registrations → `InitializeAsync` succeeds without waiting
  - Error path: An initializer throws during initialization → exception propagates to test setup

  **Verification:**
  - `HeadlessTestServer.InitializeAsync` completes successfully when initializers succeed
  - Tests get a clear error (not a silent hang) when initialization fails

- [ ] **Unit 4: Remove `RemoveHostedService` workarounds from framework test bases**

  **Goal:** Framework integration tests exercise the actual initialization code path instead of bypassing it.

  **Requirements:** R5, R6

  **Dependencies:** Units 2, 3

  **Files:**
  - Modify: `tests/Headless.Features.Tests.Integration/TestSetup/FeaturesTestBase.cs`
  - Modify: `tests/Headless.Settings.Tests.Integration/TestSetup/SettingsTestBase.cs`
  - Modify: `tests/Headless.Permissions.Tests.Integration/TestSetup/SettingsTestBase.cs`

  **Approach:**
  - Remove `RemoveHostedService<*InitializationBackgroundService>()` calls
  - Verify integration tests pass with services running — initialization should succeed against Testcontainers DB on first attempt

  **Test scenarios:**
  - Integration: All existing integration tests pass without the workaround
  - Integration: Initialization actually runs (services seed data) — verify by checking seeded data exists

  **Verification:**
  - All integration test suites pass
  - No `RemoveHostedService<*InitializationBackgroundService>` references remain in the codebase

## System-Wide Impact

- **Interaction graph:** `HeadlessTestServer.InitializeAsync` → resolves `IInitializer[]` → awaits each service's TCS. Production is unaffected — `IInitializer` is only consumed by test infrastructure.
- **Error propagation:** Initialization failure flows: service catches final exception → `TrySetException` on TCS → `WaitForInitializationAsync` throws → `HeadlessTestServer.InitializeAsync` fails → test fixture setup fails with clear error.
- **API surface parity:** `RemoveHostedService<T>` matches on `ImplementationType` and won't find factory-registered services after singleton forwarding. Since the goal is to stop removing these services entirely, this is expected.
- **Unchanged invariants:** Production startup behavior is unchanged — services still fire-and-forget. The `IInitializer` TCS is only awaited by test infrastructure.

## Risks & Dependencies

| Risk | Mitigation |
|------|------------|
| Initialization hangs in tests (Polly + FakeTimeProvider) if first DB attempt fails | Testcontainers ensures DB is ready; timeout on `WaitForInitializationAsync` converts silent hang to clear `TimeoutException` |
| Conditional skip path leaves TCS never completed → timeout hang | Call `_tcs.TrySetResult()` in every early-return branch of initialization (addressed in Unit 2) |
| `PermissionsInitializationBackgroundService.StopAsync` doesn't await background task | Normalize all three `StopAsync` implementations in Unit 2 |
| `Headless.Testing.AspNetCore` doesn't reference `Headless.Hosting` — won't compile | Add `ProjectReference` in Unit 3 (addressed explicitly) |
| Singleton forwarding changes DI resolution behavior | Follows established Messaging pattern; test DI resolution explicitly |

## Future Considerations

- **Unify `IBootstrapper` and `IInitializer`**: `IBootstrapper` could implement `IInitializer`, giving Messaging services auto-discovery in `HeadlessTestServer`. Separate issue — different semantics and scope.
- **Configurable retry for tests**: If tests need to verify retry behavior, provide a way to configure shorter delays or advance `FakeTimeProvider`.

## Sources & References

- Related issue: xshaheen/headless-framework#212
- IBootstrapper pattern: `src/Headless.Messaging.Core/IBootstrapper.cs`
- Messaging registration: `src/Headless.Messaging.Core/Setup.cs:210-212`
- Institutional learning: `docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md`
- Institutional learning: `docs/solutions/concurrency/startup-pause-gating-and-half-open-recovery.md`
