---
status: ready
priority: p1
issue_id: "002"
tags: [code-review, security, vodafone, sms, xml-injection]
dependencies: []
---

# XML Injection vulnerability in VodafoneSmsSender

## Problem Statement

`VodafoneSmsSender._BuildPayload` constructs XML via string concatenation without escaping user input. The `request.Text` and `recipients` values are directly embedded, allowing XML injection attacks.

## Findings

- **File:** `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs:71-78`
- **Vulnerable code:**
```csharp
return "<Payload>"
    + $"<AccountId>{_options.AccountId}</AccountId>"
    + $"<Message>{request.Text}</Message>"  // VULNERABLE
    + "</Payload>";
```
- If `request.Text` contains `</Message><Password>leaked</Password><Message>`, the XML structure is broken
- Could potentially inject malicious elements or break parsing

## Proposed Solutions

### Option 1: Use SecurityElement.Escape

**Approach:** Escape all user-provided values before embedding in XML.

```csharp
using System.Security;

return "<Payload>"
    + $"<Message>{SecurityElement.Escape(request.Text)}</Message>"
    + "</Payload>";
```

**Pros:**
- Simple, built-in .NET method
- Handles `<`, `>`, `&`, `'`, `"` characters

**Cons:**
- None significant

**Effort:** 30 minutes

**Risk:** Low

---

### Option 2: Use XElement for proper XML construction

**Approach:** Use `System.Xml.Linq.XElement` for proper XML serialization.

```csharp
var payload = new XElement("Payload",
    new XElement("AccountId", _options.AccountId),
    new XElement("Message", request.Text)
);
return payload.ToString();
```

**Pros:**
- Automatic escaping
- More maintainable
- Validates XML structure

**Cons:**
- Slightly more verbose
- Minor overhead

**Effort:** 1 hour

**Risk:** Low

## Recommended Action

Implement Option 1 for quick fix, or Option 2 for cleaner long-term solution.

## Technical Details

**Affected files:**
- `src/Framework.Sms.Vodafone/VodafoneSmsSender.cs:64-79`

**Security impact:**
- XML structure manipulation
- Potential for injecting unauthorized elements
- May cause parsing failures at Vodafone API

## Acceptance Criteria

- [ ] All user input is properly escaped before XML embedding
- [ ] Special characters (`<`, `>`, `&`, `'`, `"`) are handled correctly
- [ ] Test with SMS text containing XML special characters

## Work Log

### 2026-01-12 - Security Review

**By:** Claude Code

**Actions:**
- Identified XML injection vulnerability
- Confirmed string concatenation without escaping
- Proposed two remediation approaches
