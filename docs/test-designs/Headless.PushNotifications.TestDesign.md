# Headless.PushNotifications Test Design

## Overview

The Headless.PushNotifications packages provide push notification services with Firebase Cloud Messaging (FCM) support. Features include automatic retry with exponential backoff for transient errors, batch sending with chunking (500 tokens per batch), and proper error classification.

### Packages
1. **Headless.PushNotifications.Abstractions** - Interfaces and response models
2. **Headless.PushNotifications.Firebase** - FCM implementation with Polly resilience
3. **Headless.PushNotifications.Dev** - No-op implementation for development/testing

### Key Components
- **IPushNotificationService** - Core interface for sending notifications
- **FcmPushNotificationService** - Firebase implementation with retry policies
- **NoopPushNotificationService** - Development stub that always succeeds
- **RetryHelper** - Transient error detection and Retry-After parsing
- **FirebaseOptions/FirebaseRetryOptions** - Configuration with validation

### Existing Tests
**No existing tests found**

---

## 1. Headless.PushNotifications.Abstractions

### 1.1 PushNotificationResponse Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 1 | should_create_succeeded_response | Unit | Succeeded factory creates Success status |
| 2 | should_require_token_for_succeeded | Unit | Argument validation for null/empty token |
| 3 | should_require_message_id_for_succeeded | Unit | Argument validation for null/empty messageId |
| 4 | should_set_message_id_on_success | Unit | MessageId populated on success |
| 5 | should_create_failed_response | Unit | Failed factory creates Failure status |
| 6 | should_require_token_for_failed | Unit | Argument validation for null/empty token |
| 7 | should_require_failure_error_for_failed | Unit | Argument validation for null/empty error |
| 8 | should_set_failure_error_on_failure | Unit | FailureError populated on failure |
| 9 | should_create_unregistered_response | Unit | Unregistered factory creates Unregistered status |
| 10 | should_require_token_for_unregistered | Unit | Argument validation for null/empty token |
| 11 | should_not_set_message_id_on_unregistered | Unit | MessageId null for unregistered |
| 12 | should_return_true_from_is_succeeded_when_success | Unit | IsSucceeded() returns true |
| 13 | should_return_false_from_is_succeeded_when_failure | Unit | IsSucceeded() returns false |
| 14 | should_return_true_from_is_failed_when_failure | Unit | IsFailed() returns true |
| 15 | should_return_false_from_is_failed_when_success | Unit | IsFailed() returns false |

### 1.2 BatchPushNotificationResponse Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 16 | should_set_success_count | Unit | SuccessCount property |
| 17 | should_set_failure_count | Unit | FailureCount property |
| 18 | should_contain_individual_responses | Unit | Responses list property |
| 19 | should_handle_empty_responses_list | Unit | Empty list handling |

### 1.3 PushNotificationResponseStatus Enum Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 20 | should_have_unregistered_value | Unit | Enum value exists |
| 21 | should_have_success_value | Unit | Enum value exists |
| 22 | should_have_failure_value | Unit | Enum value exists |

---

## 2. Headless.PushNotifications.Firebase

### 2.1 FcmPushNotificationService - SendToDeviceAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 23 | should_throw_when_client_token_null | Unit | ArgumentException for null token |
| 24 | should_throw_when_client_token_whitespace | Unit | ArgumentException for whitespace token |
| 25 | should_throw_when_title_null | Unit | ArgumentException for null title |
| 26 | should_throw_when_title_whitespace | Unit | ArgumentException for whitespace title |
| 27 | should_throw_when_body_null | Unit | ArgumentException for null body |
| 28 | should_throw_when_body_whitespace | Unit | ArgumentException for whitespace body |
| 29 | should_throw_when_title_exceeds_100_chars | Unit | Max title length validation |
| 30 | should_throw_when_body_exceeds_4000_chars | Unit | Max body length validation |
| 31 | should_throw_when_data_contains_from_key | Unit | Reserved word 'from' validation |
| 32 | should_throw_when_data_contains_notification_key | Unit | Reserved word 'notification' validation |
| 33 | should_throw_when_data_contains_message_type_key | Unit | Reserved word 'message_type' validation |
| 34 | should_allow_null_data | Unit | Data parameter is optional |
| 35 | should_return_succeeded_on_fcm_success | Integration | Happy path with real FCM |
| 36 | should_return_unregistered_for_invalid_token | Integration | Unregistered MessagingErrorCode |
| 37 | should_return_failed_for_fcm_error | Integration | Other FCM errors |
| 38 | should_log_failure_with_masked_token | Unit | Token truncated to first 8 chars |
| 39 | should_configure_android_high_priority | Unit | AndroidConfig.Priority = High |
| 40 | should_configure_apns_badge_1 | Unit | ApnsConfig.Aps.Badge = 1 |
| 41 | should_retry_on_transient_error | Unit | Polly retry invoked |
| 42 | should_use_retry_pipeline_from_provider | Unit | ResiliencePipelineProvider usage |
| 43 | should_return_resilience_context_to_pool | Unit | Proper context cleanup |

### 2.2 FcmPushNotificationService - SendMulticastAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 44 | should_throw_when_client_tokens_null | Unit | ArgumentException for null |
| 45 | should_throw_when_client_tokens_empty | Unit | ArgumentException for empty |
| 46 | should_validate_title_length | Unit | Max 100 chars validation |
| 47 | should_validate_body_length | Unit | Max 4000 chars validation |
| 48 | should_validate_reserved_words_in_data | Unit | Reserved word validation |
| 49 | should_batch_tokens_in_chunks_of_500 | Unit | _MaxTokensPerBatch = 500 |
| 50 | should_process_single_batch_under_500 | Unit | Single batch path |
| 51 | should_process_multiple_batches_over_500 | Unit | Multi-batch path |
| 52 | should_aggregate_success_count_across_batches | Unit | SuccessCount accumulation |
| 53 | should_aggregate_failure_count_across_batches | Unit | FailureCount accumulation |
| 54 | should_map_individual_responses_to_tokens | Unit | Response-to-token mapping |
| 55 | should_return_succeeded_for_successful_tokens | Integration | IsSuccess = true handling |
| 56 | should_return_unregistered_for_unregistered_tokens | Integration | Unregistered handling |
| 57 | should_return_failed_for_failed_tokens | Integration | Failure handling |
| 58 | should_throw_when_response_count_mismatches | Unit | Sanity check on FCM response |
| 59 | should_throw_and_log_on_batch_error | Unit | Exception propagation with logging |
| 60 | should_configure_android_high_priority_for_batch | Unit | AndroidConfig in batch |
| 61 | should_configure_apns_badge_for_batch | Unit | ApnsConfig in batch |

### 2.3 RetryHelper Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 62 | should_return_true_for_quota_exceeded | Unit | QuotaExceeded is transient |
| 63 | should_return_true_for_unavailable | Unit | Unavailable is transient |
| 64 | should_return_true_for_internal | Unit | Internal is transient |
| 65 | should_return_false_for_unregistered | Unit | Unregistered is permanent |
| 66 | should_return_false_for_invalid_argument | Unit | InvalidArgument is permanent |
| 67 | should_return_false_for_sender_id_mismatch | Unit | SenderIdMismatch is permanent |
| 68 | should_return_false_for_third_party_auth_error | Unit | ThirdPartyAuthError is permanent |
| 69 | should_return_default_delay_when_no_retry_after | Unit | Missing header uses default |
| 70 | should_extract_delay_from_retry_after_delta | Unit | Delta-seconds format |
| 71 | should_extract_delay_from_retry_after_date | Unit | HTTP-date format |
| 72 | should_return_default_when_date_in_past | Unit | Past date uses default |
| 73 | should_return_default_for_null_http_response | Unit | Null safety |

### 2.4 FirebaseOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 74 | should_require_json_property | Unit | Required init property |
| 75 | should_initialize_retry_with_defaults | Unit | Default FirebaseRetryOptions |
| 76 | should_redact_json_in_tostring | Unit | ToString returns REDACTED |
| 77 | should_have_json_ignore_on_json_property | Unit | Security: no serialization |

### 2.5 FirebaseRetryOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 78 | should_default_max_attempts_to_5 | Unit | Default value |
| 79 | should_allow_max_attempts_0 | Unit | Disable retry |
| 80 | should_allow_max_attempts_10 | Unit | Max allowed value |
| 81 | should_throw_when_max_attempts_negative | Unit | Lower bound validation |
| 82 | should_throw_when_max_attempts_over_10 | Unit | Upper bound validation |
| 83 | should_default_max_delay_to_1_minute | Unit | Default value |
| 84 | should_throw_when_max_delay_over_5_minutes | Unit | Upper bound validation |
| 85 | should_default_rate_limit_delay_to_60_seconds | Unit | Default value |
| 86 | should_throw_when_rate_limit_delay_over_5_minutes | Unit | Upper bound validation |
| 87 | should_default_use_jitter_to_true | Unit | Default value |
| 88 | should_allow_use_jitter_false | Unit | Can disable jitter |

### 2.6 FirebaseOptionsValidator Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 89 | should_fail_when_json_empty | Unit | Required validation |
| 90 | should_fail_when_max_attempts_invalid | Unit | Range validation |
| 91 | should_fail_when_max_delay_under_1_second | Unit | Lower bound |
| 92 | should_fail_when_max_delay_over_5_minutes | Unit | Upper bound |
| 93 | should_fail_when_rate_limit_delay_under_1_second | Unit | Lower bound |
| 94 | should_fail_when_rate_limit_delay_over_5_minutes | Unit | Upper bound |
| 95 | should_pass_with_valid_options | Unit | Valid configuration |

### 2.7 Setup Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 96 | should_register_fcm_service_as_singleton | Unit | DI registration |
| 97 | should_configure_firebase_app_from_options | Unit | Firebase initialization |
| 98 | should_configure_resilience_pipeline | Unit | Polly setup |

---

## 3. Headless.PushNotifications.Dev

### 3.1 NoopPushNotificationService Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 99 | should_return_succeeded_for_send_to_device | Unit | Always succeeds |
| 100 | should_generate_unique_message_id | Unit | New GUID per call |
| 101 | should_return_token_in_response | Unit | Token preserved |
| 102 | should_return_succeeded_for_all_multicast_tokens | Unit | All tokens succeed |
| 103 | should_set_success_count_to_token_count | Unit | SuccessCount correct |
| 104 | should_set_failure_count_to_zero | Unit | No failures |
| 105 | should_generate_unique_message_id_per_token | Unit | Different GUID per token |
| 106 | should_complete_synchronously | Unit | ValueTask.FromResult usage |

### 3.2 Setup Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 107 | should_register_noop_service_as_singleton | Unit | DI registration |

---

## 4. Integration Tests

### 4.1 FCM End-to-End Tests (requires valid Firebase credentials)

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 108 | should_send_notification_to_valid_device | Integration | Real FCM send |
| 109 | should_handle_invalid_token_gracefully | Integration | Unregistered response |
| 110 | should_send_multicast_to_multiple_devices | Integration | Batch send |
| 111 | should_retry_on_rate_limit | Integration | 429 response handling |
| 112 | should_respect_retry_after_header | Integration | Rate limit delay |

---

## Summary

| Category | Unit Tests | Integration Tests | Total |
|----------|------------|-------------------|-------|
| Abstractions (Models) | 22 | 0 | 22 |
| Firebase (Service) | 38 | 8 | 46 |
| Firebase (RetryHelper) | 12 | 0 | 12 |
| Firebase (Options) | 22 | 0 | 22 |
| Firebase (Setup) | 3 | 0 | 3 |
| Dev (NoopService) | 8 | 0 | 8 |
| Dev (Setup) | 1 | 0 | 1 |
| End-to-End | 0 | 5 | 5 |
| **Total** | **106** | **13** | **119** |

### Test Distribution
- **Unit tests**: 106 (mock-based, no external dependencies)
- **Integration tests**: 13 (requires real Firebase credentials)
- **Existing tests**: 0
- **Missing tests**: 119 (all tests new)

### Test Project Structure
```
tests/
├── Headless.PushNotifications.Tests.Unit/          (NEW - 106 tests)
│   ├── Abstractions/
│   │   ├── PushNotificationResponseTests.cs
│   │   └── BatchPushNotificationResponseTests.cs
│   ├── Firebase/
│   │   ├── FcmPushNotificationServiceTests.cs
│   │   ├── RetryHelperTests.cs
│   │   ├── FirebaseOptionsTests.cs
│   │   ├── FirebaseRetryOptionsTests.cs
│   │   └── FirebaseOptionsValidatorTests.cs
│   └── Dev/
│       └── NoopPushNotificationServiceTests.cs
└── Headless.PushNotifications.Tests.Integration/   (NEW - 13 tests)
    └── FcmIntegrationTests.cs
```

### Key Testing Considerations

1. **Reserved Words**: FCM reserves 'from', 'notification', and 'message_type' keys in data payload - tests must verify rejection of these.

2. **Length Limits**: Title max 100 chars, body max 4000 chars - tests should verify boundary conditions.

3. **Batch Chunking**: SendMulticastAsync chunks tokens into batches of 500 - tests should verify correct batching and aggregation.

4. **Retry Logic**: Only QuotaExceeded, Unavailable, and Internal errors are retried. Other errors (Unregistered, InvalidArgument) should fail immediately.

5. **Token Security**: Tokens should be masked (first 8 chars + "***") in logs - tests should verify this security measure.

6. **Retry-After Handling**: Tests should verify both delta-seconds and HTTP-date formats from Retry-After header.

7. **Integration Test Setup**: FCM integration tests require:
   - Valid Firebase project credentials (service account JSON)
   - Test device tokens (can use Firebase emulator)
   - Environment variables for sensitive configuration
