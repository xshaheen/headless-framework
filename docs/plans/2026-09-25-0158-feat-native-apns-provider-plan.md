---
title: Native APNs Push Provider - Plan
type: feat
date: 2026-09-25
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: x-plan-bootstrap
execution: code
---

# Native APNs Push Provider - Plan

## Goal Capsule

- **Objective:** A host can deliver iOS push notifications straight to Apple Push Notification service through `IPushNotificationService`, with no Firebase project. It learns which device tokens Apple has retired through the same `Unregistered` status Firebase already reports.
- **Means:** A new `Headless.PushNotifications.Apns` provider package. It talks HTTP/2 to APNs with a cached ES256 provider token (KTD1, KTD3) and registers through the existing `UseApns` setup-builder slots (KTD8).
- **Authority:** Product Contract Requirements win on behavior. KTDs win on mechanism. Repository conventions in `CLAUDE.md` and `docs/solutions/conventions/provider-setup-and-options.md` bind every unit.
- **Stop conditions:** Stop and report if either of these holds:
  - An APNs behavior the plan relies on proves false in the fake-server tests and changes a public option or status mapping.
  - The `CollapseKey` abstraction change (U1) breaks a consumer surface beyond the two providers.
- **Execution profile:** Standard .NET library work across 6 units. It adds one new source package, one harness project, and one new test project, and changes Abstractions, Firebase, Composition tests, and docs.
- **Tail ownership:** The caller (x-autopilot) owns simplification, review, commit, push, and PR.

---

## Product Contract

### Summary

Add `Headless.PushNotifications.Apns`, a native APNs provider beside Firebase. It sends alert and VoIP pushes over HTTP/2 with token-based authentication from a `.p8` key. It fans multicast out across concurrent streams and maps APNs responses onto the three-state `PushNotificationResponse`. The request contract gains an optional collapse key, which both providers honor. A new push-notification conformance harness pins the portable contract that Firebase and APNs both pass.

### Problem Frame

iOS is reachable today only through Firebase's APNs bridge (`docs/llms/push-notifications.md`). A host that does not run Firebase has no iOS provider. So does a host that wants APNs-native control over topic, priority, push type, and collapse behavior. Issue #952 asks for that provider.

Two premises in the issue do not match the repository, and the plan corrects both:

- **No conformance suite exists.** Push tests are unit-only: Abstractions, Composition, Dev, and Firebase. There is no harness to pass. U2 creates one.
- **No invalidation callback exists.** A dead token is signaled only by `PushNotificationResponseStatus.Unregistered` on the returned response. That status is the "shared invalidation path" the APNs provider must use.

### Requirements

**Delivery**

- R1. A `SendToDeviceAsync` through the APNs provider sends one HTTP/2 request to `/3/device/<token>` on the configured environment host.
- R2. Each request carries a valid ES256 provider token (`kid` = key id, `iss` = team id, `iat` = mint time) and these headers: `apns-topic`, `apns-push-type`, `apns-priority`, `apns-id`, and `apns-collapse-id` when the request has a collapse key.
- R3. The JSON payload puts title and body under `aps.alert` and each `Data` entry as a top-level key beside `aps`.
- R4. `SendMulticastAsync` returns exactly one response per input token, in input order. It sends the requests concurrently up to a configurable bound.

**Outcome mapping**

- R5. HTTP 200 maps to `Succeeded`, with the request's `apns-id` as `MessageId`.
- R6. Any HTTP 410 maps to `Unregistered`, whatever its reason. Apple defines 410 as a token that is no longer active, and its current reasons are `Unregistered` and `ExpiredToken`.
- R7. `BadDeviceToken` maps to `Failure` by default. It maps to `Unregistered` only when the host opts in.
- R8. Every other APNs rejection maps to `Failure`. So does any exception a send raises other than caller cancellation, including transport faults left after retries and resilience rejections such as an open circuit. The error string names the APNs `reason` and HTTP status, or the exception type. A multicast therefore never loses results already computed.
- R9. Caller cancellation propagates as `OperationCanceledException`. It is never reported as a per-token failure.

**Validation**

- R10. Invalid input throws `ArgumentException` before any network call:
  - a null or blank token
  - a null request
  - a blank title or body
  - a `Data` key named `aps`
  - a collapse key over 64 UTF-8 bytes
  - a serialized payload over 4096 bytes (5120 for VoIP)
  - an empty multicast list
- R11. Options are validated at startup. Missing or malformed key id, team id, private key, or bundle id fails `ValidateOnStart`.

**Configuration**

- R12. Options select the environment (production or sandbox), bundle id, push type (alert or VoIP), priority, the `BadDeviceToken` opt-in, and multicast concurrency.
- R13. The provider registers as the default (`setup.UseApns(…)`) or as a named instance (`setup.AddNamed(name, i => i.UseApns(…))`). Each instance has isolated options and its own HTTP client.

**Cross-provider contract**

- R14. `PushNotificationRequest` gains an optional `CollapseKey`. APNs sends it as `apns-collapse-id`. Firebase sends it as the Android collapse key and as the `apns-collapse-id` header of its APNs bridge.
- R15. A shared conformance suite pins the portable `IPushNotificationService` contract, and both Firebase and APNs pass it.
- R16. `docs/llms/push-notifications.md` lists APNs beside Firebase in the provider matrix and documents its options, status mapping, and constraints.

### Acceptance Examples

- AE1. **Covers R1, R2, R5.**
  - **Given** an APNs service configured with a fake endpoint.
  - **When** `SendToDeviceAsync(token, request)` runs.
  - **Then** the fake receives an HTTP/2 POST to `/3/device/<token>`, and its JWT verifies against the key's public half. The headers carry the bundle id topic, push type `alert`, priority `10`, and a UUID `apns-id`. The response is `Succeeded` with that `apns-id` as `MessageId`.
- AE2. **Covers R6.**
  - **Given** the fake answers `410 {"reason":"Unregistered","timestamp":…}` for token T.
  - **When** a send targets T.
  - **Then** the response `IsUnregistered()` is true. In a multicast, T counts toward `FailureCount`.
- AE3. **Covers R7.**
  - **Given** the fake answers `400 BadDeviceToken`.
  - **When** a send runs with default options, the response is `Failure` naming `BadDeviceToken`.
  - **When** it runs with the opt-in enabled, the response is `Unregistered`.
- AE4. **Covers R8.**
  - **Given** the fake answers `503` on every attempt.
  - **When** a send runs, the provider retries within its bounded budget, then returns `Failure` naming `503` and `ServiceUnavailable`.
  - **When** the fake answers `503` once and then `200`, the send returns `Succeeded`.
- AE5. **Covers R2.**
  - **Given** the fake rejects the current token, at least 20 minutes old, with `403 ExpiredProviderToken`, and accepts any other token.
  - **When** a send runs.
  - **Then** the provider mints a new token, retries once, and succeeds. Concurrent sends that hit the same rejection trigger exactly one re-mint.

### Scope Boundaries

- Certificate-based (`.p12`) authentication is out. Token auth is Apple's recommended path and needs no per-app certificate rotation.
- The request contract gets no new per-message push type, priority, expiration, badge, or sound. Options set them per instance.

### Deferred to Follow-Up Work

- **Background, live-activity, and other non-alert push types.** Background pushes must omit `alert`, `badge`, and `sound`. Live activities need `content-state` and `event`. `PushNotificationRequest` requires a title and body and has no data-only shape, so either type needs an abstraction change first.
- **Per-message overrides** of priority, expiration, and push type through the request contract.
- **A live APNs sandbox smoke test.** Some behavior can't be scripted against Kestrel: the initial `SETTINGS_MAX_CONCURRENT_STREAMS=1`, GOAWAY frames, and certificate trust.

---

## Planning Contract

### Key Technical Decisions

- KTD1. **Sign the provider token with BCL `ECDsa`, not a JWT library.**
  - Import the key with `ImportFromPem`. Sign with `SignData(…, SHA256)`, which already emits the IEEE P1363 r‖s signature that JWS requires. Encode with `Base64Url`.
  - dotAPNS carries BouncyCastle, and CorePush hand-parses ASN.1. Neither is needed on .NET 10, and a dependency-free signer keeps the package lean (research §1).
- KTD2. **Cache one token per `(team id, key id)` for the whole container.**
  - A singleton token source keeps an immutable token holder per key identity. It refreshes a token once it is 50 minutes old (the cadence apns2, pushy, and CorePush use). It guards minting with a lock plus a generation counter.
  - Two option sets with the same `(team id, key id)` but different private-key text are refused when the second is first used. Signing silently with whichever key loaded first would hide a misconfiguration.
  - Apple rejects token churn under one key with `TooManyProviderTokenUpdates`. Two clients sharing a key, such as a default and a named instance or sandbox and production, refreshing independently is the documented trigger (pushy#931, dotAPNS#39/#128).
  - Sharing across instances in one container removes that failure. Across containers or processes it remains, and the docs name it.
  - Use `TimeProvider` for `iat` and age checks.
- KTD3. **Re-mint once on `403 ExpiredProviderToken`, keyed on the rejected token's generation.**
  - A send that gets `ExpiredProviderToken` asks the source to invalidate the generation it used, then retries once.
  - Only the first caller for a generation re-mints; later callers see a newer token and reuse it. This mirrors node-apn's `regenerate(generation)`.
  - An invalidation-driven re-mint is refused when the current token is under 20 minutes old: the retry then reuses the current token. Apple's once-per-20-minutes rule would otherwise turn a persistent rejection into `TooManyProviderTokenUpdates` on every send. A host clock running more than an hour ahead is one such persistent rejection.
  - Every other 403 (`InvalidProviderToken`, `MissingProviderToken`, `Forbidden`, `UnrelatedKeyIdInToken`, `BadEnvironmentKeyIdInToken`, and the rest) is a configuration error: map it to `Failure` without retry.
- KTD4. **Map only 410 to `Unregistered`. `BadDeviceToken` is a failure unless the host opts in.**
  - Apple returns `BadDeviceToken` both for a malformed token and for a token from the other environment.
  - A host pointed at the wrong environment would otherwise delete every valid token it holds.
  - The issue asked for `BadDeviceToken` to invalidate, so the behavior stays available behind `TreatBadDeviceTokenAsUnregistered` (default `false`).
  - `DeviceTokenNotForTopic` stays a failure: it signals a topic misconfiguration, not a dead token.
- KTD5. **Use bounded HTTP resilience, not Firebase's five-attempt retry.**
  - Register the named client with `AddStandardResilienceHandler`, configured as follows:
    - Retry only transport faults, 500, and 503, at most 2 retries. Every 429 maps straight to `Failure`: `TooManyRequests` throttles one device token, so retrying spends backoff on a token that is still throttled.
    - Honor `Retry-After` if it ever appears.
    - The default predicate retries every 429, so `Retry.ShouldHandle` is overridden to the set above.
  - `CircuitBreaker.ShouldHandle` is narrowed to transport faults, 500, 503, and timeouts. The default also counts 429, and per-token 429s would then open the breaker for the whole instance.
  - The rate limiter gets a queue (`QueueLimit` 10000). The standard 1000-permit limiter has no queue, so concurrent multicasts on one instance would otherwise be rejected.
  - Breaker and limiter rejections reach the service as exceptions and map to `Failure` per R8.
  - Apple says to wait 15 minutes before retrying 5xx and documents no `Retry-After` (research §6). A long in-process retry holds a caller's multicast hostage for no gain.
  - A `configureResilience` delegate lets hosts override the policy, matching the SMS HTTP providers (`src/Headless.Sms.Cequens/Setup.cs`).
  - Rejected alternative: a `FirebaseRetryOptions`-style options class. It duplicates what `HttpStandardResilienceOptions` already exposes.
- KTD6. **Use one long-lived HTTP/2 connection pool per instance.**
  - The primary handler is a `SocketsHttpHandler` configured as follows:
    - `EnableMultipleHttp2Connections = true`
    - `PooledConnectionLifetime` of hours
    - `PooledConnectionIdleTimeout` of at least one hour
    - keepalive pings on an hourly delay
  - Every request is `Version = 2.0` with `HttpVersionPolicy.RequestVersionExact`, because APNs has no HTTP/1.1.
  - The factory handler lifetime is set to infinite. The default 2-minute `IHttpClientFactory` rotation would keep opening fresh connections, and APNs starts each token-auth connection at one stream.
  - The client's `BaseAddress` comes from the environment, and the host's `configureClient` delegate runs after it. That order is also the test seam for pointing at a fake endpoint.
  - After configuration, the provider refuses a non-HTTPS `BaseAddress` unless its host is loopback. Otherwise a misconfigured client would send the bearer JWT and payload in cleartext, while loopback h2c still serves the fake server.
- KTD7. **Fan multicast out with an index-addressed result array.**
  - Validate every token and the request before any send starts (R10), so one blank token cannot cause partial delivery. Only the network send sits inside the per-token exception mapping.
  - Serialize the payload bytes once, then run `Parallel.ForEachAsync` over token indexes with `MaxDegreeOfParallelism = MaxConcurrency` (default 100, range 1–1000).
  - Write each result into its slot, which gives input order without a sort. `Headless.Collections.ParallelForEachAsync` does not preserve order, so it is not used.
  - Bounding concurrency also limits the connection burst a cold client opens while APNs' stream limit is still 1.
- KTD8. **Registration mirrors Firebase and the SMS HTTP providers.**
  - `SetupApnsPushNotifications` and `SetupApnsPushNotificationsNamed` each carry the four `UseApns` overloads: `IConfiguration`, `Action<ApnsOptions>`, `Action<ApnsOptions, IServiceProvider>`, and a pre-built `ApnsOptions`.
  - Each also takes optional `configureClient` and `configureResilience` delegates.
  - Registration follows these rules:
    - A shared `AddApnsCore(services, name, …)` registers named options with the FluentValidation validator.
    - It registers a per-name HTTP client, `Headless:Apns` or `Headless:Apns:{name}`, with `RemoveAllLoggers()`. The factory's default logger writes the request URI, and the URI contains the raw device token.
    - Each sender reads `IOptionsMonitor.Get(name)`, never `CurrentValue`.
    - The token source is registered once with `TryAddSingleton`.
- KTD9. **Write the payload with `Utf8JsonWriter`.**
  - The payload is `aps.alert{title, body}` plus top-level string custom keys. Writing it by hand keeps it AOT-safe and makes the byte length exact for the size check.
  - Reading the error body `{reason, timestamp}` uses a small source-generated `JsonSerializerContext`.
- KTD10. **The client generates `apns-id`.**
  - The provider sends a fresh lowercase UUID as `apns-id` and uses it as `MessageId`.
  - A success then always has a message id, even if a proxy strips the echoed response header.
- KTD11. **`CollapseKey` joins the abstraction, and each provider validates its own limit.**
  - `PushNotificationRequest.CollapseKey` is an optional `init` string. The record's remarks already reserve such options for growth.
  - APNs enforces the 64-byte `apns-collapse-id` limit.
  - Firebase enforces the same limit too, because its APNs bridge forwards the value as that header.
- KTD12. **Fake APNs is an in-process Kestrel server speaking h2c.**
  - A test fixture hosts Kestrel on loopback port 0 with `HttpProtocols.Http2`. It records each request (path, headers, body, decoded JWT) and answers from a per-token script.
  - The client reaches it through `configureClient` (KTD6) with `RequestVersionExact`, which makes h2c prior-knowledge work (aspnetcore#43836). This exercises the real `SocketsHttpHandler` HTTP/2 path. A stub `HttpMessageHandler` would bypass it.
  - node-apn tests the same way. No unit test in the repository binds a Kestrel socket yet, so this fixture is the first. `tests/Headless.Captcha.ReCaptcha.Tests.Unit` already carries the `Microsoft.AspNetCore.App` framework reference it needs.
  - The fake answers `ExpiredProviderToken` by bearer value, not by attempt count: it rejects one specific JWT and accepts any other. Attempt-count scripting would make the concurrent re-mint assertion depend on request order.

### High-Level Technical Design

Send path for one token, including the token-expiry retry (KTD3) and outcome mapping (KTD4, R5–R8):

```mermaid
sequenceDiagram
  participant C as Caller
  participant S as ApnsPushNotificationService
  participant T as ApnsTokenSource (singleton)
  participant H as HttpClient (resilience + SocketsHttpHandler)
  participant A as APNs
  C->>S: SendToDeviceAsync(token, request)
  S->>S: validate + build payload bytes (R10)
  S->>T: GetToken(teamId, keyId)
  T-->>S: jwt, generation g
  S->>H: POST /3/device/token (HTTP/2, headers)
  H->>A: request (retries transport, 500, 503; max 2)
  A-->>H: status + reason
  H-->>S: response
  alt 403 ExpiredProviderToken and first attempt
    S->>T: Invalidate(g)
    T-->>S: new jwt, generation g+1
    S->>H: resend once
  end
  S-->>C: Succeeded(apns-id) / Unregistered / Failed(reason)
```

Outcome mapping owned by R5–R8 and KTD4:

| APNs response | `PushNotificationResponse` |
|---|---|
| 200 | `Succeeded(token, apns-id)` |
| 410 (any reason) | `Unregistered(token)` |
| 400 `BadDeviceToken` | `Failed` (or `Unregistered` with the opt-in) |
| 403 `ExpiredProviderToken` | re-mint and retry once, then map the retry's result |
| any other 4xx, 5xx after retries, transport fault | `Failed(token, "<reason> (HTTP <status>)")` |

Directional sketch of the registration a host writes. The shape is illustrative, not a specification:

```csharp
builder.Services.AddHeadlessPushNotifications(setup =>
{
    setup.UseApns(builder.Configuration.GetSection("Apns"));
    setup.AddNamed("calls", i => i.UseApns(o => { /* same key, PushType = Voip */ }));
});
```

### Output Structure

```text
src/Headless.PushNotifications.Apns/
  Headless.PushNotifications.Apns.csproj
  README.md
  Setup.cs
  ApnsOptions.cs                 (ApnsOptions, ApnsEnvironment, ApnsPushType, ApnsPriority, validator)
  ApnsPushNotificationService.cs
  Internals/
    ApnsTokenSource.cs           (container-scoped ES256 token cache)
    ApnsPayloadWriter.cs
    ApnsResponseMapper.cs
    ApnsJsonSerializerContext.cs
    ApnsLoggerExtensions.cs
tests/Headless.PushNotifications.Tests.Harness/
  PushNotificationServiceConformanceTests.cs
  PushNotificationRequests.cs
tests/Headless.PushNotifications.Apns.Tests.Unit/
  FakeApnsServer.cs
  ApnsConformanceTests.cs
  ApnsPushNotificationServiceTests.cs
  ApnsTokenSourceTests.cs
  ApnsOptionsValidatorTests.cs
  ApnsSetupTests.cs
```

### Assumptions

- The implementer can reach hex tokens and a throwaway P-256 key in tests. Tests generate the key with `ECDsa.Create(ECCurve.NamedCurves.nistP256)` and export it as PKCS#8 PEM, so no real Apple credential is involved.
- `FirebaseAdmin` 3.6.0 exposes `AndroidConfig.CollapseKey` and `ApnsConfig.Headers`. U1 confirms this against the installed package before relying on it.
- The multicast concurrency default of 100 is a conservative starting point. Apple publishes no per-connection guarantee, and hosts can raise it.

### Sources & Research

- Apple: [token connection](https://developer.apple.com/documentation/usernotifications/establishing-a-token-based-connection-to-apns), [sending requests](https://developer.apple.com/documentation/usernotifications/sending-notification-requests-to-apns), [responses](https://developer.apple.com/documentation/usernotifications/handling-notification-responses-from-apns), [payload](https://developer.apple.com/documentation/usernotifications/generating-a-remote-notification).
- Token caching and re-mint races:
  - [apns2 token.go](https://github.com/sideshow/apns2/blob/65966ee917060789a2a4ccf5d695c424d4b59e29/token/token.go)
  - [node-apn prepare.js generation guard](https://github.com/parse-community/node-apn/blob/c2e1ea3c04d38c86ab339e08e0e08022204dad1a/lib/credentials/token/prepare.js)
  - [pushy#931](https://github.com/jchambers/pushy/issues/931)
  - [dotAPNS#39](https://github.com/alexalok/dotAPNS/issues/39)
- `BadDeviceToken` environment mismatch: [dotAPNS#84](https://github.com/alexalok/dotAPNS/issues/84), plus the Apple responses page.
- The .NET HTTP/2 pool remembers the lowest `MAX_CONCURRENT_STREAMS` it has seen ([Http2Connection.cs](https://github.com/dotnet/runtime/blob/a28a1f81e3e712071729416783076dc21b2da40d/src/libraries/System.Net.Http/src/System/Net/Http/SocketsHttpHandler/Http2Connection.cs)). This comes from the research agent's read and is not independently verified. No decision depends on it alone.
- h2c prior knowledge:
  - Kestrel allows `HttpProtocols.Http2` without TLS only for prior-knowledge clients ([Kestrel endpoints](https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints)).
  - `SocketsHttpHandler` attempts h2c only when `Version.Major == 2` and the policy is not `RequestVersionOrLower` (dotnet/runtime `HttpConnectionPoolManager.cs`).
- Standard resilience handler behavior: the circuit breaker's `IsTransient` predicate and a retry that re-sends the original message keep `VersionPolicy` (dotnet/extensions v10.10.0 `HttpClientResiliencePredicates.cs`, `ResilienceHandler.cs`).
- Repository patterns:
  - `src/Headless.PushNotifications.Firebase/Setup.cs` for named and keyed registration
  - `src/Headless.Sms.Cequens/Setup.cs` and `CequensSmsSender.cs` for the HTTP client, resilience override, and immutable token holder
  - `tests/Headless.Sms.Tests.Harness` and `tests/Headless.Captcha.Tests.Harness` for conformance harness shape
  - `docs/solutions/best-practices/tests-harness-extraction.md`
  - `docs/solutions/conventions/provider-setup-and-options.md`

---

## Implementation Units

### U1. Collapse key on the request contract

- **Goal:** `PushNotificationRequest` carries an optional collapse key that Firebase honors.
- **Requirements:** R14; KTD11.
- **Dependencies:** none.
- **Files:**
  - `src/Headless.PushNotifications.Abstractions/Contracts/PushNotificationRequest.cs`
  - `src/Headless.PushNotifications.Firebase/FcmPushNotificationService.cs`
  - `src/Headless.PushNotifications.Firebase/Internals/FcmMessageSender.cs`
  - `src/Headless.PushNotifications.Firebase/Internals/IFcmMessageSender.cs` (where `FcmMessageContent` is declared)
  - `tests/Headless.PushNotifications.Firebase.Tests.Unit/FcmPushNotificationServiceTests.cs`
- **Approach:**
  1. Add `CollapseKey` as an optional `init` property with XML docs stating each provider's limit.
  2. Carry it through `FcmMessageContent`.
  3. Set `AndroidConfig.CollapseKey` and the `apns-collapse-id` entry of `ApnsConfig.Headers` in both message builders when the key is non-null.
  4. Validate the 64-byte UTF-8 limit in `_ValidateContent`.
- **Patterns to follow:** the existing `_ValidateContent` and `BuildMessage` shapes.
- **Test scenarios:**
  - A request with `CollapseKey = "order-42"` builds a single message whose Android collapse key and APNs `apns-collapse-id` header are both `order-42`.
  - The same holds for the multicast message.
  - A request without a collapse key leaves both unset.
  - A 65-byte collapse key, for example 33 two-byte characters, throws `ArgumentException` before the sender is called.
- **Verification:** The Firebase unit tests pass, and no Firebase behavior changes for requests without a collapse key.

### U2. Push-notification conformance harness, adopted by Firebase

- **Goal:** One portable contract suite that every real `IPushNotificationService` provider runs.
- **Requirements:** R15, R9, R10 (portable subset).
- **Dependencies:** U1.
- **Files:**
  - `tests/Headless.PushNotifications.Tests.Harness/Headless.PushNotifications.Tests.Harness.csproj`
  - `tests/Headless.PushNotifications.Tests.Harness/PushNotificationServiceConformanceTests.cs`
  - `tests/Headless.PushNotifications.Tests.Harness/PushNotificationRequests.cs`
  - `tests/Headless.PushNotifications.Firebase.Tests.Unit/FcmConformanceTests.cs`
  - `tests/Headless.PushNotifications.Firebase.Tests.Unit/Headless.PushNotifications.Firebase.Tests.Unit.csproj`
  - `headless-framework.slnx`
- **Approach:**
  1. Model the suite on `SmsSenderConformanceTests`: an abstract class with virtual scenarios that concrete classes re-expose as `[Fact]` overrides.
  2. Give it two seams:
     - a service whose backend accepts every token
     - a service whose backend reports one named token as unregistered and accepts the rest
  3. Use `Headless.NET.Sdk.Test` with `IsTestProject=false` and `IsTestHarnessProject=true`, like `Headless.Sms.Tests.Harness`.
  4. Firebase wires the seams with an NSubstitute `IFcmMessageSender`.
  5. Dev/Noop is excluded: it never validates or fails by design (`NoopPushNotificationService` remarks).
- **Test scenarios (the contract):**
  - A null, empty, or whitespace client identifier throws `ArgumentException`.
  - A null request throws `ArgumentNullException`.
  - A blank title throws `ArgumentException`. So does a blank body.
  - An empty multicast list throws `ArgumentException`.
  - A single send to an accepting backend returns `Succeeded`. Its `ClientIdentifier` equals the input, and `MessageId` is non-empty.
  - A multicast of three tokens to an accepting backend returns three responses in input order, with `SuccessCount` 3 and `FailureCount` 0.
  - A multicast where the middle token is unregistered returns that response as `IsUnregistered()`, keeps input order, and reports `SuccessCount` 2 and `FailureCount` 1.
  - A pre-cancelled token makes a single send throw `OperationCanceledException`. The seam obligation is that each provider's backend double honors the token it receives. Firebase's substitute sender must throw when the token is cancelled, because `FcmPushNotificationService` passes the token through without inspecting it.
- **Excluded from the portable contract:** transport-fault behavior. Firebase's `FcmMessageSender.SendAsync` lets a non-Firebase `HttpRequestException` propagate. The interface documents whole-call transport failure as implementation-specific. APNs covers its own transport-fault mapping in U4. The harness therefore needs only the accepting seam and the one-unregistered-token seam.
- **Verification:** The Firebase conformance class passes every scenario it adopts. The harness project builds and is attached to the slnx.

### U3. APNs options and provider-token source

- **Goal:** Validated options and a container-scoped, race-safe ES256 token source.
- **Requirements:** R2, R11, R12; KTD1, KTD2, KTD3.
- **Dependencies:** none.
- **Files:**
  - `src/Headless.PushNotifications.Apns/Headless.PushNotifications.Apns.csproj`
  - `src/Headless.PushNotifications.Apns/ApnsOptions.cs`
  - `src/Headless.PushNotifications.Apns/Internals/ApnsTokenSource.cs`
  - `tests/Headless.PushNotifications.Apns.Tests.Unit/Headless.PushNotifications.Apns.Tests.Unit.csproj`
  - `tests/Headless.PushNotifications.Apns.Tests.Unit/ApnsOptionsValidatorTests.cs`
  - `tests/Headless.PushNotifications.Apns.Tests.Unit/ApnsTokenSourceTests.cs`
  - `headless-framework.slnx`
- **Approach:**
  1. Define `ApnsOptions` with these members:
     - `KeyId`
     - `TeamId`
     - `PrivateKey` (the `.p8` PEM text; `[JsonIgnore]`, redacted `ToString`, like `FirebaseOptions`)
     - `BundleId`
     - `Environment` (`Production` default, `Sandbox`)
     - `PushType` (`Alert` default, `Voip`)
     - `Priority` (`Immediate`=10 default, `PowerConsiderate`=5, `PowerPrioritized`=1)
     - `TreatBadDeviceTokenAsUnregistered` (false)
     - `MaxConcurrency` (100)
  2. Add the validator in the same file:
     - `KeyId` and `TeamId` are 10 characters each
     - `BundleId` is non-empty
     - `MaxConcurrency` is 1–1000
     - `PrivateKey` must import as a P-256 EC key. Its failure message never includes `{PropertyValue}`, so the key cannot leak into an `OptionsValidationException`.
  3. Build `ApnsTokenSource` as a singleton:
     - It holds one `ECDsa` and an immutable `(jwt, generation, mintedAt)` holder per `(teamId, keyId)`.
     - A fast path runs without a lock; minting happens under a per-key `SemaphoreSlim`.
     - It refreshes at 50 minutes of age.
     - `Invalidate(generation)` re-mints only when the generation is still current.
     - It is disposed with the container.
- **Patterns to follow:**
  - `FirebaseOptions` and `FirebaseOptionsValidator` for redaction and the validator.
  - `CequensSmsSender`'s `CachedToken` holder for torn-read safety.
  - `docs/solutions/conventions/provider-setup-and-options.md`.
- **Test scenarios:**
  - A minted token splits into three Base64Url segments with no padding. The header decodes to `{"alg":"ES256","kid":<keyId>}` and the claims to `{"iss":<teamId>,"iat":<now seconds>}`. The signature verifies with the test key's public half as P1363.
  - Two calls 49 minutes apart return the same token. At 50 minutes, a fresh token is returned with a newer `iat`. `FakeTimeProvider` drives the clock.
  - 50 concurrent `GetToken` calls on a cold cache mint exactly once.
  - `Invalidate(g)` with the current generation re-mints when the token is at least 20 minutes old. A second `Invalidate(g)` with the now-stale generation does not.
  - `Invalidate(g)` on a token minted 5 minutes ago keeps the current token.
  - Two option sets sharing team id, key id, and key text receive the same token.
  - Two option sets sharing team id and key id but with different key text are refused with an `InvalidOperationException` naming the key id and not the key.
  - The validator rejects each of these:
    - a missing key id
    - a 9-character team id
    - an empty bundle id
    - a PEM that is RSA rather than EC
    - non-PEM garbage
    - `MaxConcurrency` of 0
  - `ToString()` on `ApnsOptions` does not contain the private key text. Neither does the validator's failure message for a malformed key.
- **Verification:** The unit tests pass, and no JWT or crypto package is referenced.

### U4. APNs send path, multicast, outcome mapping, and registration

- **Goal:** A working `IPushNotificationService` for APNs, registered through `UseApns`, proven against a fake HTTP/2 APNs server and the conformance suite.
- **Requirements:** R1–R13, R15; KTD4–KTD10, KTD12. Covers AE1–AE5.
- **Dependencies:** U2, U3.
- **Files:**
  - `src/Headless.PushNotifications.Apns/ApnsPushNotificationService.cs`
  - `src/Headless.PushNotifications.Apns/Setup.cs`
  - `src/Headless.PushNotifications.Apns/Internals/ApnsPayloadWriter.cs`
  - `src/Headless.PushNotifications.Apns/Internals/ApnsResponseMapper.cs`
  - `src/Headless.PushNotifications.Apns/Internals/ApnsJsonSerializerContext.cs`
  - `src/Headless.PushNotifications.Apns/Internals/ApnsLoggerExtensions.cs`
  - `tests/Headless.PushNotifications.Apns.Tests.Unit/FakeApnsServer.cs`
  - `tests/Headless.PushNotifications.Apns.Tests.Unit/ApnsConformanceTests.cs`
  - `tests/Headless.PushNotifications.Apns.Tests.Unit/ApnsPushNotificationServiceTests.cs`
  - `tests/Headless.PushNotifications.Apns.Tests.Unit/ApnsSetupTests.cs`
- **Approach:**
  1. Validate and write the payload once per call (R10, KTD9).
  2. Send one request per token:
     - Set the headers from options and derive the topic from the push type: the bundle id for alert, the bundle id plus `.voip` for VoIP.
     - VoIP raises the payload limit to 5120 bytes.
     - Handle token expiry per KTD3 and map outcomes per KTD4.
     - Log failures with a masked token.
     - Catch every exception per token and map it to `Failed` (R8). The one exception is an `OperationCanceledException` raised while the caller's token is cancelled, which propagates (R9). This mirrors `CequensSmsSender._SendAsync`.
  3. Multicast follows KTD7.
  4. `Setup.cs` follows KTD8:
     - It sets `BaseAddress` from the environment before the host's `configureClient` runs (KTD6).
     - It adds `TryAddSingleton(TimeProvider.System)`.
     - Its csproj carries `InternalsVisibleTo` for the unit test project and `DynamicProxyGenAssembly2`.
  5. The test project needs `FrameworkReference Microsoft.AspNetCore.App` for Kestrel, like `tests/Headless.Captcha.ReCaptcha.Tests.Unit`.
- **Execution note:** Stand up `FakeApnsServer` and the AE1 happy-path test first. It proves h2c prior knowledge and header plumbing before the mapping branches are built on top.
- **Patterns to follow:**
  - `src/Headless.PushNotifications.Firebase/Setup.cs` for the default and named factory pair.
  - `src/Headless.Sms.Cequens/Setup.cs` for `AddHttpClient`, `AddStandardResilienceHandler`, and the `configureResilience` hook.
  - `FcmPushNotificationService` for aggregate counts.
- **Test scenarios:**
  - Covers AE1. The fake receives these request properties: HTTP/2, POST, and path `/3/device/<token>`. It receives these headers: `authorization: bearer <jwt verifying with public key>`, `apns-topic: <bundle>`, `apns-push-type: alert`, `apns-priority: 10`, and `apns-id` as a lowercase UUID. The response is `Succeeded` with `MessageId` equal to that `apns-id`.
  - The payload body parses as `{"aps":{"alert":{"title":…,"body":…}},"k1":"v1"}` for `Data = {k1: v1}`.
  - `CollapseKey = "c1"` sends `apns-collapse-id: c1`. No collapse key omits the header.
  - `PushType = Voip` sends topic `<bundle>.voip` and push type `voip`, and accepts a 5000-byte payload that alert would reject.
  - `Priority = PowerConsiderate` sends `apns-priority: 5`.
  - `Environment = Sandbox` with no `configureClient` targets `https://api.sandbox.push.apple.com`. This is asserted on the configured `HttpClient.BaseAddress`, without network.
  - Covers AE2. A 410 `Unregistered` returns `Unregistered`. So does a 410 `ExpiredToken`, and a 410 with an unknown reason.
  - Covers AE3. A 400 `BadDeviceToken` returns `Failed` containing `BadDeviceToken`. With the opt-in, it returns `Unregistered`.
  - A 400 `DeviceTokenNotForTopic` returns `Failed` even with the opt-in.
  - Covers AE4. Three 503 responses (initial plus 2 retries) return `Failed` naming `ServiceUnavailable` and `503`. One 503 then 200 returns `Succeeded`. Tests set the retry delay to zero through `configureResilience`.
  - A 429 `TooManyProviderTokenUpdates` returns `Failed` after exactly one request (no retry).
  - Covers AE5. The fake rejects the first minted JWT with 403 `ExpiredProviderToken` and accepts any other. A send returns `Succeeded`, and its two requests carried different JWTs. `FakeTimeProvider` has advanced at least 20 minutes past the first mint.
  - Ten concurrent sends start with that rejected JWT, with the clock at least 20 and under 50 minutes past the first mint so the rejection, not the scheduled refresh, drives the re-mint. The fake observes exactly two distinct JWTs, the rejected one and one replacement, and all ten succeed.
  - A fake that rejects every JWT returns `Failed` after exactly two requests.
  - An outage: the fake answers 503 to every request, 250 tokens, `MaxConcurrency = 100`. The multicast returns 250 `Failed` responses in input order and does not throw, even after the circuit breaker opens.
  - A 403 `InvalidProviderToken` returns `Failed` after one request.
  - Unparseable or empty error bodies map to `Failed` naming the HTTP status.
  - A multicast of 250 tokens with `MaxConcurrency = 10` returns 250 responses in input order. The fake observes at most 10 in-flight requests.
  - A mixed multicast (success, 410, 400 `BadDeviceToken`, 500 exhausted) keeps input order and counts `SuccessCount` 1 and `FailureCount` 3.
  - After a successful send and a failed send, no captured log entry contains the raw device token.
  - 150 sends answered with 429 `TooManyRequests` each return `Failed` after one request, and a later send to another token still returns `Succeeded`.
  - Two concurrent multicasts of 1000 tokens each at `MaxConcurrency = 1000` return all `Succeeded`.
  - A multicast of three tokens whose middle token is blank throws `ArgumentException`, and the fake receives no request.
  - A `configureClient` that sets `http://apns.example.com` makes the first send fail with an error naming the non-HTTPS endpoint, and nothing is sent. A loopback `http://127.0.0.1:<port>` is accepted.
  - Validation: each R10 input throws `ArgumentException` with no request reaching the fake, including a `Data` key `aps`, a 65-byte collapse key, and a 4097-byte alert payload.
  - Cancellation mid-multicast throws `OperationCanceledException`.
  - An unreachable endpoint (closed loopback port) returns `Failed` per token.
  - Conformance: `ApnsConformanceTests` passes every harness scenario against the fake server.
  - Setup:
    - Default `UseApns(IConfiguration)` resolves an unkeyed `IPushNotificationService`.
    - `AddNamed("a", …)` and `AddNamed("b", …)` resolve keyed services bound to their own options: two different bundle ids reach the fake as two different topics.
    - Missing options fail `ValidateOnStart` on host start.
    - The default and a named instance with the same key share one token (asserted through the singleton source).
- **Verification:** All tests in `tests/Headless.PushNotifications.Apns.Tests.Unit` pass, including AE1–AE5, and the project builds clean in Release with analyzers.

### U5. Composition wiring and cross-provider mixing

- **Goal:** The Core setup surface and composition tests know about `UseApns`.
- **Requirements:** R13.
- **Dependencies:** U4.
- **Files:**
  - `src/Headless.PushNotifications.Core/HeadlessPushNotificationsSetupBuilder.cs`
  - `src/Headless.PushNotifications.Core/KeyedServicePushNotificationServiceProvider.cs`
  - `tests/Headless.PushNotifications.Composition.Tests.Unit/Headless.PushNotifications.Composition.Tests.Unit.csproj`
  - `tests/Headless.PushNotifications.Composition.Tests.Unit/CrossProviderPushNotificationsMixingTests.cs`
  - `tests/Headless.PushNotifications.Composition.Tests.Unit/PushNotificationsSetupBuilderTests.cs`
- **Approach:**
  1. Extend the zero-provider error message and the unregistered-name hint to list `UseApns` beside `UseFirebase` and `UseNoop`.
  2. Add an APNs instance to the cross-provider mixing test: a default Firebase, a named APNs, and a named Noop each resolve to their own implementation.
- **Test scenarios:**
  - A named instance with zero providers throws a message containing `UseApns`.
  - A mixed registration resolves `ApnsPushNotificationService` under its name, and the other services are unaffected.
  - Registering both `UseApns` and `UseFirebase` as default throws the existing multiple-default error.
- **Verification:** The composition tests pass.

### U6. Documentation and package landing page

- **Goal:** Consumers can pick APNs from the domain guide and install it from NuGet.
- **Requirements:** R16, R14.
- **Dependencies:** U1, U4.
- **Files:**
  - `docs/llms/push-notifications.md`
  - `src/Headless.PushNotifications.Apns/README.md`
  - `README.md`
  - `docs/llms/index.md` (only if it enumerates push packages)
- **Approach:** Follow `docs/authoring/AUTHORING.md`. Make these changes in the domain guide:
  1. Add `PushNotifications.Apns` to the frontmatter `packages`.
  2. Add an APNs orientation example and Agent Rules for these points:
     - 410-only unregistration
     - the `BadDeviceToken` opt-in and why it is off
     - the shared-key `TooManyProviderTokenUpdates` hazard across processes
     - payload limits
     - the reserved `aps` key
     - collapse key limits
     - load `PrivateKey` from a secret store or environment, never from committed configuration; the appsettings sample uses a placeholder
     - rotating the key requires a restart, because the token source caches the key per `(team id, key id)`
  3. Add a third column to "Choosing a Provider".
  4. Rewrite the Response Status Model wording from FCM-specific to provider-neutral.
  5. Add a `## Headless.PushNotifications.Apns` section with the same sub-headings as Firebase: API and behavior, Design constraints, Install, Setup and use, Configuration (appsettings sample with a placeholder key), and Runtime behavior (status table, retry policy).
  6. Document `CollapseKey` under Abstractions and Firebase.
  7. Keep the package README a small landing page. Add the root README's provider-table and package-list rows.
- **Test expectation:** none -- documentation only.
- **Verification:**
  - The domain guide reads consistently with the implemented option names and defaults.
  - No setup or API reference is copied into the package README.
  - The root README lists the package.

---

## Verification Contract

| Scope | Command | Proves |
|---|---|---|
| Abstractions + Firebase (U1, U2) | `make test-project TEST_PROJECT=tests/Headless.PushNotifications.Firebase.Tests.Unit/Headless.PushNotifications.Firebase.Tests.Unit.csproj` | Collapse key mapping; Firebase passes the harness |
| APNs (U3, U4) | `make test-project TEST_PROJECT=tests/Headless.PushNotifications.Apns.Tests.Unit/Headless.PushNotifications.Apns.Tests.Unit.csproj` | AE1–AE5, token source, validator, setup, conformance |
| Composition (U5) | `make test-project TEST_PROJECT=tests/Headless.PushNotifications.Composition.Tests.Unit/Headless.PushNotifications.Composition.Tests.Unit.csproj` | Registration surface across providers |
| Abstractions and Dev regression | `make test-project` for `Headless.PushNotifications.Abstractions.Tests.Unit` and `Headless.PushNotifications.Dev.Tests.Unit` | No regression from the request-contract change |
| Analyzer build | `make build-project PROJECT=src/Headless.PushNotifications.Apns/Headless.PushNotifications.Apns.csproj`, and `dotnet build -c Release -v:minimal` on each changed test project | A green MTP run is not a clean build: analyzer errors surface only in Release builds |
| Format | `make format-check` | CSharpier clean |
| Pre-PR | `make quality-analyzers` (narrow with `QUALITY_SEVERITY=warn` if noisy) | No new analyzer findings in the changed projects |

There is no integration suite. The fake Kestrel APNs server in the unit project is the highest credible seam without Apple credentials.

## Definition of Done

- Every R1–R16 is implemented and traced to a passing test or a documentation change. AE1–AE5 each have a named test.
- `Headless.PushNotifications.Apns` and both new test projects are in `headless-framework.slnx`. Each new `.cs` file starts with the copyright header, uses `Headless.Checks` for argument validation, and pins no package version in a `.csproj`.
- The new package uses a `Headless.NET.Sdk` project SDK and takes no JWT or crypto third-party dependency.
- The verification table passes.
- Abandoned attempts, debugging scaffolding, and unused helpers are removed from the diff.
