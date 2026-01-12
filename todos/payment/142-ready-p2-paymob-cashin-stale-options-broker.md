---
status: ready
priority: p2
issue_id: "142"
tags: [code-review, configuration, paymob, cashin]
dependencies: []
---

# Broker Captures Options at Construction (Stale Options)

## Problem Statement

`PaymobCashInBroker` captures `optionsAccessor.CurrentValue` at construction time. If options change at runtime, the broker will use stale configuration. This is inconsistent with `PaymobCashInAuthenticator` which correctly uses `_options.CurrentValue` on each access.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.cs:8-15`
- **Code**:
  ```csharp
  public partial class PaymobCashInBroker(
      HttpClient httpClient,
      IPaymobCashInAuthenticator authenticator,
      IOptionsMonitor<PaymobCashInOptions> optionsAccessor
  ) : IPaymobCashInBroker
  {
      private readonly PaymobCashInOptions _options = optionsAccessor.CurrentValue;  // Captured once!
  }
  ```
- Broker is Scoped, so impact is limited to request lifetime
- But inconsistent with Authenticator pattern which properly uses IOptionsMonitor
- If options change during long-lived scope, broker uses stale values

## Proposed Solutions

### Option 1: Access CurrentValue Each Time (Recommended)

**Approach:** Keep the monitor and access `CurrentValue` in methods.

```csharp
public partial class PaymobCashInBroker(
    HttpClient httpClient,
    IPaymobCashInAuthenticator authenticator,
    IOptionsMonitor<PaymobCashInOptions> options
) : IPaymobCashInBroker
{
    private PaymobCashInOptions Options => options.CurrentValue;
}
```

**Pros:**
- Consistent with Authenticator pattern
- Options changes take effect immediately
- Cleaner property access

**Cons:**
- Minor performance overhead (negligible)

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Use IOptionsSnapshot (Alternative)

**Approach:** Use `IOptionsSnapshot<T>` which is scoped-appropriate.

```csharp
public partial class PaymobCashInBroker(
    HttpClient httpClient,
    IPaymobCashInAuthenticator authenticator,
    IOptionsSnapshot<PaymobCashInOptions> options
) : IPaymobCashInBroker
{
    private PaymobCashInOptions Options => options.Value;
}
```

**Pros:**
- Semantically correct for Scoped service
- Options are consistent within scope

**Cons:**
- Different interface than Authenticator uses

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.cs`
- All partial files that reference `_options`

## Acceptance Criteria

- [ ] Broker accesses CurrentValue on each use (or uses IOptionsSnapshot)
- [ ] Pattern consistent across package
- [ ] Tests verify options changes are reflected

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified stale options capture pattern
- Compared with Authenticator pattern
- Drafted solutions
