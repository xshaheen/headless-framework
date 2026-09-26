---
title: APNs Cross-Library Conformance, Production Hardening, and Broadcast - Plan
type: feat
date: 2026-09-26
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: session-direction
execution: code
---

# APNs Cross-Library Conformance, Production Hardening, and Broadcast - Plan

## Goal Capsule

- **Objective:** Close the gaps that a survey of the popular APNs libraries found in our APNs provider. The survey covered node-apn and apns2 (npm), sideshow/apns2 (Go), a2 (Rust), aioapns and PyAPNs2 (Python), apnotic (Ruby), pushy (Java), and dotAPNS and CorePush (.NET), and checked them against Apple's documentation.
- **Verification without Apple credentials:** check the wire contract (headers and payload) by running three widely used libraries on the same scenarios and diffing their requests against ours.
- **Means:** three pull requests, stacked on #984, which is stacked on #974:
  - **C, payload conformance:** a cross-library oracle harness, custom data as JSON values, a raw-payload notification, a caller-supplied `apns-id`, and Live Activity `input-push-token`.
  - **D, operations:** failure classification with retry guidance, distinct auth-error handling, metrics and tracing, alternative port and proxy options, bounded connections, a test that simulates APNs' one-stream start on a new connection, and client-token guidance for apps.
  - **E, broadcast:** iOS 18 channel management, broadcast sends, and Live Activity `input-push-channel`.
- **Fixes that belong to #974 go to #974.** Two fixes change code #974 introduced: stop retrying 5xx in-process, and stop the redundant resend recorded in #972. They land on #974's branch, which is then merged into #984's branch. It is merged, not rebased: #984 is open under review.
- **Authority:** Apple's documentation wins on wire behavior. Where the reference libraries disagree with Apple, Apple wins, and the harness records the divergence with its reason. `CLAUDE.md` and `docs/solutions/conventions/provider-setup-and-options.md` bind every unit.
- **Stop conditions:**
  - A reference library shows our header or payload for a scenario differs from both Apple's documentation and every reference, and the fix would change a public type beyond this plan.
  - Apple's broadcast documentation contradicts the node-apn implementation on a point the harness cannot settle.

## Key Decisions

- **The stack shape was delegated to the lead by the user.** The work is split into three reviewable PRs rather than one large PR. Payload correctness comes first, so the oracle scenarios exist before later PRs add behavior. Rejected: one PR with everything, because it would be too large to review, and broadcast is a separate API surface.
- **Custom keys never go inside `aps`.** Apple's "Generating a remote notification" says: "Don't add your own custom keys to the aps dictionary; APNs ignores custom keys. Instead, add your custom keys as peers of the aps dictionary." dotAPNS and node-apn allow custom keys inside `aps` anyway. We do not follow them. Rejected: an `aps` extension bag.
- **Custom data becomes JSON on the typed APNs API only.** Apple allows "dictionary, array, string, number, or Boolean" values. Typed notifications take `Data` as a `JsonObject`. The shared `PushNotificationRequest.Data` stays a string map, because FCM data messages are string maps; the shared path converts it. Rejected: JSON values on the shared request, because FCM cannot carry them.
- **A raw notification is the escape hatch for Apple keys we do not model.** It is a JSON object plus a push type, and it keeps every header, size, topic, and auth-mode rule of the typed path.
- **5xx is not retried in-process.** This follows Apple's rule: "After 15 minutes, you can retry JSON payloads that receive response status codes that begin with 5XX." The circuit breaker still counts 5xx. Failure classification (D) tells the caller to retry later.
- **The oracles are run, not transcribed.** Three references were picked for adoption and maturity: `@parse/node-apn` (JS, 354k weekly downloads), `pushy` (Java, the most mature), and `sideshow/apns2` (Go, 3.2k stars).
  - Each runs on one shared `scenarios.json`. Its generated requests are committed as fixtures, and a .NET test compares ours against them semantically.
  - The generators are pinned and run on demand with `make apns-oracles`; CI runs only the .NET comparison. Go runs in a pinned `golang` Docker image because Go is not installed on the development machine.
  - Rejected: extracting expected strings from the libraries' own tests. That gives lower coverage and more room for transcription error.

## Pull Request C: payload conformance

### C1. Cross-library oracle harness
- **Files:**
  - `eng/apns-oracles/scenarios.json`: one entry per scenario. Each entry has an id, the push type, the header inputs, and a library-neutral payload description.
  - `eng/apns-oracles/node/`: `package.json`, `package-lock.json`, `generate.mjs`, with pinned `@parse/node-apn`.
  - `eng/apns-oracles/java/`: `pom.xml` with pinned pushy, and a `Generate` class.
  - `eng/apns-oracles/go/`: `go.mod`, `go.sum`, `main.go`, and a `run.sh` that uses a pinned `golang` image digest.
  - `eng/apns-oracles/README.md`.
  - A `Makefile` target `apns-oracles`.
  - The output fixtures: `tests/Headless.PushNotifications.Apns.Tests.Unit/CrossLibrary/Fixtures/{node-apn,pushy,apns2}.json`.
  - `CrossLibrary/divergences.json`: each accepted difference, with library, scenario, field, and reason.
  - `ApnsCrossLibraryConformanceTests.cs`.
- **Approach:**
  - Every generator writes `{scenario, supported, headers:{apns-push-type, apns-topic, apns-priority, apns-expiration, apns-collapse-id, apns-id?}, payload}`.
  - The .NET test maps each scenario to our typed API and runs `ApnsPayloadWriter.Prepare`.
  - The comparison parses the JSON (key order ignored, numbers compared by value) and compares the headers.
  - Any mismatch not listed in `divergences.json` fails the test, and so does a listed divergence that no longer occurs, so the allowlist cannot go stale.
  - Scenarios a library cannot express are marked unsupported and skipped for that library only.
  - JWT: each generator emits its decoded provider-token header and claims for a fixed test key and clock. The test compares that shape (alg, kid, iss, iat), not the signature, because ES256 is non-deterministic.
- **Scenarios** (at minimum):
  - basic alert
  - full alert: subtitle, localized keys and args, thread id, category, mutable content, interruption level, relevance score, target content id, badge 0, named sound, critical sound
  - background
  - VoIP
  - Live Activity start, with attributes and alert sound
  - Live Activity update, with stale date and relevance
  - Live Activity end, with dismissal date
  - location, push-to-talk, widgets, controls, complication, file provider
  - nested custom data (object, array, number, boolean)
  - expiration 0, and an absolute expiration
  - collapse id
  - priority 5 and priority 10
  - raw payload
  - caller-supplied `apns-id`
- **Supply chain:** exact versions released more than 7 days ago; npm with `ignore-scripts`; committed lockfiles; the Go image pinned by digest.
- **Tests:** the conformance test passes, with every divergence justified from Apple's docs.

### C2. Custom data as JSON
- **Files:** `Notifications/ApnsNotification.cs`, `Internals/ApnsPayloadWriter.cs`, the shared-request conversion in `ApnsPushNotificationService.cs`, `Internals/ApnsVoipDataNotification.cs`, tests, docs.
- **Approach:**
  - Typed notifications carry `JsonObject? Data`, written as peers of `aps`. The `aps` key is still refused.
  - The shared request's string map converts to string values.
  - The payload size is measured on the written bytes, as today.

### C3. Raw notification
- **Files:** a new sealed `ApnsRawNotification` and a public `ApnsNotificationType` enum (one value per supported push type). Writer and header branches, and the certificate-mode refusal list, are keyed by type.
- **Approach:**
  - `required ApnsNotificationType Type`, `required JsonElement Payload` (a JSON object), and optional `Priority`.
  - The common `Expiration`, `CollapseId`, and `ApnsId` apply.
  - The topic suffix, size limit, VoIP-instance rule, and certificate-mode refusal follow `Type`.
  - The payload is written verbatim.

### C4. Caller-supplied `apns-id`
- **Approach:**
  - `ApnsNotification.ApnsId` is a `Guid?`. When set, it is sent as the `apns-id` and returned on the result; the same id is kept on the expired-token resend.
  - Multicast refuses a set `ApnsId` with an `ArgumentException`, because every request needs its own id.

### C5. Live Activity `input-push-token`
- **Approach:** `ApnsLiveActivityNotification.RequestPushToken` (bool) writes `"input-push-token": 1` on a `start` event only; it is refused on other events. The key name and value are checked against Apple and node-apn.

### C6. Docs
- Custom data, the raw notification, `ApnsId`, `RequestPushToken`, the oracle harness (in the contributing notes of `eng/apns-oracles/README.md`), and the 5xx rule.

## Pull Request D: operations

### D1. Failure classification
- `ApnsSendResult` gains two properties:
  - `ApnsFailureKind? FailureKind`: `DeviceTokenInvalid`, `Throttled`, `ServerError`, `Authentication`, `Configuration`, `Payload`, or `Transport`.
  - `TimeSpan? RetryAfter`: 15 minutes for 5xx per Apple. For 429, the `Retry-After` header if APNs sends one. Apple's response-header table does not list it, so the value is otherwise null.
- It also gains `IsRetryable`.
- Every reason in Apple's response table maps to a kind. An unknown reason maps by status class.
- `InvalidProviderToken`, `MissingProviderToken`, and `UnrelatedKeyIdInToken` log a distinct configuration error event. `TooManyProviderTokenUpdates` logs a distinct error event naming the 20-minute rule.
- The shared `PushNotificationResponse` is unchanged.

### D2. Metrics and tracing
- A `Meter` and an `ActivitySource` named after the package, following any existing Headless telemetry convention (search the repository first).
- Instruments:
  - a counter of sends, tagged with outcome, failure kind, reason, push type, and environment
  - a send-duration histogram
  - a counter of provider tokens minted
  - a counter of certificate reloads
- One activity per device send. The device token is never a tag.

### D3. Alternative port and proxy
- `ApnsOptions.UseAlternativePort` selects port 2197.
- `ApnsOptions.Proxy` is an `IWebProxy?` marked `[JsonIgnore]`. It is applied to the primary handler.

### D4. Connection behavior
- The fake server gains a mode that serves one stream per connection.
- A test measures how many connections a cold multicast opens under `MaxConcurrency` 100.
- `SocketsHttpHandler.MaxConnectionsPerServer` cannot provide the bound. The runtime enforces it only on HTTP/1.1 (`HttpConnectionPool.cs:82` maps it to `_maxHttp11Connections`), while the HTTP/2 injection gate (`HttpConnectionPool.Http2.cs:149`) checks only `EnableMultipleHttp2Connections`.
- The provider enforces its own bound through `ApnsOptions.MaxConnections`:
  - A `ConnectCallback` takes a permit from a per-instance `SemaphoreSlim` before it dials.
  - It returns a stream wrapper that releases the permit when the connection's stream is disposed.
  - Requests wait for a free stream on an open connection or for a permit.
- Choose the default from the measured cold-start behavior and pushy's sizing advice. Record the evidence in the XML docs.
- A test proves the bound: with the one-stream server and `MaxConcurrency` 100, the number of open connections never exceeds `MaxConnections`, and every send completes.
- A GOAWAY test checks that requests the server did not process are sent on a new connection and none are duplicated.

### D5. Client-token guidance
- A docs section for apps (Flutter and native iOS):
  - Store the environment with each token, because debug builds register sandbox tokens.
  - Normalize tokens to lowercase before storing and deduplicating: `firebase_messaging` returns uppercase hex, `flutter_apns` lowercase.
  - Do not send an FCM token to APNs.

## Pull Request E: broadcast

### E1. Channel management
- `IApnsBroadcastChannelService`, registered by `UseApns`, with four operations:
  - `CreateAsync(ApnsChannelStoragePolicy)` returns a channel id.
  - `GetAsync(channelId)`
  - `ListAsync()`
  - `DeleteAsync(channelId)`
- It calls `api-manage-broadcast[.sandbox].push.apple.com` on port 2196 in production or 2195 in the sandbox, with paths `/1/apps/<bundle>/channels` and `/1/apps/<bundle>/all-channels`. It sends the `apns-channel-id` and `apns-request-id` headers and supports token or certificate auth.
- Endpoints, bodies, and status mapping are checked against Apple's "Sending channel management requests to APNs" and node-apn.

### E2. Broadcast send
- `SendBroadcastAsync(channelId, ApnsLiveActivityNotification)` posts to `/4/broadcasts/apps/<bundle>` with `apns-channel-id`, `apns-push-type`, `apns-priority`, and `apns-expiration`.
- The payload limit is 5 KB.
- Apple says, "You can't use broadcast push notifications to start a Live Activity." So a `Start` event, or any `Attributes` or `AttributesType`, is refused with `ArgumentException` before any request. Channels take `Update` and `End` only.
- `apns-expiration` is a required header on broadcast requests. A null `Expiration` is sent as `0` (deliver once), following the push-to-talk precedent. An explicit `Expiration` is sent as its epoch seconds.
- A nonzero expiration is refused on a no-storage channel only when the caller provides the policy. Otherwise APNs's answer is mapped.
- Apple's broadcast page shows the `apns-push-type` value as `Liveactivity`, while every other page uses `liveactivity`. Settle the casing from the node-apn capture and Apple's samples, and record the evidence.
- The result carries `apns-request-id` and `apns-unique-id`.

### E3. Live Activity `input-push-channel`
- A channel id written on `start` events.

### E4. Oracle scenarios
- node-apn broadcast and channel-management requests are captured by a local HTTP/2 server in the generator.

## Execution and Verification

- Each PR is built by serial implementation subagents in its own branch. The lead verifies, formats, and commits path-limited.
- Each PR passes:
  - the push-notification unit test projects
  - Release builds with 0 warnings
  - `make format-check`
  - `make quality-analyzers-project` for the changed packages
  - a code review with an adversarial pass on GLM-5.3
  - CI dispatched on the branch, because CI runs only for PRs into `main`
- Not possible: a live Apple sandbox run. The oracle harness and the fake servers stand in, and each PR says so.
