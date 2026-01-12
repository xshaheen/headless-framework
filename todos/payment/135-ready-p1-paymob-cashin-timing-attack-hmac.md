---
status: ready
priority: p1
issue_id: "135"
tags: [code-review, security, paymob, cashin]
dependencies: []
---

# Timing Attack Vulnerability in HMAC Validation

## Problem Statement

The HMAC validation in PaymobCashInBroker uses `String.Equals()` which performs early-exit comparison. An attacker can measure response times to progressively guess valid HMAC values byte-by-byte, potentially forging payment callbacks.

**This is a CRITICAL security vulnerability in payment callback validation.**

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.ValidateHmac.cs:36`
- **Vulnerable code**:
  ```csharp
  return lowerCaseHexHash.Equals(hmac, StringComparison.Ordinal);
  ```
- `String.Equals()` short-circuits on first difference
- HMAC validation is a hot path executed on every payment callback
- Could allow forging of payment webhooks (mark unpaid orders as paid)

## Proposed Solutions

### Option 1: Use CryptographicOperations.FixedTimeEquals (Recommended)

**Approach:** Use .NET's built-in constant-time comparison.

```csharp
var computedBytes = Convert.FromHexString(computedHmac);
var providedBytes = Convert.FromHexString(hmac);
return CryptographicOperations.FixedTimeEquals(computedBytes, providedBytes);
```

**Pros:**
- Standard library, well-tested
- Constant time regardless of input
- Simple implementation

**Cons:**
- Requires conversion to byte arrays

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Direct Byte Comparison with Static HashData

**Approach:** Avoid string conversion entirely, compare bytes directly.

```csharp
var keyBytes = Encoding.UTF8.GetBytes(_options.Hmac);
var textBytes = Encoding.UTF8.GetBytes(concatenatedString);
Span<byte> computedHash = stackalloc byte[64];
HMACSHA512.HashData(keyBytes, textBytes, computedHash);

var expectedBytes = Convert.FromHexString(hmac);
return CryptographicOperations.FixedTimeEquals(computedHash, expectedBytes);
```

**Pros:**
- More efficient (fewer allocations)
- Uses static HashData method
- Still constant-time

**Cons:**
- More code changes
- Requires .NET 6+

**Effort:** 1 hour

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.ValidateHmac.cs:26-36`

**Related components:**
- All payment callback handling
- Webhook endpoints that call Validate()

## Resources

- **OWASP Timing Attack**: https://owasp.org/www-community/vulnerabilities/Timing_attack
- **CryptographicOperations.FixedTimeEquals**: https://docs.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.fixedtimeequals

## Acceptance Criteria

- [ ] HMAC comparison uses CryptographicOperations.FixedTimeEquals
- [ ] All Validate() overloads updated
- [ ] Unit tests verify constant-time behavior (no early exit)
- [ ] Security review passed

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified timing attack vulnerability in HMAC validation
- Analyzed all 4 Validate() overloads
- Drafted solution using CryptographicOperations.FixedTimeEquals

**Learnings:**
- Payment callback validation is security-critical
- String comparison is never safe for cryptographic operations
