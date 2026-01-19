---
status: done
priority: p1
issue_id: "003"
tags: [code-review, security, performance, messages, redos]
created: 2026-01-19
dependencies: []
---

# ReDoS Vulnerability in Wildcard Topic Matching

## Problem Statement

Unsafe regex pattern construction from user-controlled topic names enables catastrophic backtracking (ReDoS), causing CPU exhaustion.

**Why Critical:** Malicious topic patterns like `a*.b*.c*.d*.e*.f*.g*` exhibit O(2^n) complexity, exhausting CPU across all consumer threads.

## Evidence from Reviews

**Security Sentinel (Agent a0fbd6f):**
```csharp
// Helper.cs:44-56
public static string WildcardToRegex(string wildcard)
{
    if (wildcard.IndexOf('*') >= 0)
    {
        return ("^" + wildcard + "$").Replace("*", "[0-9a-zA-Z]+").Replace(".", "\\.");
    }
    // No length limits, no wildcard count limits, no timeout
}
```

**Attack Vector:**
1. Register consumer with pattern: `a*.b*.c*.d*.e*.f*.g*`
2. Send long messages matching pattern
3. Regex engine backtracking → O(2^n) complexity
4. All consumer threads blocked → complete DoS

## Proposed Solutions

### Option 1: Add Limits + Non-Greedy Quantifiers (Recommended)
**Effort:** Medium
**Risk:** Low

```csharp
public static string WildcardToRegex(string wildcard)
{
    const int MaxWildcardLength = 200;
    const int MaxWildcardCount = 10;

    if (wildcard.Length > MaxWildcardLength)
    {
        throw new ArgumentException(
            $"Topic pattern exceeds maximum length of {MaxWildcardLength} characters"
        );
    }

    int wildcardCount = wildcard.Count(c => c == '*' || c == '#');
    if (wildcardCount > MaxWildcardCount)
    {
        throw new ArgumentException(
            $"Topic pattern contains too many wildcards (max: {MaxWildcardCount})"
        );
    }

    if (wildcard.IndexOf('*') >= 0)
    {
        return ("^" + Regex.Escape(wildcard) + "$")
            .Replace(Regex.Escape("*"), "[0-9a-zA-Z]+?");  // Non-greedy
    }
    if (wildcard.IndexOf('#') >= 0)
    {
        return ("^" + Regex.Escape(wildcard) + "$")
            .Replace(Regex.Escape("#"), "[0-9a-zA-Z\\.]+?");  // Non-greedy
    }
    return Regex.Escape(wildcard);
}
```

### Option 2: Add Regex Timeout
**Effort:** Small
**Risk:** Medium - messages may fail legitimately

```csharp
// In MethodMatcherCache
if (Regex.IsMatch(key, red.Name, RegexOptions.Singleline, TimeSpan.FromSeconds(1)))
```

### Option 3: Pre-compiled Regex with Timeout (Best)
**Effort:** Large
**Risk:** Low

Compile regex at registration time with timeout, store in cache.

## Technical Details

**Affected Files:**
- `src/Framework.Messages.Core/Internal/Helper.cs:44-56`
- `src/Framework.Messages.Core/Internal/IConsumerServiceSelector.Default.cs:176-182`

**ReDoS Pattern:**
```
Pattern: a*.b*.c*.d*.e*
Input:   aaabbbcccdddeee (matches)
Input:   aaabbbcccdddXXX (causes backtracking)
```

## Acceptance Criteria

- [ ] Add max pattern length validation (200 chars)
- [ ] Add max wildcard count validation (10)
- [ ] Use non-greedy quantifiers (`+?` instead of `+`)
- [ ] Add regex timeout (1 second)
- [ ] Use `Regex.Escape()` before replacements
- [ ] Add unit test: `should_reject_excessively_long_topic_patterns`
- [ ] Add unit test: `should_reject_too_many_wildcards`
- [ ] Add performance test: verify O(n) not O(2^n)

## Work Log

- **2026-01-19:** Issue identified during security review

## Resources

- Security Review: Agent a0fbd6f
- OWASP ReDoS: https://owasp.org/www-community/attacks/Regular_expression_Denial_of_Service_-_ReDoS
- File: `src/Framework.Messages.Core/Internal/Helper.cs`

### 2026-01-19 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-01-19 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
