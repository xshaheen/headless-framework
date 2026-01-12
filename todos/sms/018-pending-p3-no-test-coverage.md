---
status: pending
priority: p3
issue_id: "018"
tags: [code-review, testing, sms]
dependencies: []
---

# No test coverage for SMS providers

## Problem Statement

No unit or integration tests exist for any SMS provider implementation. This makes it difficult to verify correctness and catch regressions.

## Findings

- **Search:** `tests/**/Framework.Sms*` - no files found
- **Impact:**
  - Bugs like the Twilio phone number issue went undetected
  - No verification of error handling paths
  - No validation that providers work with their APIs

## Proposed Solutions

### Option 1: Add unit tests with mocks

**Approach:** Create unit tests using mocked HttpClient and SDK clients.

**Test areas:**
- Input validation
- Success paths
- Error handling
- Batch vs single sending logic
- Options validation

**Pros:**
- Fast execution
- No external dependencies
- Covers logic paths

**Cons:**
- Doesn't verify actual API compatibility

**Effort:** 4-8 hours per provider

**Risk:** Low

---

### Option 2: Add integration tests (Testcontainers)

**Approach:** Where possible, use Testcontainers or provider sandboxes.

**Pros:**
- Verifies actual API compatibility

**Cons:**
- Slower execution
- Some providers may not have sandboxes

**Effort:** 8-16 hours

**Risk:** Medium

## Recommended Action

Start with unit tests (Option 1) for immediate coverage, then add integration tests for critical providers.

## Technical Details

**Test files to create:**
- `tests/Framework.Sms.Aws.Tests.Unit/`
- `tests/Framework.Sms.Cequens.Tests.Unit/`
- `tests/Framework.Sms.Connekio.Tests.Unit/`
- `tests/Framework.Sms.Dev.Tests.Unit/`
- `tests/Framework.Sms.Infobip.Tests.Unit/`
- `tests/Framework.Sms.Twilio.Tests.Unit/`
- `tests/Framework.Sms.VictoryLink.Tests.Unit/`
- `tests/Framework.Sms.Vodafone.Tests.Unit/`

**Priority order for testing:**
1. DevSmsSender (simplest, good baseline)
2. TwilioSmsSender (verify bug fix)
3. HTTP-based providers

## Acceptance Criteria

- [ ] Unit tests for all providers
- [ ] Coverage > 80% for sender implementations
- [ ] Tests follow naming convention: `should_{action}_{expected}_when_{condition}`

## Work Log

### 2026-01-12 - Architecture Review

**By:** Claude Code

**Actions:**
- Confirmed no test files exist
- Outlined test strategy
