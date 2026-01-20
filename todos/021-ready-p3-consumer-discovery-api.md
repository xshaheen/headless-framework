---
status: ready
priority: p3
issue_id: "021"
tags: [agent-native, introspection, api-design, discoverability]
dependencies: []
---

# Missing Consumer Discovery API (Agent-Native)

## Problem Statement

No programmatic API to discover registered consumers and their topics, limiting agent-driven workflows and runtime introspection.

## Findings

**Current State**:
- ConsumerRegistry exists but is internal/singleton
- No public API to query: "what consumers handle topic X?"
- No endpoint to list all registered consumers

**Agent-Native Use Cases**:
1. Runtime validation: "Is there a consumer for this topic?"
2. Documentation generation: List all topics and handlers
3. Testing: Verify consumer registration
4. Debugging: Inspect routing table

## Proposed Solutions

### Option 1: Public Discovery API (RECOMMENDED)
**Effort**: 3-4 hours

```csharp
public interface IConsumerRegistry
{
    IReadOnlyList<ConsumerMetadata> GetAll();
    ConsumerMetadata? FindByTopic(string topic, string? group = null);
    IEnumerable<ConsumerMetadata> FindByMessageType<TMessage>();
}

// Make registry accessible:
services.AddSingleton<IConsumerRegistry, ConsumerRegistry>();
```

### Option 2: HTTP Endpoint (/messaging/consumers)
**Effort**: 2-3 hours

Add to dashboard:
```csharp
[HttpGet("/messaging/consumers")]
public IActionResult GetConsumers()
{
    return Ok(_registry.GetAll().Select(c => new
    {
        c.Topic,
        c.MessageType.Name,
        c.ConsumerType.Name,
        c.Group
    }));
}
```

## Recommended Action

Implement both - Option 1 for programmatic access, Option 2 for humans.

## Acceptance Criteria

- [ ] IConsumerRegistry interface public
- [ ] FindByTopic method available
- [ ] HTTP endpoint returns JSON list
- [ ] Documentation explains usage
- [ ] Tests verify discovery scenarios

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Agent-Native Reviewer)

**Actions:**
- Identified missing introspection capability
- Proposed dual API (programmatic + HTTP)
- Aligned with agent-native principles

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready
