---
status: pending
priority: p3
issue_id: "146"
tags: [code-review, security, paymob, cashin]
dependencies: []
---

# UnsafeRelaxedJsonEscaping Security Risk

## Problem Statement

The `PaymobCashInOptions` uses `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` which disables escaping of characters like `<`, `>`, `&`, and `'`. If any user-supplied data is serialized and later rendered in HTML context, this creates XSS vulnerability.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/Models/PaymobCashInOptions.cs:43-48`
- **Code**:
  ```csharp
  public JsonSerializerOptions SerializationOptions { get; set; } =
      new(JsonSerializerDefaults.Web)
      {
          DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
          Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
      };
  ```
- "Unsafe" is literally in the name
- Could enable XSS if serialized data is rendered in HTML

## Proposed Solutions

### Option 1: Use Default Encoder (Recommended)

**Approach:** Remove unsafe encoder or use `JavaScriptEncoder.Default`.

```csharp
public JsonSerializerOptions SerializationOptions { get; set; } =
    new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Encoder = JavaScriptEncoder.Default,  // Or just omit
    };
```

**Pros:**
- Safe by default
- XSS protection built-in

**Cons:**
- May escape some characters unnecessarily (but that's safe)
- Verify Paymob API accepts escaped JSON

**Effort:** 15 minutes

**Risk:** Low (test with Paymob API)

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/Models/PaymobCashInOptions.cs:47`

## Acceptance Criteria

- [ ] UnsafeRelaxedJsonEscaping removed
- [ ] Paymob API integration tests pass
- [ ] No XSS vectors in serialized output

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified unsafe JSON encoder usage
