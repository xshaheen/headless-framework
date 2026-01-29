# Test Case Design: Headless.Api.DataProtection

**Package:** `src/Headless.Api.DataProtection`
**Test Projects:** None existing - **needs creation**
**Generated:** 2026-01-25

## Package Analysis

| File | Purpose | Testable |
|------|---------|----------|
| `BlobStorageDataProtectionXmlRepository.cs` | IXmlRepository backed by blob storage | High |
| `DataProtectionBuilderExtensions.cs` | DI registration extensions | Medium |

---

## 1. BlobStorageDataProtectionXmlRepository Tests

**File:** `tests/Headless.Api.DataProtection.Tests.Unit/BlobStorageDataProtectionXmlRepositoryTests.cs`

### Constructor Tests

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_storage_null` | ArgumentNullException for null IBlobStorage |
| `should_use_null_logger_when_factory_null` | Works without logger factory |
| `should_create_logger_from_factory` | Logger created when factory provided |

### GetAllElements Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_return_empty_when_no_files` | No .xml files in storage | Empty collection |
| `should_return_all_xml_elements` | 3 xml files | 3 XElements |
| `should_skip_files_that_fail_to_download` | 1 of 3 fails | 2 XElements returned |
| `should_load_elements_from_DataProtection_container` | Any files | Uses "DataProtection" container |
| `should_filter_by_xml_extension` | Mixed files | Only *.xml loaded |
| `should_parse_xml_content_correctly` | Valid XML | XElement matches content |
| `should_log_trace_when_loading` | Any call | Trace logged |
| `should_log_warning_when_download_fails` | Download returns null | Warning logged |

### StoreElement Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_throw_when_element_null` | null element | ArgumentNullException |
| `should_use_friendly_name_for_filename` | friendlyName="key-123" | "key-123.xml" |
| `should_generate_guid_when_no_friendly_name` | friendlyName=null | "{guid}.xml" |
| `should_generate_guid_when_empty_friendly_name` | friendlyName="" | "{guid}.xml" |
| `should_upload_to_DataProtection_container` | Any call | "DataProtection" container |
| `should_save_xml_without_formatting` | Any element | SaveOptions.DisableFormatting |
| `should_retry_on_IOException` | 1st call fails with IOException | Retries succeed |
| `should_retry_up_to_4_times` | All calls fail | 4 retries attempted |
| `should_use_exponential_backoff` | Retries occur | Delay increases |
| `should_log_trace_before_and_after_save` | Any call | 2 trace logs |

### Thread Safety Tests

| Test Case | Description |
|-----------|-------------|
| `should_handle_concurrent_GetAllElements_calls` | Multiple parallel reads |
| `should_handle_concurrent_StoreElement_calls` | Multiple parallel writes |
| `should_handle_concurrent_read_and_write` | Read during write |

### Integration Considerations

| Test Case | Description |
|-----------|-------------|
| `should_round_trip_xml_element` | Store then retrieve same element |
| `should_preserve_xml_structure` | Complex XML preserved |

---

## 2. DataProtectionBuilderExtensions Tests

**File:** `tests/Headless.Api.DataProtection.Tests.Unit/DataProtectionBuilderExtensionsTests.cs`

### PersistKeysToBlobStorage(storage, loggerFactory) Tests

| Test Case | Description |
|-----------|-------------|
| `should_configure_XmlRepository_with_storage` | Repository uses provided storage |
| `should_pass_logger_factory_to_repository` | LoggerFactory passed through |
| `should_work_without_logger_factory` | null loggerFactory accepted |
| `should_return_builder_for_chaining` | Returns same builder |

### PersistKeysToBlobStorage(storageFactory) Tests

| Test Case | Description |
|-----------|-------------|
| `should_resolve_storage_from_factory` | Factory invoked with IServiceProvider |
| `should_resolve_logger_factory_from_services` | ILoggerFactory resolved |
| `should_register_as_singleton` | Single registration |

### PersistKeysToBlobStorage() (parameterless) Tests

| Test Case | Description |
|-----------|-------------|
| `should_resolve_storage_from_di` | IBlobStorage resolved |
| `should_throw_when_storage_not_registered` | InvalidOperationException |
| `should_resolve_logger_factory_from_di` | ILoggerFactory resolved (optional) |

---

## Test Infrastructure

### Mock IBlobStorage

```csharp
public class MockBlobStorage : IBlobStorage
{
    private readonly Dictionary<string, byte[]> _blobs = new();

    public Task<IReadOnlyList<BlobInfo>> GetBlobsListAsync(
        string[] containers, string pattern, CancellationToken ct = default)
    {
        var results = _blobs.Keys
            .Where(k => k.EndsWith(".xml"))
            .Select(k => new BlobInfo { BlobKey = k })
            .ToList();
        return Task.FromResult<IReadOnlyList<BlobInfo>>(results);
    }

    public Task<BlobDownloadResult?> OpenReadStreamAsync(
        string[] containers, string blobKey, CancellationToken ct = default)
    {
        if (_blobs.TryGetValue(blobKey, out var data))
        {
            return Task.FromResult<BlobDownloadResult?>(
                new BlobDownloadResult(new MemoryStream(data)));
        }
        return Task.FromResult<BlobDownloadResult?>(null);
    }

    public Task UploadAsync(
        string[] containers, BlobUploadInfo info, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        info.Stream.CopyTo(ms);
        _blobs[info.BlobKey] = ms.ToArray();
        return Task.CompletedTask;
    }

    // Helper for tests
    public void AddBlob(string key, XElement element)
    {
        using var ms = new MemoryStream();
        element.Save(ms);
        _blobs[key] = ms.ToArray();
    }
}
```

### Test Helpers

```csharp
public static class DataProtectionTestHelpers
{
    public static XElement CreateTestElement(string id = "test-key")
    {
        return new XElement("key",
            new XAttribute("id", id),
            new XElement("creationDate", DateTime.UtcNow),
            new XElement("encryptedKey", "base64-data-here"));
    }

    public static IBlobStorage CreateFailingStorage(int failCount)
    {
        var storage = Substitute.For<IBlobStorage>();
        var callCount = 0;
        storage.UploadAsync(Arg.Any<string[]>(), Arg.Any<BlobUploadInfo>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (++callCount <= failCount)
                    throw new IOException("Simulated failure");
                return Task.CompletedTask;
            });
        return storage;
    }
}
```

---

## Test Summary

| Component | Test Count | Priority |
|-----------|------------|----------|
| BlobStorageDataProtectionXmlRepository | 20 | High |
| DataProtectionBuilderExtensions | 10 | Medium |
| **Total** | **30** | - |

---

## Integration Tests

For full integration testing with real blob storage:

**File:** `tests/Headless.Api.DataProtection.Tests.Integration/BlobStorageDataProtectionIntegrationTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_persist_and_retrieve_keys_with_real_storage` | Full round-trip with Testcontainers |
| `should_work_with_azure_blob_storage` | Azure Blob integration |
| `should_work_with_aws_s3` | S3 integration |
| `should_work_with_filesystem_storage` | File system integration |

**Infrastructure:** Use Testcontainers for Azurite (Azure emulator) or MinIO (S3-compatible).

---

## Priority

**Medium Priority** - Data protection key persistence is important for distributed deployments, but the package is small and well-defined.

## Notes

1. The `Async.RunSync` pattern is used because `IXmlRepository` interface is synchronous
2. Retry logic uses Polly with exponential backoff (200ms base, 4 retries)
3. Only `IOException` triggers retry - other exceptions propagate
4. Container name is hardcoded to "DataProtection"
