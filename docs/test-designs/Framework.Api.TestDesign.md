# Test Case Design: Framework.Api

**Package:** `src/Framework.Api`
**Test Projects:** `Framework.Api.Tests.Unit`, `Framework.Api.Tests.Integration`
**Generated:** 2026-01-25

## Current Test Coverage

| Component | Existing Tests | Coverage |
|-----------|----------------|----------|
| IdempotencyMiddleware | 4 tests | Good |
| TotpRfc6238Generator | 10 tests | Good |
| Other components | 0 tests | Missing |

---

## 1. Authentication Handlers

### 1.1 ApiKeyAuthenticationHandler Tests

**File:** `tests/Framework.Api.Tests.Unit/Identity/Authentication/ApiKeyAuthenticationHandlerTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_return_success_when_user_already_authenticated` | HandleAuthenticateAsync | Returns success ticket when Context.User.Identity.IsAuthenticated is true |
| `should_return_no_result_when_no_api_key_header_or_query` | HandleAuthenticateAsync | No result when neither header nor query param contains API key |
| `should_return_no_result_when_api_key_header_empty` | HandleAuthenticateAsync | No result when X-Api-Key header exists but is empty |
| `should_return_no_result_when_api_key_query_empty` | HandleAuthenticateAsync | No result when api_key query param exists but is empty |
| `should_return_no_result_when_api_key_user_not_found` | HandleAuthenticateAsync | No result when IApiKeyStore returns null |
| `should_return_fail_when_user_cannot_sign_in` | HandleAuthenticateAsync | Fail when SignInManager.CanSignInAsync returns false |
| `should_return_fail_when_user_locked_out` | HandleAuthenticateAsync | Fail when UserManager.IsLockedOutAsync returns true |
| `should_return_success_with_ticket_when_valid_api_key` | HandleAuthenticateAsync | Success ticket with claims principal from SignInManager |
| `should_use_header_over_query_when_both_present` | HandleAuthenticateAsync | Header takes precedence when both sources have API key |

**Mocking Requirements:**
- `IApiKeyStore<TUser, TUserId>` - return user or null
- `UserManager<TUser>` - SupportsUserLockout, IsLockedOutAsync
- `SignInManager<TUser>` - CanSignInAsync, CreateUserPrincipalAsync

---

### 1.2 BasicAuthenticationHandler Tests

**File:** `tests/Framework.Api.Tests.Unit/Identity/Authentication/BasicAuthenticationHandlerTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_return_success_when_user_already_authenticated` | HandleAuthenticateAsync | Returns success when Context.User is authenticated |
| `should_return_no_result_when_no_authorization_header` | HandleAuthenticateAsync | No result when Authorization header missing |
| `should_return_no_result_when_not_basic_scheme` | HandleAuthenticateAsync | No result when scheme is Bearer not Basic |
| `should_return_fail_when_invalid_base64_encoding` | HandleAuthenticateAsync | Fail when credentials not valid base64 |
| `should_return_fail_when_no_colon_separator` | HandleAuthenticateAsync | Fail when decoded string has no colon |
| `should_return_fail_when_user_not_found` | HandleAuthenticateAsync | Fail with generic message when FindByNameAsync returns null |
| `should_return_fail_when_user_cannot_sign_in` | HandleAuthenticateAsync | Fail when CanSignInAsync returns false |
| `should_return_fail_when_user_locked_out` | HandleAuthenticateAsync | Fail when IsLockedOutAsync returns true |
| `should_return_fail_when_wrong_password` | HandleAuthenticateAsync | Fail when CheckPasswordAsync returns false |
| `should_return_success_with_ticket_when_valid_credentials` | HandleAuthenticateAsync | Success with claims principal |
| `should_use_generic_error_message_to_prevent_enumeration` | HandleAuthenticateAsync | All failures return same "Invalid user name or password" |

**Edge Cases:**
- Empty username with password
- Username with empty password
- Credentials with multiple colons (password contains colon)

---

### 1.3 DynamicAuthenticationSchemeProvider Tests

**File:** `tests/Framework.Api.Tests.Unit/Identity/Schemes/DynamicAuthenticationSchemeProviderTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_return_null_when_no_http_context` | _GetRequestSchemeAsync | Returns null when HttpContextAccessor.HttpContext is null |
| `should_return_api_key_scheme_when_api_key_header_present` | GetDefaultAuthenticateSchemeAsync | Returns ApiKey scheme when X-Api-Key header exists |
| `should_return_api_key_scheme_when_api_key_query_present` | GetDefaultAuthenticateSchemeAsync | Returns ApiKey scheme when api_key query param exists |
| `should_return_basic_scheme_when_basic_auth_header` | GetDefaultAuthenticateSchemeAsync | Returns Basic scheme when "Basic xxx" in Authorization |
| `should_return_bearer_scheme_when_bearer_auth_header` | GetDefaultAuthenticateSchemeAsync | Returns Bearer scheme when "Bearer xxx" in Authorization |
| `should_return_bearer_scheme_when_non_basic_auth_header` | GetDefaultAuthenticateSchemeAsync | Returns Bearer for any non-Basic Authorization header |
| `should_fallback_to_base_when_no_scheme_detected` | GetDefaultAuthenticateSchemeAsync | Calls base method when no scheme indicators found |
| `should_check_api_key_before_authorization_header` | GetDefaultAuthenticateSchemeAsync | API key takes precedence over Authorization header |
| `should_work_for_all_get_default_methods` | GetDefault*SchemeAsync | All 5 GetDefault methods use same detection logic |

---

## 2. Middlewares

### 2.1 StatusCodesRewriterMiddleware Tests

**File:** `tests/Framework.Api.Tests.Unit/Middlewares/StatusCodesRewriterMiddlewareTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_call_next_and_not_modify_response_when_2xx` | InvokeAsync | Pass through for 200-299 status codes |
| `should_call_next_and_not_modify_response_when_3xx` | InvokeAsync | Pass through for 300-399 status codes |
| `should_not_modify_when_response_already_started` | InvokeAsync | Skip when HasStarted is true |
| `should_not_modify_when_content_length_set` | InvokeAsync | Skip when ContentLength has value |
| `should_not_modify_when_content_type_set` | InvokeAsync | Skip when ContentType is not empty |
| `should_return_problem_details_for_401` | InvokeAsync | Returns Unauthorized problem details |
| `should_return_problem_details_for_403` | InvokeAsync | Returns Forbidden problem details |
| `should_return_problem_details_for_404` | InvokeAsync | Returns EndpointNotFound problem details |
| `should_not_modify_other_4xx_errors` | InvokeAsync | 400, 422, 429 etc. pass through unchanged |
| `should_set_content_type_to_problem_json` | InvokeAsync | Response ContentType is application/problem+json |

---

### 2.2 ServerTimingMiddleware Tests

**File:** `tests/Framework.Api.Tests.Unit/Middlewares/ServerTimingMiddlewareTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_call_next_without_trailer_when_not_supported` | InvokeAsync | No trailer when SupportsTrailers() is false |
| `should_declare_server_timing_trailer` | InvokeAsync | DeclareTrailer called with "Server-Timing" |
| `should_append_server_timing_trailer_after_next` | InvokeAsync | AppendTrailer called with timing value |
| `should_format_timing_in_microseconds` | InvokeAsync | Format is "app;dur=XXX.0" |
| `should_use_invariant_culture_for_formatting` | InvokeAsync | Consistent decimal separator across cultures |
| `should_throw_when_context_null` | InvokeAsync | ArgumentNullException for null context |
| `should_throw_when_next_null` | InvokeAsync | ArgumentNullException for null next |

---

### 2.3 RequestCanceledMiddleware Tests

**File:** `tests/Framework.Api.Tests.Unit/Middlewares/RequestCanceledMiddlewareTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_call_next_when_not_cancelled` | InvokeAsync | Normal flow when no cancellation |
| `should_return_499_when_request_aborted` | InvokeAsync | Status 499 when OperationCanceledException + RequestAborted |
| `should_not_catch_non_abort_cancellation` | InvokeAsync | Re-throws OperationCanceledException when not from RequestAborted |
| `should_add_activity_event_when_cancelled` | InvokeAsync | ActivityEvent "Client cancelled the request" added |
| `should_log_information_when_cancelled` | InvokeAsync | Logger.LogInformation called |
| `should_throw_when_context_null` | InvokeAsync | ArgumentNullException for null context |

---

## 3. JWT Token Factory

### 3.1 JwtTokenFactory Tests

**File:** `tests/Framework.Api.Tests.Unit/Security/Jwt/JwtTokenFactoryTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_create_token_with_claims` | CreateJwtToken | Token contains all provided claims |
| `should_set_issued_at_from_clock` | CreateJwtToken | IssuedAt uses IClock.UtcNow |
| `should_set_expires_from_ttl` | CreateJwtToken | Expires = IssuedAt + ttl |
| `should_set_not_before_when_provided` | CreateJwtToken | NotBefore = IssuedAt + notBefore |
| `should_skip_not_before_when_null` | CreateJwtToken | NotBefore is null when parameter omitted |
| `should_sign_token_with_hmac_sha256` | CreateJwtToken | Uses HmacSha256 algorithm |
| `should_encrypt_token_when_encrypting_key_provided` | CreateJwtToken | Token is JWE when encryptingKey not null |
| `should_skip_encryption_when_key_null` | CreateJwtToken | Token is JWS when encryptingKey is null |
| `should_set_issuer_and_audience` | CreateJwtToken | Issuer and audience claims set correctly |
| `should_parse_valid_token` | ParseJwtTokenAsync | Returns ClaimsPrincipal for valid token |
| `should_return_null_for_expired_token` | ParseJwtTokenAsync | Returns null when token expired |
| `should_return_null_for_invalid_signature` | ParseJwtTokenAsync | Returns null when signature invalid |
| `should_return_null_for_wrong_issuer` | ParseJwtTokenAsync | Returns null when issuer doesn't match |
| `should_return_null_for_wrong_audience` | ParseJwtTokenAsync | Returns null when audience doesn't match |
| `should_skip_issuer_validation_when_disabled` | ParseJwtTokenAsync | Validates when validateIssuer=false |
| `should_skip_audience_validation_when_disabled` | ParseJwtTokenAsync | Validates when validateAudience=false |
| `should_decrypt_encrypted_token` | ParseJwtTokenAsync | Successfully decrypts JWE token |
| `should_use_zero_clock_skew` | ParseJwtTokenAsync | No tolerance for expired tokens |

**Security Tests:**
| Test Case | Description |
|-----------|-------------|
| `should_reject_token_without_signature` | Unsigned tokens rejected |
| `should_reject_none_algorithm` | Algorithm "none" rejected |
| `should_reject_malformed_token` | Invalid format returns null |

---

## 4. ProblemDetails Creator

### 4.1 ProblemDetailsCreator Tests

**File:** `tests/Framework.Api.Tests.Unit/Abstractions/ProblemDetailsCreatorTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_create_endpoint_not_found_with_404` | EndpointNotFound | Status=404, correct title/detail |
| `should_include_request_path_in_endpoint_not_found` | EndpointNotFound | Detail includes request path |
| `should_create_entity_not_found_with_404` | EntityNotFound | Status=404, EntityNotFound title |
| `should_not_expose_entity_or_key` | EntityNotFound | Entity and key params are discarded |
| `should_create_malformed_syntax_with_400` | MalformedSyntax | Status=400, BadRequest title |
| `should_create_too_many_requests_with_429` | TooManyRequests | Status=429, includes retryAfter |
| `should_create_unprocessable_entity_with_422` | UnprocessableEntity | Status=422, errors extension |
| `should_create_conflict_with_409` | Conflict | Status=409, errors extension |
| `should_create_unauthorized_with_401` | Unauthorized | Status=401, correct title |
| `should_create_forbidden_with_403` | Forbidden | Status=403, correct title |
| `should_include_errors_in_forbidden_when_provided` | Forbidden | Errors extension when count > 0 |
| `should_omit_errors_in_forbidden_when_empty` | Forbidden | No errors extension when empty |

**Normalize Tests:**
| Test Case | Description |
|-----------|-------------|
| `should_add_trace_id_from_activity` | traceId from Activity.Current.Id |
| `should_add_trace_id_from_http_context` | traceId from HttpContext.TraceIdentifier |
| `should_add_build_number` | buildNumber extension added |
| `should_add_commit_number` | commitNumber extension added |
| `should_add_timestamp_in_iso_format` | timestamp in "O" format |
| `should_set_instance_from_request_path` | Instance = request path |
| `should_set_internal_error_title_for_500` | Overwrites title for 500 |
| `should_not_overwrite_existing_trace_id` | Preserves existing traceId |
| `should_use_client_error_mapping_for_type` | Uses ApiBehaviorOptions.ClientErrorMapping |

---

## 5. Token Providers

### 5.1 TotpTokenProvider Tests

**File:** `tests/Framework.Api.Tests.Unit/Identity/TokenProviders/TotpTokenProviderTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_generate_code_using_security_stamp` | GenerateAsync | Uses UserManager.CreateSecurityTokenAsync |
| `should_validate_correct_code` | ValidateAsync | Returns true for correct code |
| `should_reject_wrong_code` | ValidateAsync | Returns false for wrong code |
| `should_reject_code_after_timestep_expires` | ValidateAsync | Returns false outside variance window |
| `should_use_modifier_from_options` | GenerateAsync | Modifier includes options.Name and purpose |
| `should_return_false_for_can_generate_two_factor` | CanGenerateTwoFactorTokenAsync | Always returns false |

### 5.2 EmailConfirmationCodeProvider Tests

| Test Case | Description |
|-----------|-------------|
| `should_use_email_in_modifier` | Modifier includes user email |
| `should_throw_when_user_has_no_email` | InvalidOperationException when email null |
| `should_return_false_for_can_generate_two_factor` | Always returns false |

### 5.3 PasswordResetCodeProvider Tests

| Test Case | Description |
|-----------|-------------|
| `should_use_user_id_in_modifier` | Modifier includes user ID |
| `should_return_false_for_can_generate_two_factor` | Always returns false |

---

## 6. HTTP Abstractions

### 6.1 HttpCurrentUser Tests

**File:** `tests/Framework.Api.Tests.Unit/Abstractions/HttpCurrentUserTests.cs`

| Test Case | Property/Method | Description |
|-----------|-----------------|-------------|
| `should_return_principal_from_accessor` | Principal | Returns ICurrentPrincipalAccessor.Principal |
| `should_return_is_authenticated_from_identity` | IsAuthenticated | True when Identity.IsAuthenticated |
| `should_return_false_when_no_identity` | IsAuthenticated | False when Principal null |
| `should_return_user_id_from_claims` | UserId | Extracts from UserClaimTypes.UserId claim |
| `should_return_null_user_id_when_missing` | UserId | Null when claim not present |
| `should_return_user_name_from_claims` | UserName | Extracts from UserClaimTypes.UserName claim |
| `should_return_email_from_claims` | Email | Extracts from UserClaimTypes.Email claim |
| `should_return_roles_from_claims` | Roles | ImmutableHashSet of role claims |
| `should_return_empty_roles_when_none` | Roles | Empty set when no role claims |

### 6.2 HttpRequestContext Tests

| Test Case | Property | Description |
|-----------|----------|-------------|
| `should_return_user_from_http_current_user` | User | Delegates to ICurrentUser |
| `should_return_tenant_from_current_tenant` | Tenant | Delegates to ICurrentTenant |
| `should_return_date_started_from_clock` | DateStarted | Uses IClock.UtcNow |
| `should_return_correlation_id_from_http_context` | CorrelationId | From headers or HttpContext.TraceIdentifier |

### 6.3 HttpAbsoluteUrlFactory Tests

| Test Case | Property/Method | Description |
|-----------|-----------------|-------------|
| `should_return_origin_from_request` | Origin | Combines scheme and host |
| `should_set_scheme_and_host_when_setting_origin` | Origin setter | Updates Request.Scheme and Request.Host |
| `should_throw_when_origin_format_invalid` | Origin setter | Invalid format throws ArgumentException |
| `should_create_absolute_url` | Create | Combines origin with path |
| `should_preserve_query_string` | Create | Query params included in URL |

---

## 7. Mediator Behaviors

### 7.1 ApiValidationRequestPreProcessor Tests

**File:** `tests/Framework.Api.Tests.Unit/Mediator/ApiValidationRequestPreProcessorTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_not_throw_when_no_validators` | Process | Completes without error when validators empty |
| `should_not_throw_when_validation_passes` | Process | Completes when all validators pass |
| `should_throw_validation_exception_when_fails` | Process | ValidationException with failures |
| `should_aggregate_errors_from_multiple_validators` | Process | All validator errors collected |
| `should_log_request_when_debug_enabled` | Process | Logs at debug level |

### 7.2 ApiRequestLoggingBehavior Tests

| Test Case | Description |
|-----------|-------------|
| `should_log_request_when_logging_enabled` | Logs message name and user ID |
| `should_not_log_when_logging_disabled` | Respects IsMediatorMessageLoggingEnabled |

### 7.3 ApiCriticalRequestLoggingBehavior Tests

| Test Case | Description |
|-----------|-------------|
| `should_log_when_request_exceeds_threshold` | Logs slow requests |
| `should_not_log_when_under_threshold` | No log for fast requests |

---

## 8. Extensions

### 8.1 HttpContextExtensions Tests

**File:** `tests/Framework.Api.Tests.Unit/Extensions/HttpContextExtensionsTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_get_ip_from_x_forwarded_for` | GetRequestIp | First IP from X-Forwarded-For |
| `should_get_ip_from_x_real_ip` | GetRequestIp | Fallback to X-Real-IP |
| `should_get_ip_from_connection` | GetRequestIp | Fallback to RemoteIpAddress |
| `should_get_user_agent_from_header` | GetUserAgent | User-Agent header value |
| `should_get_correlation_id_from_header` | GetCorrelationId | X-Correlation-ID header |
| `should_set_no_cache_headers` | SetNoCacheHeaders | Sets all cache control headers |
| `should_set_cache_headers_with_ttl` | SetCacheHeaders | Sets max-age and related headers |

### 8.2 FormFileExtensions Tests

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_save_file_to_directory` | SaveAsync | Creates file in specified directory |
| `should_create_directory_if_not_exists` | SaveAsync | Creates parent directories |
| `should_save_multiple_files` | SaveAsync (collection) | Saves all files in collection |
| `should_return_failures_for_io_errors` | SaveAsync (collection) | Result contains exceptions |

---

## 9. Integration Tests

### 9.1 Authentication Integration Tests

**File:** `tests/Framework.Api.Tests.Integration/Identity/AuthenticationIntegrationTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_authenticate_with_valid_api_key` | Full flow with WebApplicationFactory |
| `should_authenticate_with_valid_basic_credentials` | Full flow with Basic auth |
| `should_reject_invalid_api_key` | 401 response for invalid key |
| `should_reject_invalid_basic_credentials` | 401 response for wrong password |
| `should_select_correct_scheme_dynamically` | DynamicAuthenticationSchemeProvider integration |

### 9.2 Middleware Integration Tests

| Test Case | Description |
|-----------|-------------|
| `should_return_problem_details_for_unauthorized` | Full pipeline returns proper 401 |
| `should_add_server_timing_trailer` | Server-Timing in response |
| `should_return_conflict_for_duplicate_idempotency_key` | Full idempotency flow |

---

## Test Infrastructure Requirements

### Shared Fixtures

```csharp
public sealed class ApiTestFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; }
    // Setup authentication schemes, mock stores, etc.
}
```

### Test Helpers

```csharp
public static class AuthenticationTestHelpers
{
    public static string CreateBasicAuthHeader(string username, string password);
    public static MockUser CreateTestUser(string id, string username, string email);
    public static IApiKeyStore<TUser, TUserId> CreateMockApiKeyStore<TUser, TUserId>(...);
}
```

### Mocking Strategy

| Dependency | Mock Library | Notes |
|------------|--------------|-------|
| UserManager | NSubstitute | Partial mock for SupportsUserLockout |
| SignInManager | NSubstitute | Mock CanSignInAsync, CreateUserPrincipalAsync |
| IApiKeyStore | NSubstitute | Simple mock |
| IHttpContextAccessor | NSubstitute | Return configured DefaultHttpContext |
| ICache | NSubstitute | Mock TryInsertAsync return values |
| IClock | FakeTimeProvider | From Microsoft.Extensions.Time.Testing |

---

## Priority Order

1. **High Priority** (security-critical, no existing tests):
   - ApiKeyAuthenticationHandler
   - BasicAuthenticationHandler
   - JwtTokenFactory
   - DynamicAuthenticationSchemeProvider

2. **Medium Priority** (middleware, core functionality):
   - StatusCodesRewriterMiddleware
   - ServerTimingMiddleware
   - RequestCanceledMiddleware
   - ProblemDetailsCreator
   - ApiValidationRequestPreProcessor

3. **Lower Priority** (utilities, less critical):
   - HttpCurrentUser
   - HttpRequestContext
   - HttpContextExtensions
   - FormFileExtensions
   - Token Providers (partially covered)

---

## Estimated Test Count

| Category | Unit Tests | Integration Tests | Total |
|----------|------------|-------------------|-------|
| Authentication | 30 | 5 | 35 |
| Middlewares | 25 | 3 | 28 |
| JWT | 20 | 2 | 22 |
| ProblemDetails | 20 | 2 | 22 |
| Token Providers | 15 | 0 | 15 |
| HTTP Abstractions | 25 | 0 | 25 |
| Mediator Behaviors | 10 | 0 | 10 |
| Extensions | 15 | 0 | 15 |
| **Total** | **160** | **12** | **172** |

**Current:** ~14 tests
**Target:** ~172 tests
**Gap:** ~158 tests needed
