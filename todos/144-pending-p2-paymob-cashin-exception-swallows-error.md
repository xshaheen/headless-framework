---
status: pending
priority: p2
issue_id: "144"
tags: [code-review, error-handling, paymob, cashin]
dependencies: []
---

# Exception Swallowed When Reading Error Body

## Problem Statement

In `PaymobCashInException.ThrowAsync`, if reading the response body fails, the exception is silently swallowed with an empty catch block. This loses diagnostic information about why the body couldn't be read.

## Findings

- **Location**: `src/Framework.Payments.Paymob.CashIn/Models/PaymobCashInException.cs:27-35`
- **Code**:
  ```csharp
  try
  {
      body = await response.Content.ReadAsStringAsync();
  }
  #pragma warning disable ERP022
  catch
  {
      body = null;  // SWALLOWED - we lose diagnostic information
  }
  #pragma warning restore ERP022
  ```
- The pragma disable indicates this was intentional but problematic
- If body read fails, we have no idea why
- Could be timeout, encoding issue, or other problems

## Proposed Solutions

### Option 1: Include Exception Type in Message (Recommended)

**Approach:** Capture and include exception type for diagnostics.

```csharp
public static async Task ThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
{
    if (response.IsSuccessStatusCode)
    {
        return;
    }

    string? body;
    string? readError = null;
    try
    {
        body = await response.Content.ReadAsStringAsync(cancellationToken).AnyContext();
    }
    catch (Exception ex)
    {
        body = null;
        readError = ex.GetType().Name;
    }

    var statusCode = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
    var message = readError is null
        ? $"Paymob CashIn HTTP request failed with status code {statusCode}."
        : $"Paymob CashIn HTTP request failed with status code {statusCode}. Body read failed: {readError}";

    throw new PaymobCashInException(message, response.StatusCode, body);
}
```

**Pros:**
- Preserves diagnostic information
- Clear when body read failed
- Doesn't expose full exception details

**Cons:**
- Slightly longer message

**Effort:** 30 minutes

**Risk:** Low

## Recommended Action

**To be filled during triage.**

## Technical Details

**Affected files:**
- `src/Framework.Payments.Paymob.CashIn/Models/PaymobCashInException.cs:18-42`

## Acceptance Criteria

- [ ] Exception type captured when body read fails
- [ ] Message indicates body read failure
- [ ] CancellationToken added to ReadAsStringAsync
- [ ] No sensitive data leaked

## Work Log

### 2026-01-11 - Initial Discovery

**By:** Claude Code (Code Review)

**Actions:**
- Identified swallowed exception in error handling
- Drafted solution preserving diagnostic info
