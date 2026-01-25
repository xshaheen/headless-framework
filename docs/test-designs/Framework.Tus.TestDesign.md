# Framework.Tus Test Design

## Overview

The Framework.Tus packages provide TUS (resumable file upload) protocol support with Azure Blob Storage backend. Implements the full tusdotnet store interface including creation, termination, expiration, concatenation, checksum verification, and distributed locking.

### Packages
1. **Framework.Tus** - Base package (appears minimal/shared utilities)
2. **Framework.Tus.Azure** - Azure Blob Storage implementation with block-based uploads
3. **Framework.Tus.ResourceLock** - Distributed locking via IResourceLockProvider

### Key Components
- **TusAzureStore** - Full tusdotnet store implementation for Azure Blob Storage
- **TusAzureMetadata** - Metadata parsing/serialization for TUS protocol
- **AzureBlobFileLock/Provider** - Azure-native blob leasing for file locks
- **ResourceLockTusFileLock/Provider** - Framework's distributed lock integration
- **TusAzureStoreOptions** - Configuration (container name, blob prefix, etc.)

### Existing Tests
Located in `tests/Framework.Tus.Azure.Tests.Integration/`:
- `TusAzureStoreTests.cs` - ~18 comprehensive integration tests

**Total existing: ~18 integration tests**

---

## 1. Framework.Tus.Azure

### 1.1 TusAzureStore - ITusCreationStore Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 1 | should_create_file_with_valid_upload_length | Integration | CreateFileAsync with positive length (existing) |
| 2 | should_create_file_with_metadata | Integration | Metadata preserved in blob properties (existing) |
| 3 | should_set_date_created_on_create | Integration | DateCreated metadata set (existing) |
| 4 | should_generate_unique_file_id | Integration | FileIdProvider creates unique IDs |
| 5 | should_use_custom_file_id_provider | Unit | Custom ITusFileIdProvider injection |
| 6 | should_set_blob_http_headers | Integration | Content-Type from ITusAzureBlobHttpHeadersProvider |
| 7 | should_log_creation_info | Unit | Logger.LogInformation on success |
| 8 | should_log_error_on_creation_failure | Unit | Logger.LogError on exception |
| 9 | should_get_upload_metadata | Integration | GetUploadMetadataAsync (existing) |
| 10 | should_return_null_metadata_for_nonexistent | Integration | GetUploadMetadataAsync returns null |

### 1.2 TusAzureStore - ITusCreationDeferLengthStore Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 11 | should_create_file_with_deferred_length | Integration | CreateFileAsync with -1 length |
| 12 | should_set_upload_length | Integration | SetUploadLengthAsync (existing) |
| 13 | should_update_metadata_on_set_length | Integration | Blob metadata updated |

### 1.3 TusAzureStore - Core Store Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 14 | should_check_file_exists_true | Integration | FileExistAsync returns true (existing) |
| 15 | should_check_file_exists_false | Integration | FileExistAsync returns false (existing) |
| 16 | should_handle_404_on_file_exist | Integration | RequestFailedException handling |
| 17 | should_get_upload_length | Integration | GetUploadLengthAsync (existing) |
| 18 | should_return_null_length_for_nonexistent | Integration | GetUploadLengthAsync returns null (existing) |
| 19 | should_get_upload_offset_from_committed_blocks | Integration | GetUploadOffsetAsync sums block sizes (existing) |
| 20 | should_return_zero_offset_for_nonexistent | Integration | GetUploadOffsetAsync returns 0 (existing) |
| 21 | should_handle_404_on_get_block_list | Integration | Empty list on missing blob |

### 1.4 TusAzureStore - ITusStore (Stream) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 22 | should_append_data_from_stream | Integration | AppendDataAsync with MemoryStream (existing) |
| 23 | should_throw_for_nonexistent_file_on_append | Integration | InvalidOperationException (existing) |
| 24 | should_stage_and_commit_blocks | Integration | Azure block blob pattern |
| 25 | should_calculate_correct_bytes_appended | Integration | Return value matches written bytes |
| 26 | should_handle_multiple_append_operations | Integration | Sequential uploads |

### 1.5 TusAzureStore - ITusPipelineStore Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 27 | should_append_data_from_pipe_reader | Integration | AppendDataAsync with PipeReader (existing) |
| 28 | should_throw_for_nonexistent_file_on_pipe_append | Integration | InvalidOperationException (existing) |
| 29 | should_read_from_readonly_sequence_stream | Unit | ReadOnlySequenceStream wrapper |

### 1.6 TusAzureStore - ITusReadableStore Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 30 | should_get_file_by_id | Integration | GetFileAsync returns ITusFile (existing) |
| 31 | should_return_null_for_nonexistent_file | Integration | GetFileAsync returns null (existing) |
| 32 | should_return_file_with_correct_metadata | Integration | ITusFile metadata dictionary (existing) |

### 1.7 TusAzureStore - ITusTerminationStore Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 33 | should_delete_file | Integration | DeleteFileAsync removes blob (existing) |
| 34 | should_not_throw_for_nonexistent_delete | Integration | Idempotent delete (existing) |
| 35 | should_handle_404_on_delete | Integration | RequestFailedException handling |

### 1.8 TusAzureStore - ITusExpirationStore Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 36 | should_set_expiration | Integration | SetExpirationAsync (existing) |
| 37 | should_get_expiration | Integration | GetExpirationAsync (existing) |
| 38 | should_return_null_expiration_for_nonexistent | Integration | GetExpirationAsync returns null (existing) |
| 39 | should_get_expired_files | Integration | GetExpiredFilesAsync (existing) |
| 40 | should_not_include_non_expired_files | Integration | Filtering by expiration time (existing) |
| 41 | should_remove_expired_files | Integration | RemoveExpiredFilesAsync |

### 1.9 TusAzureStore - ITusChecksumStore Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 42 | should_return_supported_algorithms | Integration | sha1, sha256, sha512, md5 (existing) |
| 43 | should_return_false_without_checksum_metadata | Integration | VerifyChecksumAsync (existing) |
| 44 | should_return_false_for_nonexistent_checksum | Integration | Graceful handling (existing) |
| 45 | should_verify_valid_checksum | Integration | Two-phase commit with tusdotnet |
| 46 | should_reject_invalid_checksum | Integration | Checksum mismatch handling |

### 1.10 TusAzureStore - ITusConcatenationStore Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 47 | should_create_partial_file | Integration | CreatePartialFileAsync |
| 48 | should_create_final_file_from_partials | Integration | CreateFinalFileAsync |
| 49 | should_set_concatenation_metadata | Integration | Partial/final markers |
| 50 | should_get_partial_file_info | Integration | GetUploadConcatAsync |

### 1.11 TusAzureStore - Initialization Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 51 | should_create_container_if_not_exists | Integration | CreateContainerIfNotExists option |
| 52 | should_not_create_container_when_disabled | Unit | Skip container creation |
| 53 | should_log_initialization_info | Unit | Logger on container init |
| 54 | should_rethrow_on_init_failure | Unit | Exception propagation |
| 55 | should_apply_public_access_type | Integration | ContainerPublicAccessType option |

### 1.12 TusAzureStoreOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 56 | should_require_container_name | Unit | Required property validation |
| 57 | should_default_blob_prefix_to_empty | Unit | Default value |
| 58 | should_default_create_container_to_true | Unit | Default value |
| 59 | should_default_public_access_type | Unit | Default value |

### 1.13 TusAzureMetadata Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 60 | should_parse_tus_metadata_string | Unit | FromTus parsing |
| 61 | should_handle_base64_encoded_values | Unit | Base64 decoding |
| 62 | should_convert_to_azure_dictionary | Unit | ToAzure conversion |
| 63 | should_convert_to_user_dictionary | Unit | ToUser conversion |
| 64 | should_convert_to_tus_string | Unit | ToTusString conversion |
| 65 | should_set_date_created | Unit | DateCreated property |
| 66 | should_set_upload_length | Unit | UploadLength property |
| 67 | should_set_expiration | Unit | Expiration property |

### 1.14 TusAzureFile/Wrapper Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 68 | should_create_from_blob_properties | Unit | FromBlobProperties factory |
| 69 | should_expose_file_id | Unit | Id property |
| 70 | should_expose_blob_name | Unit | BlobName property |
| 71 | should_get_metadata_dictionary | Unit | GetMetadataAsync |
| 72 | should_get_content_stream | Unit | GetContentAsync |

### 1.15 AzureBlobFileLock Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 73 | should_acquire_blob_lease | Integration | Lock returns true |
| 74 | should_return_false_when_lease_taken | Integration | Concurrent lock attempt |
| 75 | should_release_lease_when_held | Integration | ReleaseIfHeld cleanup |
| 76 | should_handle_release_when_no_lease | Unit | Safe release when not locked |

### 1.16 AzureBlobFileLockProvider Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 77 | should_create_file_lock_for_id | Unit | AquireLock factory |
| 78 | should_use_correct_blob_client | Unit | Blob path construction |

### 1.17 TusAzureCacheControlProvider Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 79 | should_return_blob_http_headers | Unit | GetBlobHttpHeadersAsync |
| 80 | should_set_content_type_from_metadata | Unit | Content-Type mapping |
| 81 | should_set_cache_control | Unit | Cache-Control header |

### 1.18 ReadOnlySequenceStream Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 82 | should_read_from_sequence | Unit | Stream read operations |
| 83 | should_report_correct_length | Unit | Length property |
| 84 | should_report_correct_position | Unit | Position property |
| 85 | should_not_support_write | Unit | Write throws NotSupportedException |

---

## 2. Framework.Tus.ResourceLock

### 2.1 ResourceLockTusFileLock Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 86 | should_acquire_lock_via_provider | Unit | Lock returns true on success |
| 87 | should_return_false_when_lock_unavailable | Unit | Lock returns false on failure |
| 88 | should_use_correct_resource_name | Unit | "tus-file-lock-{fileId}" format |
| 89 | should_use_infinite_expiration | Unit | timeUntilExpires = Timeout.InfiniteTimeSpan |
| 90 | should_use_zero_acquire_timeout | Unit | acquireTimeout = TimeSpan.Zero |
| 91 | should_release_lock_when_held | Unit | ReleaseIfHeld calls provider |
| 92 | should_noop_release_when_not_held | Unit | Safe release when null |

### 2.2 ResourceLockTusLockProvider Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 93 | should_create_file_lock | Unit | AquireLock returns ITusFileLock |
| 94 | should_pass_file_id_to_lock | Unit | FileId forwarded correctly |
| 95 | should_use_injected_provider | Unit | IResourceLockProvider dependency |

### 2.3 Setup Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 96 | should_register_lock_provider | Unit | DI registration |

---

## 3. Integration Tests (Cross-Component)

### 3.1 Full Upload Flow Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 97 | should_complete_small_file_upload | Integration | Create → Append → Verify |
| 98 | should_resume_interrupted_upload | Integration | Partial upload → Continue |
| 99 | should_handle_concurrent_uploads | Integration | Multiple file uploads |
| 100 | should_handle_large_file_chunking | Integration | Multiple blocks |
| 101 | should_handle_checksum_verification_flow | Integration | Upload with checksum |

### 3.2 Locking Integration Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 102 | should_prevent_concurrent_file_access | Integration | Lock blocking |
| 103 | should_release_lock_after_upload | Integration | Lock cleanup |
| 104 | should_handle_lock_timeout | Integration | Failed lock acquisition |

### 3.3 Expiration Cleanup Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 105 | should_cleanup_expired_uploads | Integration | Full expiration flow |
| 106 | should_not_delete_active_uploads | Integration | Safety check |

---

## Summary

| Category | Unit Tests | Integration Tests | Total |
|----------|------------|-------------------|-------|
| TusAzureStore (Creation) | 4 | 6 | 10 |
| TusAzureStore (Core) | 1 | 7 | 8 |
| TusAzureStore (Stream/Pipe) | 1 | 7 | 8 |
| TusAzureStore (Readable) | 0 | 3 | 3 |
| TusAzureStore (Termination) | 0 | 3 | 3 |
| TusAzureStore (Expiration) | 0 | 6 | 6 |
| TusAzureStore (Checksum) | 0 | 5 | 5 |
| TusAzureStore (Concatenation) | 0 | 4 | 4 |
| TusAzureStore (Initialization) | 3 | 2 | 5 |
| Options/Metadata/Models | 16 | 0 | 16 |
| Azure Lock | 2 | 3 | 5 |
| ResourceLock | 11 | 0 | 11 |
| Full Upload Flows | 0 | 5 | 5 |
| Locking Integration | 0 | 3 | 3 |
| Expiration Integration | 0 | 2 | 2 |
| **Total** | **38** | **56** | **94** |

### Test Distribution
- **Unit tests**: 38 (mock-based, no Azure required)
- **Integration tests**: 56 (requires Azurite container)
- **Existing tests**: ~18 integration tests (comprehensive coverage)
- **Missing tests**: ~76

### Test Project Structure
```
tests/
├── Framework.Tus.Tests.Unit/                    (NEW - 38 tests)
│   ├── Azure/
│   │   ├── TusAzureMetadataTests.cs
│   │   ├── TusAzureFileTests.cs
│   │   ├── TusAzureStoreOptionsTests.cs
│   │   ├── ReadOnlySequenceStreamTests.cs
│   │   └── TusAzureCacheControlProviderTests.cs
│   └── ResourceLock/
│       ├── ResourceLockTusFileLockTests.cs
│       └── ResourceLockTusLockProviderTests.cs
└── Framework.Tus.Azure.Tests.Integration/       (EXISTING - expand)
    ├── TusAzureStoreTests.cs                    (existing ~18 tests)
    ├── TestSetup/
    │   └── AzureBlobTestFixture.cs              (existing)
    ├── Locks/
    │   └── AzureBlobFileLockTests.cs            (new)
    ├── Flows/
    │   ├── FullUploadFlowTests.cs               (new)
    │   └── ExpirationCleanupTests.cs            (new)
    └── Concatenation/
        └── TusConcatenationTests.cs             (new)
```

### Key Testing Considerations

1. **Block Blob Pattern**: Azure Blob Storage uses staged blocks that are committed together. Tests should verify block staging, listing, and committing behavior.

2. **TUS Metadata Format**: The TUS protocol uses a specific metadata format (`key base64value, key2 base64value2`). Tests must verify correct parsing and serialization.

3. **Checksum Two-Phase Commit**: Checksum verification requires tusdotnet's internal flow - unit tests can only verify API behavior, not full E2E checksum validation.

4. **Locking Semantics**: The ResourceLock adapter uses infinite expiration and zero acquire timeout - tests must verify these exact semantics.

5. **Azurite for Integration**: Integration tests require Azurite (Azure Storage emulator) via Testcontainers.

6. **Existing Test Coverage**: The existing 18 tests provide solid coverage of core functionality. Focus new tests on:
   - Unit tests for metadata/model classes
   - Concatenation store (partial/final file support)
   - Lock provider integration
   - Edge cases and error handling
