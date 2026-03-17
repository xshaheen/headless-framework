---
status: pending
priority: p2
issue_id: "019"
tags: ["code-review","reliability","dotnet"]
dependencies: []
---

# NodeHeartBeatBackgroundService silently dies on error + event subscription leak

## Problem Statement

Two hosted service issues: (1) HeartbeatBackgroundService.ExecuteAsync catches all exceptions and stops — no retry, no restart, heartbeats silently cease while service appears healthy. Also uses string interpolation for exception logging instead of structured logging. (2) JobsInitializationHostedService subscribes to NotifyCoreAction event in StartAsync but never unsubscribes in StopAsync.

## Findings

- **Silent death:** src/Headless.Jobs.Caching.Redis/NodeHeartBeatBackgroundService.cs:30-32
- **Wrong logging:** logger.LogError('...{Exception}', e) should be logger.LogError(e, '...')
- **Event leak:** src/Headless.Jobs.Core/Src/BackgroundServices/JobsInitializationHostedService.cs:29-56
- **Discovered by:** strict-dotnet-reviewer, pragmatic-dotnet-reviewer

## Proposed Solutions

### Add retry loop with delay + fix logging + unsubscribe in StopAsync
- **Pros**: Resilient heartbeat, proper structured logging, clean shutdown
- **Cons**: None
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Wrap heartbeat loop in retry with exponential backoff. Use logger.LogError(e, message). Store event delegate and unsubscribe in StopAsync.

## Acceptance Criteria

- [ ] Heartbeat service recovers from transient failures
- [ ] Exception logged as structured data
- [ ] Event unsubscribed on shutdown

## Notes

Source: Code review

## Work Log

### 2026-03-17 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin
