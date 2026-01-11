# Missing BlobServiceClient Registration in Setup

**Date:** 2026-01-11
**Status:** pending
**Priority:** P2 - Important
**Tags:** code-review, architecture, blobs-azure, dependency-injection

---

## Problem Statement

`Setup.cs` registers `AzureBlobStorage` but doesn't register `BlobServiceClient`:

```csharp
private IServiceCollection _AddCore()
{
    services.TryAddSingleton<IBlobNamingNormalizer, AzureBlobNamingNormalizer>();
    services.AddSingleton<IBlobStorage, AzureBlobStorage>();
    return services;  // No BlobServiceClient!
}
```

`AzureBlobStorage` constructor requires `BlobServiceClient`:
```csharp
public AzureBlobStorage(
    BlobServiceClient blobServiceClient,  // Required!
    ...
)
```

**Why it matters:**
- Consumer must know to register `BlobServiceClient` separately
- Undocumented dependency
- Runtime DI failure with confusing error message
- "Pit of failure" instead of "pit of success"

---

## Proposed Solutions

### Option A: Add Connection String to Options
```csharp
public class AzureStorageOptions
{
    public string? ConnectionString { get; set; }
}

// In Setup.cs:
services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value;
    return new BlobServiceClient(options.ConnectionString);
});
```
- **Pros:** Complete setup in one call
- **Cons:** May conflict if user wants custom BlobServiceClient
- **Effort:** Small
- **Risk:** Low

### Option B: Use TryAddSingleton for BlobServiceClient
```csharp
services.TryAddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<AzureStorageOptions>>().Value;
    return new BlobServiceClient(options.ConnectionString);
});
```
- **Pros:** Allows override, doesn't conflict
- **Cons:** Still needs connection string in options
- **Effort:** Small
- **Risk:** Low

### Option C: Document Requirement
Add XML comments and README instructions.
- **Pros:** Non-breaking
- **Cons:** Doesn't solve the problem
- **Effort:** Small
- **Risk:** Low

---

## Recommended Action

**Option B** - Use `TryAddSingleton` so users can override with their own `BlobServiceClient` if needed.

---

## Technical Details

**Affected Files:**
- `src/Framework.Blobs.Azure/Setup.cs`
- `src/Framework.Blobs.Azure/AzureStorageOptions.cs` (add ConnectionString)

---

## Acceptance Criteria

- [ ] `BlobServiceClient` registered by default
- [ ] User can override with custom client
- [ ] Connection string configurable via options
- [ ] Validation fails if no connection string provided

---

## Work Log

| Date | Action | Notes |
|------|--------|-------|
| 2026-01-11 | Created | From architecture review |
