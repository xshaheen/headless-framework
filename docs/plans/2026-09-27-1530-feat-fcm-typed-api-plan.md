---
title: Firebase Provider Correctness and Typed FCM API - Plan
type: feat
date: 2026-09-27
artifact_contract: x-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: session-direction
execution: code
---

# Firebase Provider Correctness and Typed FCM API - Plan

## Goal Capsule

- **Objective:** bring the Firebase provider to the same bar as the APNs provider. That means fixing its retry and error handling to follow Google's FCM guidance, keeping its per-token promise that a send never throws, classifying every failure, adding metrics and tracing, and adding a typed Firebase API with the Android, web, and APNs-bridge options the shared request cannot express.
- **Branch:** its own PR, based on #984's branch `shaheen/feat/apns-rich-features`, which holds the current Firebase code. It is independent of the APNs-only PRs #991, #993, and #994.
- **Authority:** Google's FCM documentation wins on behavior. Where Google contradicts itself, take the safer reading. `CLAUDE.md` and `docs/solutions/conventions/provider-setup-and-options.md` bind every unit.

## Evidence

**Google** (firebase.google.com, checked 2026-09-27):
- *Error codes* and *Scale FCM*: do not retry 400/401/403/404. Retry 500 and 503 with exponential backoff and jitter. "Wait at least 10 seconds before retrying a failed request." For `QUOTA_EXCEEDED`, honor Retry-After or default to 60 seconds.
- *Manage tokens*: delete tokens on `UNREGISTERED`, and on `INVALID_ARGUMENT` only when the error is about the token itself.
- Payload limit: 4096 bytes, or 2048 bytes for topic sends. There is no title or body limit. Maximum TTL is 28 days.
- There is no idempotency key, so a retry can deliver twice.

**FirebaseAdmin .NET 3.6.0 source:**
- It already retries HTTP 503 and every non-cancellation exception up to 4 times, honoring a Retry-After of up to 30 seconds. This cannot be configured.
- It does not retry 429 or 500.
- It wraps `HttpRequestException` in `FirebaseMessagingException` with `MessagingErrorCode == null`, and lets cancellation, timeout, and credential exceptions escape a single send.
- `SendEachForMulticastAsync` throws only `ArgumentException`. Per-message errors, including cancellation, come back inside `BatchResponse`.
- It starts all 500 sends at once.

**Other libraries** (firebase-admin Node/Python/Go/Java, fcm-django, kreait/firebase-php, PyFCM, CorePush):
- None retries per-token failures inside a multicast.
- fcm-django and kreait treat `SENDER_ID_MISMATCH` as a dead token.
- kreait exposes `retryAfter()`.
- Go caps multicast concurrency at 50 and kreait at 25.
- None ships metrics.

## Key Decisions

- **KD1: retries.** The SDK owns 503 and transport retries. Our layer adds only what the SDK does not retry: `INTERNAL` (500) and `QUOTA_EXCEEDED` (429).
  - Our layer stops retrying `UNAVAILABLE`, which removes the double retry.
  - The delays follow Google: at least 10 seconds with jitter, and for quota at least 60 seconds, or Retry-After if longer.
  - Multicast retries only the failed transient indices, merged back by index. The Polly wrapper around the whole batch goes.
  - Defaults: `Retry.MaxAttempts` is 2 and `MaxDelay` is 5 minutes. Setting `MaxAttempts` to 0 disables in-process retry, for callers who retry from a queue.
- **KD2: never throw per token.** A single send maps every exception except caller cancellation to a `Failure`. Multicast checks `cancellationToken` after the SDK returns and rethrows caller cancellation. A timeout is a `Transport` failure.
- **KD3: dead tokens.** `UNREGISTERED` means `Unregistered`. `SENDER_ID_MISMATCH` stays a `Failure` (`Configuration`) unless `FirebaseOptions.TreatSenderIdMismatchAsUnregistered` is set. That is the same protective default as APNs `BadDeviceToken`: a host signed in to the wrong Firebase project would otherwise delete every valid token. `INVALID_ARGUMENT` is never read as a dead token.
- **KD4: classification.** A new public `FcmFailureKind`: `TokenInvalid`, `Throttled`, `ServerError`, `Authentication` (`THIRD_PARTY_AUTH_ERROR` or credential failure), `Configuration` (`SENDER_ID_MISMATCH`), `Payload` (`INVALID_ARGUMENT`), and `Transport`. The typed result carries `ErrorCode`, `FailureKind`, `IsRetryable`, and `RetryAfter`.
- **KD5: the typed API.** A new `IFcmPushNotificationService`, registered by `UseFirebase` next to `IPushNotificationService`, both resolving to one instance.
  - Methods: `SendAsync(target token/FID, FcmMessage)`, `SendMulticastAsync(tokens, FcmMessage)`, `SendToTopicAsync(topic, FcmMessage)`, and `SendToConditionAsync(condition, FcmMessage)`.
  - `FcmMessage` is our own record, so SDK types never leak into the public API. It holds `Notification` (title, body, image), `Data`, `Android` (channel id, priority, TTL, collapse key, tag, color, icon, click action, sound, notification count, visibility, image, direct boot), `Webpush` (link, headers, data), `Apns` (headers and a raw `aps`/custom JSON object), `AnalyticsLabel` (checked against Google's pattern), and `DryRun` (`validate_only`).
  - A condition may name at most 5 topics. The topic name must not carry the `/topics/` prefix.
- **KD6: limits.** Remove the made-up 100-character title and 4000-character body limits. Keep the reserved data keys, adding `gcm.` and `google.`, and keep the 28-day TTL. Do not size-check the payload on the client: FCM's `INVALID_ARGUMENT` "message too big" is the authority.
- **KD7: telemetry.** Add a `Meter` and an `ActivitySource` named `Headless.PushNotifications.Firebase`, following the APNs pattern.
  - Instruments: sends (tagged with outcome, failure kind, and error code), a send-duration histogram, and retries.
  - Tracing: one activity per device send and one per topic or condition send. Tokens and payloads are never tags.
- **KD8: tests that exercise the SDK.** Build the `FirebaseApp` with `AppOptions.HttpClientFactory` over a fake `HttpMessageHandler` that returns FCM v1 JSON errors. The tests then run the real SDK error mapping, our retries, the batch path, and cancellation. Keep the `IFcmMessageSender` seam only where it still earns its place.

## Units

- **U1: correctness** (KD1, KD2, KD3, KD6, KD8)
  - Covers `Internals/FcmMessageSender.cs`, `Internals/RetryHelper.cs`, `Setup.cs`, `FirebaseOptions.cs`, `FcmPushNotificationService.cs`, the fake-HTTP test infrastructure, and tests.
  - Every defect above gets a test that fails first.
- **U2: typed API, classification, telemetry, docs** (KD4, KD5, KD7)
  - Adds the new public types, the service, their registration, telemetry, docs/llms/push-notifications.md, and tests.

## Verification

- All push-notification unit test projects pass.
- The Release build has 0 warnings, `make format-check` passes, and `make quality-analyzers-project` passes for the Firebase package.
- A code review runs with an adversarial pass on GLM-5.3.
- CI is dispatched on the branch.
- Not possible: a live FCM send without project credentials. The fake HTTP handler runs the real SDK code instead.
