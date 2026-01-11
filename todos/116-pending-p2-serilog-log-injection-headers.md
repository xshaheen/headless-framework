---
status: pending
priority: p2
issue_id: "116"
tags: [code-review, security, serilog, log-injection]
dependencies: []
---

# Log Injection via Unsanitized HTTP Headers

## Problem Statement

Request headers (User-Agent, ClientVersion, ApiVersion) are logged without sanitization. An attacker can inject malicious content like newline characters (`\r\n`) to forge log entries, ANSI escape codes for terminal-based log viewers, or excessively long strings for log flooding/DoS.

## Findings

**Source:** security-sentinel agent

**Affected Files:**
- `src/Framework.Api.Logging.Serilog/ApiSerilogFactory.cs:85-87`

**Current Code:**
```csharp
.Enrich.WithRequestHeader(HttpHeaderNames.UserAgent)
.Enrich.WithRequestHeader(HttpHeaderNames.ClientVersion)
.Enrich.WithRequestHeader(HttpHeaderNames.ApiVersion);
```

**Exploitation Example:**
```http
User-Agent: Mozilla/5.0\n[2024-01-01 00:00:00.000 +00:00 INF] Admin user logged in successfully
```

## Proposed Solutions

### Option 1: Custom Sanitizing Header Enricher (Recommended)
**Pros:** Complete control over sanitization
**Cons:** More code
**Effort:** Medium
**Risk:** Low

```csharp
public static class SanitizedHeaderEnricher
{
    private const int MaxHeaderLength = 512;

    public static LoggerConfiguration WithSanitizedRequestHeader(
        this LoggerEnrichmentConfiguration enricher,
        string headerName)
    {
        return enricher.With(new SanitizedHeaderEnricher(headerName, MaxHeaderLength));
    }
}

// Then use:
.Enrich.WithSanitizedRequestHeader(HttpHeaderNames.UserAgent)
```

### Option 2: Truncate via Destructure Policy
**Pros:** Less invasive change
**Cons:** Only handles length, not control chars
**Effort:** Small
**Risk:** Medium (incomplete protection)

### Option 3: Accept Risk with Documentation
**Pros:** No code change
**Cons:** Leaves vulnerability
**Effort:** None
**Risk:** High

Document that log aggregation systems must handle sanitization.

## Technical Details

**Affected Components:** ApiSerilogFactory, header enrichment
**Attack Surface:** Any HTTP request can include malicious headers

**Headers to Sanitize:**
- Remove newline chars (`\r`, `\n`)
- Remove ANSI escape sequences
- Truncate to reasonable length (512 chars)

## Acceptance Criteria

- [ ] Headers are sanitized before logging
- [ ] Newlines and control characters removed
- [ ] Headers truncated to max 512 chars
- [ ] Tests verify sanitization

## Work Log

| Date | Action | Learnings |
|------|--------|-----------|
| 2026-01-11 | Created from security review | User-controlled input in logs = injection risk |

## Resources

- OWASP Log Injection: https://owasp.org/www-community/attacks/Log_Injection
