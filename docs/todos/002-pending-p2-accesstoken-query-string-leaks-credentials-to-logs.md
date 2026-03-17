---
status: pending
priority: p2
issue_id: "002"
tags: ["code-review","security","dotnet"]
dependencies: []
---

# access_token query string leaks credentials to logs and Referer headers

## Problem Statement

AuthService._GetAuthorizationValue accepts auth tokens from ?access_token query parameter (intended for SignalR). Query strings appear in server logs, browser history, and Referer headers. AuthMiddleware.cs:51 logs the full path including query string on auth failure, writing the token to logs.

## Findings

- **Location:** src/Headless.Dashboard.Authentication/AuthService.cs:80-85, AuthMiddleware.cs:51
- **OWASP:** A02:2021 — Cryptographic Failures / A09:2021 — Security Logging
- **Discovered by:** security-sentinel

## Proposed Solutions

### Restrict access_token to SignalR paths only + sanitize log path
- **Pros**: Minimal change, closes the leak
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Only accept access_token on paths matching */hub* or */negotiate*. Strip query string from log messages.

## Acceptance Criteria

- [ ] access_token only accepted on SignalR paths
- [ ] Log messages don't contain query strings with tokens
- [ ] SignalR WebSocket connections still authenticate

## Notes

Combined with the /negotiate Contains bypass (P1), this is a compounding vulnerability.

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
