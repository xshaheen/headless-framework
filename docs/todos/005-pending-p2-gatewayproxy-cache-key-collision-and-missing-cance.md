---
status: pending
priority: p2
issue_id: "005"
tags: ["code-review","security","performance","dotnet"]
dependencies: []
---

# GatewayProxy cache key collision and missing CancellationToken/Dispose

## Problem Statement

Three issues in GatewayProxyAgent: (1) Cache key is raw string concat of requestNodeName + ns — name='foo',ns='bar-baz' collides with name='foo-bar',ns='baz'. (2) SendAsync called without context.RequestAborted — proxy continues after client disconnect. (3) HttpResponseMessage is never disposed in the happy path.

## Findings

- **Cache collision:** src/Headless.Messaging.Dashboard/GatewayProxy/GatewayProxyAgent.cs:51-53
- **Missing cancellation:** src/Headless.Messaging.Dashboard/GatewayProxy/GatewayProxyAgent.cs:90-91
- **Mutable field:** src/Headless.Messaging.Dashboard/GatewayProxy/GatewayProxyAgent.cs:29 — DownstreamRequest should be local
- **Discovered by:** security-sentinel, strict-dotnet-reviewer

## Proposed Solutions

### Use tuple key, add CancellationToken, using var response
- **Pros**: Simple fixes, no design changes
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Use (requestNodeName, ns) tuple as cache key. Add context.RequestAborted to SendAsync. Use 'using var response'. Make DownstreamRequest a local variable.

## Acceptance Criteria

- [ ] Cache key cannot collide for different (name, ns) pairs
- [ ] Proxy cancels on client disconnect
- [ ] HttpResponseMessage is disposed

## Notes

Source: Code review

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
