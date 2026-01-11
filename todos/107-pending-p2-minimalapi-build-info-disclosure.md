# Build Information Exposed in Error Responses

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, security, information-disclosure, dotnet

---

## Problem Statement

The `ProblemDetailsCreator.Normalize` method adds build information to ALL error responses:

```csharp
// IProblemDetailsCreator.cs lines 185-197
if (!problemDetails.Extensions.ContainsKey("buildNumber"))
{
    problemDetails.Extensions["buildNumber"] = buildInformationAccessor.GetBuildNumber();
}
if (!problemDetails.Extensions.ContainsKey("commitNumber"))
{
    problemDetails.Extensions["commitNumber"] = buildInformationAccessor.GetCommitNumber();
}
```

This results in responses like:
```json
{
  "status": 404,
  "title": "Not Found",
  "buildNumber": "1.2.3",
  "commitNumber": "abc123def"
}
```

**Why it matters:**
- Attackers can identify exact software version
- Known vulnerabilities for specific versions become exploitable
- Aids reconnaissance attacks
- Violates security best practices

---

## Proposed Solutions

### Option A: Remove in Production
```csharp
if (environment.IsDevelopmentOrTest())
{
    problemDetails.Extensions["buildNumber"] = buildInformationAccessor.GetBuildNumber();
    problemDetails.Extensions["commitNumber"] = buildInformationAccessor.GetCommitNumber();
}
```
- **Pros:** Build info available for debugging, hidden in production
- **Cons:** Requires environment awareness in this class
- **Effort:** Small
- **Risk:** Low

### Option B: Make Configurable
```csharp
public class ProblemDetailsOptions
{
    public bool IncludeBuildInfo { get; set; } = false;  // Opt-in
}
```
- **Pros:** User controls behavior
- **Cons:** More configuration
- **Effort:** Medium
- **Risk:** Low

---

## Recommended Action

**Option A** - Only include in development/test environments.

---

## Technical Details

**Affected Files:**
- `src/Framework.Api/Abstractions/IProblemDetailsCreator.cs` (lines 190-197)

---

## Acceptance Criteria

- [ ] Build info not exposed in production responses
- [ ] Build info available in development for debugging

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From code review - security-sentinel |
