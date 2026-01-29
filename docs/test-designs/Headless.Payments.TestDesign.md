# Headless.Payments.Paymob Test Design

## Overview

The Headless.Payments.Paymob packages provide integration with Paymob's payment gateway for Egyptian market, supporting cash-in (accepting payments) and cash-out (disbursements) operations across multiple payment methods including cards, wallets (Vodafone, Etisalat, Orange), kiosks, and bank transfers.

### Packages
1. **Headless.Payments.Paymob.CashIn** - Low-level API broker for accepting payments
2. **Headless.Payments.Paymob.CashOut** - Low-level API broker for disbursements
3. **Headless.Payments.Paymob.Services** - High-level services wrapping brokers with business logic

### Key Components
- **IPaymobCashInBroker** - Creates orders, payment keys, validates HMAC signatures
- **IPaymobCashInAuthenticator** - Token management with caching and concurrency control
- **IPaymobCashOutBroker** - Disbursements to wallets, bank accounts, kiosks
- **IPaymobCashOutAuthenticator** - OAuth token management with refresh
- **IPaymobCashInService** - High-level cash-in operations
- **ICashOutService** - High-level cash-out operations

### Existing Tests
- **Headless.Payments.Paymob.CashIn.Tests.Unit** - 21 tests (authenticator, broker operations, validation)
- **Headless.Payments.Paymob.CashOut.Tests.Unit** - 0 tests (fixture exists but no tests)

---

## 1. Headless.Payments.Paymob.CashIn

### 1.1 PaymobCashInAuthenticator - RequestAuthenticationTokenAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 1 | should_request_token_from_api | Unit | HTTP POST to auth endpoint |
| 2 | should_throw_on_http_error | Unit | PaymobCashInException on failure |
| 3 | should_deserialize_token_response | Unit | CashInAuthenticationTokenResponse |

### 1.2 PaymobCashInAuthenticator - GetAuthenticationTokenAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 4 | should_return_cached_token_when_valid | Unit | Fast path without lock |
| 5 | should_request_new_token_when_expired | Unit | Token refresh flow |
| 6 | should_cache_new_token_after_request | Unit | Updates cache |
| 7 | should_handle_concurrent_requests | Unit | SemaphoreSlim prevents duplicate requests |
| 8 | should_use_double_check_locking | Unit | Re-validates after acquiring lock |

### 1.3 PaymobCashInBroker - CreateOrderAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 9 | should_create_order_successfully | Unit | POST to orders endpoint |
| 10 | should_include_auth_token_header | Unit | Authorization header |
| 11 | should_throw_on_error_response | Unit | PaymobCashInException |
| 12 | should_serialize_order_request | Unit | JSON serialization |

### 1.4 PaymobCashInBroker - RequestPaymentKeyAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 13 | should_request_payment_key | Unit | POST to payment keys endpoint |
| 14 | should_include_billing_data | Unit | BillingData in request |
| 15 | should_set_integration_id | Unit | Integration ID in request |

### 1.5 PaymobCashInBroker - CreateWalletPayAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 16 | should_create_wallet_pay | Unit | POST to wallets endpoint |
| 17 | should_include_phone_number | Unit | Phone in source.identifier |
| 18 | should_return_redirect_url | Unit | RedirectUrl in response |

### 1.6 PaymobCashInBroker - CreateKioskPayAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 19 | should_create_kiosk_pay | Unit | POST to kiosks endpoint |
| 20 | should_return_bill_reference | Unit | BillReference for Aman/Fawry |

### 1.7 PaymobCashInBroker - CreateSavedTokenPayAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 21 | should_create_saved_token_pay | Unit | POST with saved token |
| 22 | should_handle_3ds_response | Unit | RedirectionUrl for 3D Secure |

### 1.8 PaymobCashInBroker - CreateCashCollectionPayAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 23 | should_create_cash_collection_pay | Unit | POST to cash collection endpoint |

### 1.9 PaymobCashInBroker - CreateIntentionAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 24 | should_create_intention | Unit | POST to intentions endpoint |
| 25 | should_return_payment_keys | Unit | PaymentKeys in response |

### 1.10 PaymobCashInBroker - RefundTransactionAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 26 | should_refund_transaction | Unit | POST to refund endpoint |
| 27 | should_include_amount_cents | Unit | Amount in request |

### 1.11 PaymobCashInBroker - VoidTransactionAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 28 | should_void_transaction | Unit | POST to void endpoint |

### 1.12 PaymobCashInBroker - GetTransactionsPageAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 29 | should_get_transactions_page | Unit | GET transactions endpoint |
| 30 | should_support_pagination | Unit | Page parameters |

### 1.13 PaymobCashInBroker - GetTransactionAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 31 | should_get_transaction_by_id | Unit | GET single transaction |

### 1.14 PaymobCashInBroker - GetOrderAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 32 | should_get_order_by_id | Unit | GET single order |

### 1.15 PaymobCashInBroker - GetOrdersPageAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 33 | should_get_orders_page | Unit | GET orders endpoint |

### 1.16 PaymobCashInBroker - Validate HMAC Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 34 | should_validate_transaction_hmac | Unit | HMACSHA512 verification |
| 35 | should_return_false_for_invalid_hmac | Unit | Tampered data detection |
| 36 | should_validate_token_callback_hmac | Unit | Token callback HMAC |
| 37 | should_validate_query_parameters | Unit | CashInCallbackQueryParameters |
| 38 | should_use_fixed_time_equals | Unit | Timing attack prevention |
| 39 | should_throw_for_null_concatenated_string | Unit | ArgumentException |
| 40 | should_throw_for_null_hmac | Unit | ArgumentException |

### 1.17 PaymobCashInBroker - CreateIframeSrc Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 41 | should_create_iframe_url | Unit | URL construction |

### 1.18 CashInCallbackTransaction Model Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 42 | should_concatenate_fields_in_order | Unit | ToConcatenatedString |
| 43 | should_handle_null_fields | Unit | Null-safe concatenation |

### 1.19 AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 44 | should_add_egypt_offset_to_unspecified | Unit | DateTimeOffset conversion |
| 45 | should_preserve_utc_datetime | Unit | UTC unchanged |

### 1.20 PaymobCashInOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 46 | should_require_api_key | Unit | Required validation |
| 47 | should_require_secret_key | Unit | Required validation |
| 48 | should_require_hmac | Unit | Required validation |

### 1.21 Setup Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 49 | should_register_broker_services | Unit | DI registration |
| 50 | should_configure_http_client | Unit | HttpClient configuration |

---

## 2. Headless.Payments.Paymob.CashOut

### 2.1 PaymobCashOutAuthenticator - GetAccessTokenAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 51 | should_return_cached_token_when_valid | Unit | Fast path |
| 52 | should_request_new_token_when_expired | Unit | OAuth password grant |
| 53 | should_use_semaphore_for_concurrency | Unit | SemaphoreSlim |
| 54 | should_clear_cache_on_options_change | Unit | IOptionsMonitor.OnChange |
| 55 | should_set_token_expiration | Unit | TokenRefreshBuffer |
| 56 | should_double_check_after_lock | Unit | Re-validates in lock |

### 2.2 PaymobCashOutAuthenticator - RefreshTokenAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 57 | should_refresh_with_refresh_token | Unit | OAuth refresh grant |
| 58 | should_update_cache_after_refresh | Unit | Cache updated |
| 59 | should_throw_on_refresh_error | Unit | PaymobCashOutException |

### 2.3 PaymobCashOutBroker - Disburse Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 60 | should_disburse_to_vodafone | Unit | Vodafone wallet |
| 61 | should_disburse_to_etisalat | Unit | Etisalat wallet |
| 62 | should_disburse_to_orange | Unit | Orange wallet |
| 63 | should_disburse_to_bank_wallet | Unit | Bank wallet |
| 64 | should_disburse_to_bank_account | Unit | Bank card |
| 65 | should_disburse_to_aman_kiosk | Unit | Aman/Accept |
| 66 | should_include_auth_header | Unit | Bearer token |
| 67 | should_throw_on_error | Unit | PaymobCashOutException |

### 2.4 PaymobCashOutBroker - GetBudgetAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 68 | should_get_budget | Unit | GET budget/inquire |
| 69 | should_respect_rate_limit | Unit | 5 requests/minute |

### 2.5 PaymobCashOutBroker - GetTransactionsAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 70 | should_get_transactions_by_ids | Unit | POST with IDs |
| 71 | should_validate_non_empty_ids | Unit | ArgumentException |
| 72 | should_validate_positive_page | Unit | ArgumentException |
| 73 | should_distinguish_bank_transactions | Unit | isBankTransactions flag |

### 2.6 PaymobCashOutException Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 74 | should_create_with_status_code | Unit | HttpStatusCode |
| 75 | should_include_response_body | Unit | Body property |
| 76 | should_throw_from_http_response | Unit | ThrowAsync factory |

### 2.7 PaymobCashOutOptions Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 77 | should_require_client_id | Unit | Required validation |
| 78 | should_require_client_secret | Unit | Required validation |
| 79 | should_require_username | Unit | Required validation |
| 80 | should_require_password | Unit | Required validation |
| 81 | should_default_token_refresh_buffer | Unit | Default value |

### 2.8 CashOutDisburseRequest Factory Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 82 | should_create_vodafone_request | Unit | Vodafone factory |
| 83 | should_create_etisalat_request | Unit | Etisalat factory |
| 84 | should_create_orange_request | Unit | Orange factory |
| 85 | should_create_bank_wallet_request | Unit | Bank wallet factory |
| 86 | should_create_bank_card_request | Unit | Bank card factory |
| 87 | should_create_accept_request | Unit | Accept/Aman factory |

### 2.9 CashOutTransaction Status Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 88 | should_detect_success_status | Unit | IsSuccess() |
| 89 | should_detect_pending_status | Unit | IsPending() |
| 90 | should_detect_provider_down_error | Unit | IsProviderDownError() |
| 91 | should_detect_no_vodafone_cash | Unit | IsNotHaveVodafoneCashError() |
| 92 | should_detect_no_etisalat_cash | Unit | IsNotHaveEtisalatCashError() |
| 93 | should_detect_validation_error | Unit | IsRequestValidationError() |

### 2.10 Setup Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 94 | should_register_broker_services | Unit | DI registration |
| 95 | should_configure_http_client | Unit | HttpClient configuration |

---

## 3. Headless.Payments.Paymob.Services

### 3.1 PaymobCashInService - StartAsync (Card) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 96 | should_create_order_and_payment_key | Unit | Full flow |
| 97 | should_return_iframe_src | Unit | PaymobCardCashInResponse |
| 98 | should_convert_amount_to_cents | Unit | amount * 100 |
| 99 | should_throw_conflict_on_broker_error | Unit | ConflictException |
| 100 | should_throw_conflict_on_empty_payment_key | Unit | ConflictException |

### 3.2 PaymobCashInService - StartAsync (SavedToken) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 101 | should_create_saved_token_pay | Unit | Full flow |
| 102 | should_detect_3ds_requirement | Unit | Is3DSecure flag |
| 103 | should_throw_on_error_occurred | Unit | ErrorOccured check |

### 3.3 PaymobCashInService - StartAsync (Wallet) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 104 | should_create_wallet_pay | Unit | Full flow |
| 105 | should_return_redirect_url | Unit | PaymobWalletCashInResponse |
| 106 | should_throw_on_empty_redirect | Unit | ConflictException |

### 3.4 PaymobCashInService - StartAsync (Kiosk) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 107 | should_create_kiosk_pay | Unit | Full flow |
| 108 | should_return_billing_reference | Unit | PaymobKioskCashInResponse |
| 109 | should_throw_on_null_data | Unit | ConflictException |

### 3.5 PaymobCashInService - StartAsync (Intention) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 110 | should_create_intention | Unit | Delegates to broker |
| 111 | should_throw_for_null_request | Unit | ArgumentException |

### 3.6 PaymobCashInService - RefundAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 112 | should_refund_transaction | Unit | Delegates to broker |
| 113 | should_convert_amount_to_cents | Unit | Ceiling(amount * 100) |
| 114 | should_throw_for_null_request | Unit | ArgumentException |

### 3.7 PaymobCashInService - VoidAsync Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 115 | should_void_transaction | Unit | Delegates to broker |
| 116 | should_throw_for_null_request | Unit | ArgumentException |

### 3.8 PaymobCashOutService - DisburseAsync (Vodafone) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 117 | should_disburse_to_vodafone | Unit | Full flow |
| 118 | should_return_success_status | Unit | CashOutResponseStatus.Success |
| 119 | should_return_pending_status | Unit | CashOutResponseStatus.Pending |
| 120 | should_return_failure_on_error | Unit | CashOutResult.Failure |

### 3.9 PaymobCashOutService - DisburseAsync (Etisalat) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 121 | should_disburse_to_etisalat | Unit | Full flow |

### 3.10 PaymobCashOutService - DisburseAsync (Orange) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 122 | should_disburse_to_orange | Unit | Full flow |
| 123 | should_include_full_name | Unit | Orange requires name |

### 3.11 PaymobCashOutService - DisburseAsync (BankWallet) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 124 | should_disburse_to_bank_wallet | Unit | Full flow |

### 3.12 PaymobCashOutService - DisburseAsync (BankAccount) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 125 | should_disburse_to_bank_account | Unit | Full flow |
| 126 | should_convert_transaction_type | Unit | BankTransactionType enum |
| 127 | should_throw_for_invalid_type | Unit | ArgumentOutOfRangeException |

### 3.13 PaymobCashOutService - DisburseAsync (Kiosk) Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 128 | should_disburse_to_kiosk | Unit | Full flow |
| 129 | should_return_billing_reference | Unit | AmanCashingDetails |
| 130 | should_return_failure_on_null_reference | Unit | Unexpected response |

### 3.14 PaymobCashOutService - Error Mapping Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 131 | should_map_provider_down_error | Unit | ProviderIsDown message |
| 132 | should_map_no_vodafone_cash | Unit | NoVodafoneCash message |
| 133 | should_map_no_etisalat_cash | Unit | NoEtisalatCash message |
| 134 | should_map_validation_error | Unit | InvalidRequest message |
| 135 | should_map_budget_exceeded | Unit | ProviderConnectionFailed |
| 136 | should_handle_unknown_error | Unit | ProviderConnectionFailed |

### 3.15 PaymobCashInFeesCalculator Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 137 | should_calculate_card_fees | Unit | Card fee calculation |
| 138 | should_calculate_wallet_fees | Unit | Wallet fee calculation |
| 139 | should_calculate_kiosk_fees | Unit | Kiosk fee calculation |

### 3.16 PaymobMessageDescriptor Tests

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 140 | should_return_localized_cash_in_messages | Unit | CashIn messages |
| 141 | should_return_localized_cash_out_messages | Unit | CashOut messages |
| 142 | should_return_localized_general_messages | Unit | General messages |

---

## 4. Integration Tests

### 4.1 CashIn End-to-End Tests (Sandbox)

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 143 | should_complete_card_payment_flow | Integration | Full card flow |
| 144 | should_complete_wallet_payment_flow | Integration | Full wallet flow |
| 145 | should_validate_callback_signature | Integration | Real HMAC validation |
| 146 | should_refund_transaction | Integration | Refund flow |

### 4.2 CashOut End-to-End Tests (Sandbox)

| # | Test Case | Type | Description |
|---|-----------|------|-------------|
| 147 | should_disburse_to_wallet | Integration | Real wallet disbursement |
| 148 | should_handle_insufficient_balance | Integration | Budget error handling |

---

## Summary

| Category | Unit Tests | Integration Tests | Total |
|----------|------------|-------------------|-------|
| CashIn Authenticator | 8 | 0 | 8 |
| CashIn Broker | 36 | 0 | 36 |
| CashIn Options/Setup | 5 | 0 | 5 |
| CashOut Authenticator | 9 | 0 | 9 |
| CashOut Broker | 15 | 0 | 15 |
| CashOut Models | 12 | 0 | 12 |
| CashOut Options/Setup | 7 | 0 | 7 |
| Services (CashIn) | 21 | 0 | 21 |
| Services (CashOut) | 20 | 0 | 20 |
| Services (Other) | 9 | 0 | 9 |
| End-to-End | 0 | 6 | 6 |
| **Total** | **142** | **6** | **148** |

### Test Distribution
- **Unit tests**: 142 (mock-based with HttpClient fakes)
- **Integration tests**: 6 (requires Paymob sandbox credentials)
- **Existing tests**: 21 (CashIn broker and authenticator)
- **Missing tests**: 127 (new tests needed)

### Test Project Structure
```
tests/
├── Headless.Payments.Paymob.CashIn.Tests.Unit/     (EXISTING - 21 tests)
│   ├── PaymobCashInAuthenticatorTests.*.cs
│   ├── PaymobCashInBrokerTests.*.cs
│   └── Internal/
│       └── AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverterTests.cs
├── Headless.Payments.Paymob.CashOut.Tests.Unit/    (NEEDS TESTS - ~44 tests)
│   ├── PaymobCashOutAuthenticatorTests.cs
│   ├── PaymobCashOutBrokerTests.cs
│   └── Models/
│       └── CashOutTransactionTests.cs
├── Headless.Payments.Paymob.Services.Tests.Unit/   (NEW - ~62 tests)
│   ├── CashIn/
│   │   └── PaymobCashInServiceTests.cs
│   └── CashOut/
│       └── PaymobCashOutServiceTests.cs
└── Headless.Payments.Paymob.Tests.Integration/     (NEW - 6 tests)
    ├── CashInIntegrationTests.cs
    └── CashOutIntegrationTests.cs
```

### Key Testing Considerations

1. **HMAC Validation**: Critical security feature - tests must verify HMACSHA512 computation and constant-time comparison using `CryptographicOperations.FixedTimeEquals`.

2. **Token Caching**: Both authenticators use double-checked locking pattern with `SemaphoreSlim` - tests must verify thread safety and cache invalidation.

3. **Amount Conversion**: CashIn uses `Math.Ceiling(amount * 100)` for cents - tests should verify edge cases like fractional amounts.

4. **Error Mapping**: CashOut service maps various error types to user-friendly messages - tests should cover all error scenarios.

5. **HTTP Client Mocking**: Use `MockHttpMessageHandler` or similar for HTTP request/response mocking.

6. **Sandbox Testing**: Integration tests require Paymob sandbox credentials and should be tagged appropriately.

7. **Egypt Timezone**: The `AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter` handles Paymob's timezone quirks - important edge case testing.
