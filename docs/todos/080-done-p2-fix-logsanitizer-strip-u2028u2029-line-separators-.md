---
status: done
priority: p2
issue_id: "080"
tags: ["code-review","security","messaging","log-injection"]
dependencies: []
---

# Fix LogSanitizer: strip \u2028/\u2029 line separators and lone surrogates to prevent log injection

## Problem Statement

LogSanitizer.Sanitize does not strip U+2028 (Line Separator) and U+2029 (Paragraph Separator), which char.IsControl returns false for. These characters cause line-splitting in structured log sinks (Splunk, Datadog, Elasticsearch), enabling log-line injection by crafting group names. Additionally the truncation fast-path (needsSanitization=false, needsTruncation=true) computes maxLength-truncationSuffix.Length which is negative when maxLength < 3, causing ArgumentOutOfRangeException.

## Findings

- **Location:** src/Headless.Messaging.Core/Internal/LogSanitizer.cs:36-58
- **Missing chars:** \u2028, \u2029, lone surrogates \uD800-\uDFFF
- **Truncation bug:** maxLength < 3 causes negative span length in fast path
- **Discovered by:** security-sentinel

## Proposed Solutions

### Extend strip set and add truncation precondition
- **Pros**: Complete fix for both issues
- **Cons**: Minor code addition
- **Effort**: Small
- **Risk**: Low


## Recommended Action

Extend the sanitization char check to include `c is '\u2028' or '\u2029'` and lone surrogates. Add guard `if (maxLength < truncationSuffix.Length) throw ArgumentOutOfRangeException(nameof(maxLength))` or clamp to 0.

## Acceptance Criteria

- [ ] \u2028 and \u2029 are stripped/replaced in output
- [ ] Lone surrogates \uD800-\uDFFF are stripped/replaced
- [ ] maxLength < 3 does not throw in truncation-only fast path
- [ ] Unit tests for all new sanitization cases

## Notes

PR #194 code review finding.

## Work Log

### 2026-03-23 - Created

**By:** Agent
**Actions:**
- Created via todo.sh create --stdin

### 2026-03-23 - Approved

**By:** Triage Agent
**Actions:**
- Status changed: pending → ready

### 2026-03-23 - Completed

**By:** Agent
**Actions:**
- Status changed: in-progress → done
