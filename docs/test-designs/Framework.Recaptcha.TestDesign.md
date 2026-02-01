# Test Case Design: Framework.Recaptcha

**Package:** `src/Framework.Recaptcha`
**Test Projects:** None existing - **needs creation**
**Generated:** 2026-01-25

## Package Analysis

| File | Purpose | Testable |
|------|---------|----------|
| `Setup.cs` | DI registration for V2/V3 services | Medium (DI wiring) |
| `Contracts/ReCaptchaOptions.cs` | Options + FluentValidation validator | High |
| `Contracts/ReCaptchaError.cs` | Error enum | Low (enum) |
| `Contracts/ReCaptchaSiteVerifyRequest.cs` | Request DTO | Low (record) |
| `Contracts/ReCaptchaSiteVerifyV2Response.cs` | V2 response + error parsing | High |
| `Contracts/ReCaptchaSiteVerifyV3Response.cs` | V3 response + error parsing | High |
| `Services/IReCaptchaLanguageCodeProvider.cs` | Language code interface + impl | Medium |
| `V2/IReCaptchaSiteVerifyV2.cs` | V2 verification service | High (HTTP mocking) |
| `V3/IReCaptchaSiteVerifyV3.cs` | V3 verification service | High (HTTP mocking) |
| `V2/TagHelpers/ReCaptchaV2DivTagHelper.cs` | Razor div tag helper | Medium |
| `V2/TagHelpers/ReCaptchaV2ElementTagHelper.cs` | Razor element attributes tag helper | Medium |
| `V2/TagHelpers/ReCaptchaV2ScriptTagHelper.cs` | Razor script tag helper | Medium |
| `V3/TagHelpers/ReCaptchaV3ScriptTagHelper.cs` | V3 script tag helper | Medium |
| `V3/TagHelpers/ReCaptchaV3ScriptJsTagHelper.cs` | V3 JS execution tag helper | High (XSS validation) |
| `Internals/StringExtensions.cs` | String utilities (RemovePostFix, Left) | High |
| `Internals/ReCaptchaErrorExtensions.cs` | Error code string-to-enum mapping | High |
| `Internals/ReCaptchaJsonOptions.cs` | JSON serialization config | Low |
| `Internals/ReCaptchaJsonSerializerContext.cs` | Source-gen JSON context | Low |
| `Internals/ReCaptchaLoggerExtensions.cs` | Structured logging | Low |

---

## 1. ReCaptchaOptionsValidator Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/Contracts/ReCaptchaOptionsValidatorTests.cs`

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_pass_when_all_fields_valid` | Valid SiteKey, SiteSecret, VerifyBaseUrl | Valid |
| `should_fail_when_SiteKey_empty` | SiteKey = "" | Error |
| `should_fail_when_SiteKey_null` | SiteKey = null | Error |
| `should_fail_when_SiteSecret_empty` | SiteSecret = "" | Error |
| `should_fail_when_SiteSecret_null` | SiteSecret = null | Error |
| `should_fail_when_VerifyBaseUrl_invalid` | VerifyBaseUrl = "not-a-url" | Error |
| `should_pass_with_default_VerifyBaseUrl` | VerifyBaseUrl = default | Valid |
| `should_pass_with_custom_VerifyBaseUrl` | VerifyBaseUrl = "https://custom.com/" | Valid |

---

## 2. Response Error Parsing Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/Contracts/ReCaptchaSiteVerifyV2ResponseTests.cs`

### ParseErrors Tests

| Test Case | ErrorCodes | Expected |
|-----------|------------|----------|
| `should_return_empty_when_ErrorCodes_null` | null | [] |
| `should_parse_single_error` | ["bad-request"] | [BadRequest] |
| `should_parse_multiple_errors` | ["bad-request", "timeout-or-duplicate"] | [BadRequest, TimeOutOrDuplicate] |
| `should_return_Unknown_for_unrecognized_error` | ["unknown-error-code"] | [Unknown] |

### ParseError Static Tests

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_parse_bad_request` | "bad-request" | BadRequest |
| `should_parse_timeout_or_duplicate` | "timeout-or-duplicate" | TimeOutOrDuplicate |
| `should_parse_invalid_input_response` | "invalid-input-response" | InvalidInputResponse |
| `should_parse_missing_input_response` | "missing-input-response" | MissingInputResponse |
| `should_parse_invalid_input_secret` | "invalid-input-secret" | InvalidInputSecret |
| `should_parse_missing_input_secret` | "missing-input-secret" | MissingInputSecret |
| `should_return_Unknown_for_unrecognized` | "foo-bar" | Unknown |

### MemberNotNullWhen Attribute Tests

| Test Case | Description |
|-----------|-------------|
| `should_have_HostName_when_Success_true` | Nullable analysis compliance |
| `should_have_ChallengeTimeStamp_when_Success_true` | Nullable analysis compliance |
| `should_have_ErrorCodes_when_Success_false` | Nullable analysis compliance |

**File:** `tests/Framework.Recaptcha.Tests.Unit/Contracts/ReCaptchaSiteVerifyV3ResponseTests.cs`

Same tests as V2 plus:

| Test Case | Description |
|-----------|-------------|
| `should_have_Score_when_Success_true` | V3-specific |
| `should_have_Action_when_Success_true` | V3-specific |

---

## 3. ReCaptchaErrorExtensions Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/Internals/ReCaptchaErrorExtensionsTests.cs`

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_convert_bad_request` | "bad-request" | BadRequest |
| `should_convert_timeout_or_duplicate` | "timeout-or-duplicate" | TimeOutOrDuplicate |
| `should_convert_invalid_input_response` | "invalid-input-response" | InvalidInputResponse |
| `should_convert_missing_input_response` | "missing-input-response" | MissingInputResponse |
| `should_convert_invalid_input_secret` | "invalid-input-secret" | InvalidInputSecret |
| `should_convert_missing_input_secret` | "missing-input-secret" | MissingInputSecret |
| `should_return_Unknown_for_empty` | "" | Unknown |
| `should_return_Unknown_for_unrecognized` | "random-error" | Unknown |

---

## 4. StringExtensions Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/Internals/StringExtensionsTests.cs`

### RemovePostFix Tests

| Test Case | Input | PostFixes | Expected |
|-----------|-------|-----------|----------|
| `should_return_null_for_null_string` | null | ["/"] | null |
| `should_return_null_for_empty_string` | "" | ["/"] | null |
| `should_return_string_when_no_postfixes` | "test" | [] | "test" |
| `should_remove_matching_postfix` | "https://google.com/" | ["/"] | "https://google.com" |
| `should_remove_first_matching_postfix` | "test.txt.bak" | [".bak", ".txt"] | "test.txt" |
| `should_return_original_when_no_match` | "test" | ["/"] | "test" |
| `should_respect_StringComparison` | "TEST/" | ["/"] (OrdinalIgnoreCase) | "TEST" |

### Left Tests

| Test Case | Input | Length | Expected |
|-----------|-------|--------|----------|
| `should_return_left_characters` | "hello" | 3 | "hel" |
| `should_return_full_string_when_shorter` | "hi" | 10 | "hi" |
| `should_return_empty_for_zero_length` | "hello" | 0 | "" |

---

## 5. CultureInfoReCaptchaLanguageCodeProvider Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/Services/CultureInfoReCaptchaLanguageCodeProviderTests.cs`

| Test Case | Culture | Expected |
|-----------|---------|----------|
| `should_return_current_UI_culture` | en-US | "en-US" |
| `should_return_different_culture` | ar-SA | "ar-SA" |
| `should_handle_neutral_culture` | en | "en" |

---

## 6. ReCaptchaSiteVerifyV2 Tests (HTTP Mocking)

**File:** `tests/Framework.Recaptcha.Tests.Unit/V2/ReCaptchaSiteVerifyV2Tests.cs`

### Success Cases

| Test Case | Description |
|-----------|-------------|
| `should_return_success_response_when_token_valid` | Mock 200 with success:true |
| `should_include_remoteip_when_provided` | Form data includes remoteip |
| `should_not_include_remoteip_when_null` | Form data excludes remoteip |
| `should_send_secret_in_form_data` | Options.SiteSecret sent |
| `should_send_response_token_in_form_data` | Request.Response sent |

### Failure Cases

| Test Case | Description |
|-----------|-------------|
| `should_return_failure_response_with_error_codes` | Mock success:false with error-codes |
| `should_throw_HttpRequestException_on_non_success_status` | Mock 500 status |
| `should_throw_InvalidOperationException_when_response_null` | Mock empty body |
| `should_log_failure_when_not_success` | Logger called on failure |

### HTTP Client Configuration

| Test Case | Description |
|-----------|-------------|
| `should_use_correct_endpoint` | POST to recaptcha/api/siteverify |
| `should_use_configured_base_url` | Options.VerifyBaseUrl used |
| `should_respect_cancellation_token` | CancellationToken honored |

---

## 7. ReCaptchaSiteVerifyV3 Tests (HTTP Mocking)

**File:** `tests/Framework.Recaptcha.Tests.Unit/V3/ReCaptchaSiteVerifyV3Tests.cs`

Same structure as V2 tests, plus:

| Test Case | Description |
|-----------|-------------|
| `should_include_score_in_success_response` | Score parsed from JSON |
| `should_include_action_in_success_response` | Action parsed from JSON |

---

## 8. TagHelper Tests

### ReCaptchaV2DivTagHelper Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/V2/TagHelpers/ReCaptchaV2DivTagHelperTests.cs`

| Test Case | Properties Set | Expected Output |
|-----------|----------------|-----------------|
| `should_render_div_with_class_and_sitekey` | None | div.g-recaptcha[data-sitekey] |
| `should_include_badge_attribute` | Badge="bottomleft" | data-badge="bottomleft" |
| `should_include_theme_attribute` | Theme="dark" | data-theme="dark" |
| `should_include_size_attribute` | Size="compact" | data-size="compact" |
| `should_include_tabindex_attribute` | TabIndex="1" | data-tabindex="1" |
| `should_include_callback_attribute` | Callback="onSuccess" | data-callback="onSuccess" |
| `should_include_expired_callback_attribute` | ExpiredCallback="onExpired" | data-expired-callback="onExpired" |
| `should_include_error_callback_attribute` | ErrorCallback="onError" | data-error-callback="onError" |
| `should_omit_empty_attributes` | Badge="" | No data-badge attribute |

### ReCaptchaV2ElementTagHelper Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/V2/TagHelpers/ReCaptchaV2ElementTagHelperTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_add_recaptcha_attributes_to_any_element` | Works on button, div, etc |
| `should_add_class_and_sitekey` | g-recaptcha class added |
| `should_preserve_existing_attributes` | Original attributes kept |

### ReCaptchaV2ScriptTagHelper Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/V2/TagHelpers/ReCaptchaV2ScriptTagHelperTests.cs`

| Test Case | Properties | Expected |
|-----------|------------|----------|
| `should_render_script_tag_with_src` | Default | script tag with api.js URL |
| `should_include_async_by_default` | ScriptAsync=true | async attribute |
| `should_include_defer_by_default` | ScriptDefer=true | defer attribute |
| `should_omit_async_when_false` | ScriptAsync=false | No async |
| `should_omit_defer_when_false` | ScriptDefer=false | No defer |
| `should_include_onload_param` | Onload="init" | &onload=init |
| `should_include_render_param` | Render="explicit" | &render=explicit |
| `should_url_encode_onload` | Onload="my func" | &onload=my%20func |
| `should_include_hl_param_with_language_code` | Provider returns "en-US" | ?hl=en-US |
| `should_add_hide_badge_style` | HideBadge=true | style tag with visibility:hidden |
| `should_not_add_style_when_HideBadge_false` | HideBadge=false | No style tag |

### ReCaptchaV3ScriptTagHelper Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/V3/TagHelpers/ReCaptchaV3ScriptTagHelperTests.cs`

| Test Case | Properties | Expected |
|-----------|------------|----------|
| `should_render_script_with_render_param` | Default | &render=SITEKEY |
| `should_include_hl_param` | Provider returns "ar-SA" | ?hl=ar-SA |
| `should_add_hide_badge_style` | HideBadge=true | style tag |

### ReCaptchaV3ScriptJsTagHelper Tests (Security Critical)

**File:** `tests/Framework.Recaptcha.Tests.Unit/V3/TagHelpers/ReCaptchaV3ScriptJsTagHelperTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_render_grecaptcha_ready_script` | Output contains grecaptcha.ready |
| `should_include_sitekey_in_execute` | SiteKey in grecaptcha.execute |
| `should_include_action_when_provided` | Action="login" in options |
| `should_url_encode_action` | Action with special chars encoded |
| `should_auto_execute_when_Execute_true` | grecaptcha.reExecute() called |
| `should_not_auto_execute_when_Execute_false` | No auto reExecute |
| `should_include_callback_when_Execute_true` | Callback in .then() |
| `should_use_callback_param_when_Execute_false` | callback param in reExecute |

#### XSS Prevention Tests (Security Critical)

| Test Case | Callback | Expected |
|-----------|----------|----------|
| `should_allow_valid_js_identifier` | "myCallback" | No exception |
| `should_allow_underscore_prefix` | "_callback" | No exception |
| `should_allow_numbers_after_first_char` | "callback123" | No exception |
| `should_throw_for_script_injection` | "<script>alert(1)</script>" | InvalidOperationException |
| `should_throw_for_semicolon` | "foo;alert(1)" | InvalidOperationException |
| `should_throw_for_parentheses` | "foo()" | InvalidOperationException |
| `should_throw_for_space` | "foo bar" | InvalidOperationException |
| `should_throw_for_dash` | "foo-bar" | InvalidOperationException |
| `should_throw_for_starting_number` | "123callback" | InvalidOperationException |
| `should_encode_action_to_prevent_XSS` | Action="';alert(1);//" | Encoded in output |

---

## 9. Setup/DI Tests

**File:** `tests/Framework.Recaptcha.Tests.Unit/SetupTests.cs`

### AddReCaptchaV2 Tests

| Test Case | Description |
|-----------|-------------|
| `should_register_IReCaptchaSiteVerifyV2` | Service resolvable |
| `should_register_IReCaptchaLanguageCodeProvider` | Service resolvable |
| `should_configure_named_HttpClient` | HttpClient configured with V2Name |
| `should_configure_options_with_V2Name` | Options accessible via V2Name |
| `should_use_custom_configureClient` | Custom HttpClient config applied |
| `should_add_standard_resilience_handler` | Resilience handler registered |
| `should_use_custom_resilience_options` | Custom resilience applied |
| `should_not_throw_when_setupAction_null` | null setupAction allowed |

### AddReCaptchaV3 Tests

| Test Case | Description |
|-----------|-------------|
| `should_register_IReCaptchaSiteVerifyV3` | Service resolvable |
| `should_configure_named_HttpClient` | HttpClient configured with V3Name |
| `should_configure_options_with_V3Name` | Options accessible via V3Name |
| `should_support_IServiceProvider_overload` | Second overload works |

### TryAdd Behavior Tests

| Test Case | Description |
|-----------|-------------|
| `should_not_replace_existing_language_provider` | TryAddTransient behavior |

---

## Test Infrastructure

### Required Test Project Setup

```xml
<!-- tests/Framework.Recaptcha.Tests.Unit/Framework.Recaptcha.Tests.Unit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Framework.Recaptcha\Framework.Recaptcha.csproj" />
    <ProjectReference Include="..\Framework.Testing\Framework.Testing.csproj" />
  </ItemGroup>
</Project>
```

### HTTP Mock Helper

```csharp
public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        => _handler = handler;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        => Task.FromResult(_handler(request));
}
```

### TagHelper Test Helper

```csharp
public static async Task<TagHelperOutput> ProcessTagHelperAsync<T>(
    T tagHelper,
    TagHelperAttributeList attributes = null) where T : TagHelper
{
    var context = new TagHelperContext(
        tagName: "test",
        allAttributes: attributes ?? new TagHelperAttributeList(),
        items: new Dictionary<object, object>(),
        uniqueId: Guid.NewGuid().ToString());

    var output = new TagHelperOutput(
        tagName: "test",
        attributes: new TagHelperAttributeList(),
        getChildContentAsync: (_, _) => Task.FromResult<TagHelperContent>(
            new DefaultTagHelperContent()));

    await tagHelper.ProcessAsync(context, output);
    return output;
}
```

---

## Test Summary

| Component | Test Count | Priority |
|-----------|------------|----------|
| ReCaptchaV3ScriptJsTagHelper (XSS) | 15 | Critical (Security) |
| ReCaptchaSiteVerifyV2/V3 | 24 | High |
| ReCaptchaOptionsValidator | 8 | High |
| Response Error Parsing | 20 | High |
| StringExtensions | 10 | Medium |
| ReCaptchaErrorExtensions | 8 | Medium |
| TagHelpers (V2 Div/Element/Script) | 25 | Medium |
| TagHelpers (V3 Script) | 8 | Medium |
| Setup/DI | 15 | Medium |
| CultureInfoLanguageCodeProvider | 3 | Low |
| **Total** | **~136** | - |

---

## Priority Order

1. **ReCaptchaV3ScriptJsTagHelper XSS Tests** - Security-critical: prevents JavaScript injection
2. **ReCaptchaSiteVerifyV2/V3 Tests** - Core API verification logic with HTTP mocking
3. **ReCaptchaOptionsValidator Tests** - Configuration validation
4. **Response Error Parsing Tests** - Ensure error codes mapped correctly
5. **StringExtensions Tests** - URL manipulation utilities
6. **ReCaptchaErrorExtensions Tests** - Error code mapping
7. **TagHelper Tests** - HTML output correctness
8. **Setup/DI Tests** - Service registration
9. **CultureInfoLanguageCodeProvider Tests** - Simple culture handling

---

## Testing Notes

### External API Dependencies

**Google reCAPTCHA API cannot be called in unit tests.** All verification service tests must use HTTP mocking via `MockHttpMessageHandler` or similar approach.

### What Can Be Unit Tested

- Options validation
- Error code parsing
- String utilities
- TagHelper HTML output
- DI registration
- HTTP request formation (via mocked handler inspection)
- JSON response deserialization

### What Requires Integration Tests

- Actual Google API calls (manual/smoke tests only)
- End-to-end form submission with real tokens
- Browser-based reCAPTCHA widget interaction

### Security Considerations

1. **XSS Prevention in V3ScriptJsTagHelper** - The `_ValidJsIdentifierRegex()` validation is critical
   - Callback parameter must be a valid JS identifier
   - Action parameter is JavaScript-encoded via `JavaScriptEncoder.Default.Encode`
   - Tests must verify malicious inputs are rejected

2. **Options Secrets** - SiteSecret is sensitive
   - Ensure it's sent only in POST body, not in logs
   - Verify logging doesn't expose secrets

3. **URL Encoding** - TagHelpers use `Uri.EscapeDataString`
   - Test special characters in language codes, onload params
