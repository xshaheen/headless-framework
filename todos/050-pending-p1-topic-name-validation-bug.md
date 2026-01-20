---
status: pending
priority: p1
issue_id: "050"
tags: [code-review, dotnet, aws-sqs, validation, logic-bug]
created: 2026-01-20
dependencies: []
---

# Topic Name Validation Logic Bug

## Problem Statement

**File:** `src/Framework.Messages.AwsSqs/TopicNormalizer.cs:11`

```csharp
Argument.IsGreaterThan(origin.Length, 256);
```

**BUG:** Inverted logic. AWS SNS topics max 256 chars. This checks if `> 256` (rejects long names correctly) but argument name suggests checking for max length ≤ 256.

Likely `IsGreaterThan` throws when condition is TRUE. Should use `IsLessThanOrEqualTo(origin.Length, 256)` for clarity.

## Solution

```csharp
public static string NormalizeForAws(this string origin)
{
    Argument.IsNotNullOrWhiteSpace(origin);
    Argument.IsLessThanOrEqualTo(origin.Length, 256,
        "AWS SNS topic names max 256 characters");

    return origin.Replace('.', '-').Replace(':', '_');
}
```

**Also validate normalized length** (replacements don't change length but future-proof).

## Acceptance Criteria

- [ ] Change to `IsLessThanOrEqualTo(origin.Length, 256)`
- [ ] Add null/whitespace check
- [ ] Add test: 256-char name (pass)
- [ ] Add test: 257-char name (throw)
- [ ] Verify against AWS SDK limits

**Effort:** 15 min | **Risk:** Very Low
