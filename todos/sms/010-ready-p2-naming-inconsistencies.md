---
status: ready
priority: p2
issue_id: "010"
tags: [code-review, naming, conventions, sms]
dependencies: []
---

# Naming inconsistencies across SMS providers

## Problem Statement

Several naming inconsistencies exist across SMS provider packages that violate the established patterns.

## Findings

### 1. Setup class naming
- **Pattern:** `{Provider}Setup` (e.g., `AwsSnsSetup`, `CequensSetup`)
- **Violation:** `src/Framework.Sms.Vodafone/Setup.cs:11` uses `Setup` instead of `VodafoneSetup`

### 2. Options class naming
- **Pattern:** `{Provider}SmsOptions` (e.g., `CequensSmsOptions`, `TwilioSmsOptions`)
- **Violation:** `src/Framework.Sms.Infobip/InfobipOptions.cs:8` uses `InfobipOptions` (missing `Sms`)

### 3. Validator attribute inconsistency
- **AWS:** Has `[UsedImplicitly]` on validator (`src/Framework.Sms.Aws/AwsSnsSmsOptions.cs:14`)
- **All others:** Missing the attribute

### 4. HttpClient name inconsistency
- **Cequens/Infobip:** Use named clients (`"cequens-client"`, `"infobip-client"`)
- **Connekio/VictoryLink/Vodafone:** No name on HttpClient registration

## Proposed Solutions

### Option 1: Standardize all naming

**Changes:**
1. Rename `Setup` to `VodafoneSetup` in Vodafone package
2. Rename `InfobipOptions` to `InfobipSmsOptions`
3. Either add `[UsedImplicitly]` to all validators or remove from AWS
4. Either add names to all HttpClient registrations or remove from Cequens/Infobip

**Pros:**
- Consistent API across all providers
- Easier to maintain

**Cons:**
- Breaking changes for `InfobipOptions` rename

**Effort:** 1-2 hours

**Risk:** Medium (breaking change for Infobip options)

## Recommended Action

Fix naming issues. For `InfobipOptions`, consider keeping both names with obsolete attribute on old one for backwards compatibility.

## Technical Details

**Files requiring changes:**
- `src/Framework.Sms.Vodafone/Setup.cs:11` - rename class
- `src/Framework.Sms.Infobip/InfobipOptions.cs:8` - rename class
- All validator classes - add or remove `[UsedImplicitly]`
- Setup.cs files - standardize HttpClient naming

## Acceptance Criteria

- [ ] All Setup classes follow `{Provider}Setup` pattern
- [ ] All Options classes follow `{Provider}SmsOptions` pattern
- [ ] Consistent use of `[UsedImplicitly]` attribute
- [ ] Consider backwards compatibility for renames

## Work Log

### 2026-01-12 - Pattern Recognition Review

**By:** Claude Code

**Actions:**
- Cataloged all naming patterns across providers
- Identified 4 categories of inconsistencies
