---
status: pending
priority: p2
issue_id: "140"
tags: [code-review, performance, paymob, cashin]
dependencies: ["135"]
---

# Excessive Memory Allocations in HMAC Validation

## Problem Statement

The `Validate` method in PaymobCashInBroker allocates heavily on every callback validation (hot path):
- ~7+ heap allocations per HMAC check
- HMAC key bytes are re-encoded on every call (same key!)
- StringBuilder and string interpolation in hex conversion

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.ValidateHmac.cs:26-56`

**Current allocations per call:**
1. `Encoding.UTF8.GetBytes(concatenatedString)` - byte[]
2. `Encoding.UTF8.GetBytes(_options.Hmac)` - byte[] (same key every time!)
3. `new HMACSHA512(keyBytes)` - object allocation
4. `hash.ComputeHash(textBytes)` - byte[]
5. `new StringBuilder(...)` - object allocation
6. String interpolation `$"{b:x2}"` per byte (64 iterations for SHA512)
7. `sb.ToString()` - string allocation

## Proposed Solutions

### Option 1: Use Modern .NET APIs (Recommended)

**Approach:** Use `HMACSHA512.HashData` (static, no allocation) and `Convert.ToHexStringLower`.

```csharp
public bool Validate(string concatenatedString, string hmac)
{
    Argument.IsNotNullOrEmpty(concatenatedString);
    Argument.IsNotNullOrEmpty(hmac);

    var keyBytes = Encoding.UTF8.GetBytes(Options.Hmac);
    var textBytes = Encoding.UTF8.GetBytes(concatenatedString);
    var hashBytes = HMACSHA512.HashData(keyBytes, textBytes);
    var computedHmac = Convert.ToHexStringLower(hashBytes);

    // Use CryptographicOperations.FixedTimeEquals (from issue #135)
    return CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(computedHmac),
        Convert.FromHexString(hmac));
}
```

**Pros:**
- Removes HMAC object allocation
- Uses built-in hex conversion
- Cleaner code

**Cons:**
- Still allocates for byte arrays

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Full Span-Based Optimization

**Approach:** Use stackalloc and ArrayPool for zero heap allocations.

```csharp
public bool Validate(string concatenatedString, string hmac)
{
    // Use ArrayPool for text bytes, stackalloc for hash
    var maxByteCount = Encoding.UTF8.GetMaxByteCount(concatenatedString.Length);
    var textBytes = ArrayPool<byte>.Shared.Rent(maxByteCount);
    try
    {
        var actualByteCount = Encoding.UTF8.GetBytes(concatenatedString, textBytes);
        Span<byte> hashBytes = stackalloc byte[64]; // SHA512 = 64 bytes

        // Cache key bytes (or use stackalloc if small)
        var keyBytes = Encoding.UTF8.GetBytes(Options.Hmac);
        HMACSHA512.HashData(keyBytes, textBytes.AsSpan(0, actualByteCount), hashBytes);

        // Compare...
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(textBytes);
    }
}
```

**Pros:**
- Near-zero allocations
- Best for high-throughput scenarios

**Cons:**
- More complex code
- Requires careful buffer management

**Effort:** 1-2 hours

**Risk:** Medium

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/PaymobCashInBroker.ValidateHmac.cs`

**Hot path analysis:**
- Called on every payment callback webhook
- High-volume merchants could have thousands of callbacks/day

## Acceptance Criteria

- [ ] HMAC object allocation eliminated (use static HashData)
- [ ] Hex conversion uses Convert.ToHexStringLower
- [ ] Benchmark shows reduced allocations
- [ ] Timing-safe comparison preserved (from #135)

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Analyzed allocation profile of HMAC validation
- Identified 7+ allocations per call
- Drafted optimization solutions
