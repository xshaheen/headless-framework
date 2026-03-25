---
title: "Transport wrapper drift: keep adapters, display surfaces, and generated docs aligned"
category: messaging
date: 2026-03-25
tags: [messaging, nats, kafka, pulsar, documentation, credentials, wrappers]
problem_type: integration_issue
components:
  - KafkaConsumerClient
  - ConnectionFactory
  - MessagingNatsOptions
  - NatsConsumerClient
  - Messaging dashboard docs
research:
  agents: [compact-safe-local]
  documented_at: 2026-03-25T00:00:00Z
  conversation_context: "PR #199 transport follow-up todos across Kafka, Pulsar, NATS, and generated messaging docs"
---

# Transport wrapper drift: keep adapters, display surfaces, and generated docs aligned

Several transport todos came from the same failure mode: wrapper code and generated docs drifted away from the real library and framework API surface after refactors and package changes. The branch ended up with compile breaks in Kafka and Pulsar, stale NATS and dashboard docs, and raw NATS server URIs being reused as display values even though they may contain credentials.

## Root Cause

The transport layer and doc generation pipeline were treated as stable wrappers, but they depend on moving contracts:

- third-party client APIs such as Pulsar disposal methods
- local fluent APIs such as `UseNats`, dashboard auth methods, and stream options
- broker URI inputs that are safe for connection setup but unsafe for logs or metadata

Once those contracts changed, the repo had no guardrail forcing code samples, generated docs, and display surfaces to update together.

## Working Fix

Apply the contract at the boundary that actually owns it:

```csharp
// Constructor defaults need real delegates, not invalid method-group coalescing.
_consumerFactory = consumerFactory ?? _BuildConsumer;
_adminClientFactory = adminClientFactory ?? _BuildAdminClient;

// Verify the wrapped client API against the referenced package, not memory.
await _client.CloseAsync().ConfigureAwait(false);
```

For broker surfaces, separate connection input from operator-facing output:

```csharp
internal string GetSanitizedServersForDisplay()
{
    return string.Join(",",
        Servers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(_SanitizeServerForDisplay));
}
```

For generated docs, update the source examples at the same time as the API rename:

```csharp
options.UseDashboard(dashboard =>
{
    dashboard.WithBasicAuth("admin", "password");
});

options.UseNats(nats =>
{
    nats.EnableSubscriberClientStreamAndSubjectCreation = false;
});
```

## Greenfield Scope Rule

If a review todo mixes a current runtime defect with migration work, split them. For greenfield modules with no live consumers yet, fix the current runtime path and explicitly drop speculative migration logic from the todo and implementation. That kept the NATS subject-coverage fix focused on correct fresh stream creation instead of adding upgrade reconciliation that was not needed yet.

## Prevention

- Treat wrapper code as contract code. Verify third-party APIs from the referenced package before changing disposal, lifetime, or factory signatures.
- Never reuse raw broker connection strings for logs, telemetry, or `BrokerAddress`-style metadata. Add a sanitized display path once and route all operator-facing surfaces through it.
- When renaming or reshaping a public transport API, update human docs and generated LLM docs in the same change.
- Add narrow unit seams for tricky async behavior so fixes do not rely only on code inspection.

## Related

- [circuit-breaker-transport-thread-safety-patterns.md](/Users/xshaheen/Dev/framework/headless-framework/docs/solutions/concurrency/circuit-breaker-transport-thread-safety-patterns.md)
