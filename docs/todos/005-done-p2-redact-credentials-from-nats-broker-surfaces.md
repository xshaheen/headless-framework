---
status: done
priority: p2
issue_id: "005"
tags: ["code-review","security","dotnet","nats"]
dependencies: []
---

# Redact credentials from NATS broker surfaces

## Problem Statement

`MessagingNatsOptions.Servers` explicitly allows embedded username/password information, but the branch now exposes that raw value through `BrokerAddress` and debug logging. Downstream logging or telemetry can leak NATS credentials.

## Findings

- **Location:** src/Headless.Messaging.Nats/MessagingNatsOptions.cs:15-19; src/Headless.Messaging.Nats/INatsConnectionPool.cs:25,38-42; src/Headless.Messaging.Nats/NatsTransport.cs:13; src/Headless.Messaging.Nats/NatsConsumerClient.cs:41
- **Risk:** Credential exposure via logs, telemetry, or diagnostics
- **Discovered by:** code review

## Proposed Solutions

### Redact URI userinfo
- **Pros**: Small targeted fix
- **Cons**: Need to handle multiple-server strings carefully
- **Effort**: Small
- **Risk**: Low

### Separate display endpoint from raw connection string
- **Pros**: Prevents future accidental leaks
- **Cons**: Touches more call sites
- **Effort**: Medium
- **Risk**: Low


## Recommended Action

Introduce a sanitized endpoint representation for broker addresses and logs, leaving the raw server string only for connection setup.

## Acceptance Criteria

- [x] No log or BrokerAddress path emits NATS credentials
- [x] Multi-server connection strings are redacted safely
- [x] Regression coverage verifies usernames/passwords are removed from surfaced endpoints

## Notes

Review of branch xshaheen/review-transports on 2026-03-25.

## Work Log

### 2026-03-25 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-25 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-25 - Implemented

**By:** Agent
**Actions:**
- Added sanitized server-display helpers for broker addresses and connection-pool surfaces
- Added unit coverage for single-server and multi-server credential redaction

### 2026-03-25 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
