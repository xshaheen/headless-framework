---
status: done
priority: p1
issue_id: "001"
tags: [code-review, security, messages, critical]
created: 2026-01-19
dependencies: []
---

# Unsafe Type Conversion with Convert.ChangeType

## Problem Statement

`ISubscribeInvoker.Default.cs:59` uses unsafe `Convert.ChangeType()` fallback without validation, enabling type confusion attacks and DoS.

**Why Critical:** Attackers can craft malicious messages triggering `InvalidCastException` or potentially arbitrary code execution via deserialization gadgets.

## Evidence from Reviews

**Security Sentinel (Agent a0fbd6f):**
```csharp
// Line 59
else
{
    messageInstance = Convert.ChangeType(mediumMessage.Origin.Value, messageType);
}
```

**Attack Vector:**
- Craft message with unexpected type in `mediumMessage.Origin.Value`
- Trigger `InvalidCastException` → DoS
- Potential type confusion → RCE

## Proposed Solutions

### Option 1: Remove Fallback (Recommended)
**Effort:** Small
**Risk:** Low - forces proper serialization

```csharp
else
{
    throw new InvalidOperationException(
        $"Unsupported message value type: {mediumMessage.Origin.Value?.GetType().Name}. " +
        $"Expected JSON string, JsonElement, or {messageType.Name}"
    );
}
```

### Option 2: Whitelist Allowed Types
**Effort:** Medium
**Risk:** Medium - may miss valid scenarios

```csharp
else if (messageType.IsPrimitive || messageType == typeof(string))
{
    messageInstance = Convert.ChangeType(mediumMessage.Origin.Value, messageType);
}
else
{
    throw new InvalidOperationException(...);
}
```

### Option 3: Type Registry
**Effort:** Large
**Risk:** Low - most flexible

Maintain allowed types in `ConsumerRegistry`, validate before conversion.

## Technical Details

**Affected Files:**
- `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs:59`

**Related Code:**
```csharp
// Lines 38-61: Deserialization logic
if (serializer.IsJsonType(mediumMessage.Origin.Value))
{
    messageInstance = serializer.Deserialize(...);
}
else if (mediumMessage.Origin.Value is string jsonString)
{
    messageInstance = System.Text.Json.JsonSerializer.Deserialize(...);
}
else if (messageType.IsInstanceOfType(mediumMessage.Origin.Value))
{
    messageInstance = mediumMessage.Origin.Value;
}
else
{
    messageInstance = Convert.ChangeType(...);  // ❌ REMOVE THIS
}
```

## Acceptance Criteria

- [ ] Remove `Convert.ChangeType` fallback or add type whitelist
- [ ] Add unit test: `should_reject_unsupported_message_value_types`
- [ ] Verify all demos still work (10 demo apps)
- [ ] Security review confirms vulnerability closed

## Work Log

- **2026-01-19:** Issue identified during code review (Security Sentinel agent)

## Resources

- PR: (current branch `xshaheen/messaging-consume`)
- Security Review: Agent a0fbd6f output
- File: `src/Framework.Messages.Core/Internal/ISubscribeInvoker.Default.cs`

### 2026-01-19 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-19 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
