# Silent Decryption Failure Returns Null

**Date:** 2026-01-10
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, security, dotnet, settings, encryption

---

## Problem Statement

In `ISettingEncryptionService.cs` (lines 40-56), decryption failure returns null silently:

```csharp
public string? Decrypt(SettingDefinition settingDefinition, string? encryptedValue)
{
    try
    {
        return stringEncryptionService.Decrypt(encryptedValue);
    }
    catch (Exception e)
    {
        logger.LogWarning(e, "Failed to decrypt setting value: {SettingDefinition}", settingDefinition.Name);
        return null;  // Silent failure!
    }
}
```

But `Encrypt` throws on failure.

**Why it matters:**
- Asymmetric behavior is confusing
- Corrupted encrypted values return null (looks like setting doesn't exist)
- Key rotation issues silently mask settings
- Security bypass if encrypted setting controls access

---

## Proposed Solutions

### Option A: Throw SettingDecryptionException
```csharp
catch (Exception e)
{
    logger.LogError(e, "Failed to decrypt...");
    throw new SettingDecryptionException(settingDefinition.Name, e);
}
```
- **Pros:** Consistent with Encrypt, fails fast
- **Cons:** Breaking change, consumers must handle
- **Effort:** Small
- **Risk:** Medium

### Option B: Return DecryptResult with Success/Failure
```csharp
public DecryptResult Decrypt(...)
{
    // return DecryptResult.Success(value) or DecryptResult.Failure(reason)
}
```
- **Pros:** Explicit failure handling
- **Cons:** API change, more complex
- **Effort:** Medium
- **Risk:** Medium

### Option C: Log at ERROR Level, Document Behavior
- **Pros:** Non-breaking
- **Cons:** Silent failure continues
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option A** - Throw exception. Decrypt failures should be visible, not masked.

---

## Technical Details

**Affected Files:**
- `src/Framework.Settings.Core/Helpers/ISettingEncryptionService.cs` (lines 40-56)

---

## Acceptance Criteria

- [ ] Decrypt throws exception on failure
- [ ] Add SettingDecryptionException type
- [ ] Log at ERROR level
- [ ] Document in migration guide
- [ ] Update consumers to handle exception

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-10 | Created | From security review |
