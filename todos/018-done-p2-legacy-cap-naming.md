---
status: done
priority: p2
issue_id: "018"
tags: [naming, terminology, cap-rename, legacy]
dependencies: []
---

# Legacy CAP Naming Remains (58 Occurrences)

## Problem Statement

Despite comprehensive CAP→Messaging rename, 58 legacy references remain in diagnostics, comments, and error messages.

## Findings

**Remaining Locations**:
- Diagnostic listener names: "Headless.Messaging." prefix (should be "Framework.Messages.")
- GitHub issue references: "see: https://github.com/dotnetcore/CAP/issues/63"
- Internal class names: `ICapPublisher`, `CapOptions`
- XML comments: "CAP" references

**Examples**:
```csharp
// MessageDiagnosticListenerNames.cs:10
private const string _Prefix = "Headless.Messaging."; // Inconsistent

// ISubscribeExector.Default.cs:64
var error = $"... see: https://github.com/dotnetcore/CAP/issues/63"; // External reference
```

## Proposed Solutions

### Option 1: Complete Rename (RECOMMENDED)
**Effort**: 2-3 hours

Replace all occurrences:
- `Headless.Messaging.` → `Framework.Messages.`
- CAP GitHub links → internal documentation
- `ICapPublisher` → `IOutboxPublisher` (already exists)
- Comments: CAP → Messaging

### Option 2: Keep Internal Names
**Effort**: 1 hour
**Rationale**: Non-breaking if internal-only

Only rename public-facing strings.

## Recommended Action

Implement Option 1 for consistency.

## Acceptance Criteria

- [x] Zero occurrences of "CAP" in public APIs
- [x] Diagnostic names use Framework.Messages prefix
- [x] External URLs replaced with actionable guidance
- [x] Internal names consistent with terminology
- [x] Breaking changes documented (if any)

## Work Log

### 2026-01-20 - Issue Created

**By:** Claude Code (Pattern Recognition Specialist Agent)

**Actions:**
- Searched codebase for remaining CAP references
- Categorized by visibility and impact
- Recommended complete rename

### 2026-01-20 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-21 - Completed

**By:** Claude Code
**Actions:**
- Updated diagnostic prefix: "Headless.Messaging." → "Framework.Messages."
- Updated OpenTelemetry source name: "Headless.Messaging.OpenTelemetry" → "Framework.Messages.OpenTelemetry"
- Updated operation prefix: "Headless/" → "Framework.Messages/"
- Replaced all XML comment references: "CAP" → appropriate messaging terminology
- Replaced GitHub CAP issue URLs with actionable guidance messages
- Updated transaction-related comments: "CAP transaction" → "outbox transaction"
- Updated log messages: "CAP message" → "Message"
- Updated all provider-specific comments (Kafka, NATS, Pulsar, Azure Service Bus, etc.)
- Status changed: ready → done

**Breaking Changes:**
- Diagnostic listener names changed (affects OpenTelemetry integration):
  - Source name: "Headless.Messaging.OpenTelemetry" → "Framework.Messages.OpenTelemetry"
  - Event name prefix: "Headless.Messaging.*" → "Framework.Messages.*"
  - Operation prefix: "Headless/*" → "Framework.Messages/*"
- Any monitoring/observability dashboards using these names will need updates
