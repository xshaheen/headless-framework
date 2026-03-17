---
status: done
priority: p1
issue_id: "004"
tags: ["code-review","performance","dotnet"]
dependencies: []
---

# new HttpClient() per request in _PingServices causes socket exhaustion

## Problem Statement

MessagingDashboardEndpoints._PingServices creates a new HttpClient per request. Each Dispose puts the socket in TIME_WAIT (up to 240s). Under dashboard polling load, ephemeral ports exhaust within minutes. IHttpClientFactory is already registered in the project via GatewayProxyAgent.

## Findings

- **Location:** src/Headless.Messaging.Dashboard/Endpoints/MessagingDashboardEndpoints.cs:525
- **Risk:** Critical — socket exhaustion at 100 req/min within 30 minutes
- **Existing pattern:** GatewayProxyAgent.cs:16 already uses IHttpClientFactory
- **Discovered by:** performance-oracle, strict-dotnet-reviewer, pragmatic-dotnet-reviewer, security-sentinel, code-simplicity-reviewer

## Proposed Solutions

### Inject IHttpClientFactory into handler parameter
- **Pros**: Minimal API auto-resolves from DI, consistent with existing GatewayProxy pattern
- **Cons**: None
- **Effort**: Small
- **Risk**: None


## Recommended Action

Add IHttpClientFactory parameter to _PingServices and call httpClientFactory.CreateClient()

## Acceptance Criteria

- [ ] HttpClient obtained via IHttpClientFactory
- [ ] No direct new HttpClient() in endpoint handlers

## Notes

Confirmed by 5 independent reviewers. Also a DoS amplifier since the endpoint is AllowAnonymous.

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-17 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-17 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
