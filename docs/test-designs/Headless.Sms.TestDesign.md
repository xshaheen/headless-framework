# Test Case Design: Headless.Sms (All Packages)

**Packages:**
- `src/Headless.Sms.Abstractions`
- `src/Headless.Sms.Aws`
- `src/Headless.Sms.Cequens`
- `src/Headless.Sms.Connekio`
- `src/Headless.Sms.Dev`
- `src/Headless.Sms.Infobip`
- `src/Headless.Sms.Twilio`
- `src/Headless.Sms.VictoryLink`
- `src/Headless.Sms.Vodafone`

**Test Projects:** None (new projects needed)
**Generated:** 2026-01-25

## Package Analysis

### Headless.Sms.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `ISmsSender.cs` | SMS sender interface | Low (interface) |
| `Contracts/SendSingleSmsRequest.cs` | SMS request with destinations | High |
| `Contracts/SendSingleSmsResponse.cs` | SMS response with status | Medium |

### Headless.Sms.Dev

| File | Purpose | Testable |
|------|---------|----------|
| `DevSmsSender.cs` | File-based SMS sender | High |
| `NoopSmsSender.cs` | No-op SMS sender | Low |
| `Setup.cs` | DI registration | Low |

### Headless.Sms.Aws

| File | Purpose | Testable |
|------|---------|----------|
| `AwsSnsSmsSender.cs` | AWS SNS SMS sender | High (integration) |
| `AwsSnsSmsOptions.cs` | AWS configuration | Medium |
| `Setup.cs` | DI registration | Low |

### Provider Packages (Integration Only)

| Package | Sender Class | Testable |
|---------|-------------|----------|
| `Headless.Sms.Cequens` | `CequensSmsSender.cs` | Integration |
| `Headless.Sms.Connekio` | `ConnekioSmsSender.cs` | Integration |
| `Headless.Sms.Infobip` | `InfobipSmsSender.cs` | Integration |
| `Headless.Sms.Twilio` | `TwilioSmsSender.cs` | Integration |
| `Headless.Sms.VictoryLink` | `VictoryLinkSmsSender.cs` | Integration |
| `Headless.Sms.Vodafone` | `VodafoneSmsSender.cs` | Integration |

## Current Test Coverage

**Existing Tests:** None

---

## Missing: SmsRequestDestination Tests

**File:** `tests/Headless.Sms.Tests.Unit/Contracts/SmsRequestDestinationTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_store_code_and_number` | Constructor properties |
| `should_format_toString_without_plus` | Default ToString |
| `should_format_toString_with_plus` | ToString(hasPlusPrefix: true) |
| `should_use_invariant_culture` | Culture formatting |

---

## Missing: SendSingleSmsRequest Tests

**File:** `tests/Headless.Sms.Tests.Unit/Contracts/SendSingleSmsRequestTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_require_destinations` | Required property |
| `should_require_text` | Required property |
| `should_allow_null_message_id` | Optional MessageId |
| `should_allow_null_properties` | Optional Properties |
| `should_return_false_for_is_batch_with_single` | IsBatch = false |
| `should_return_true_for_is_batch_with_multiple` | IsBatch = true |

---

## Missing: DevSmsSender Tests

**File:** `tests/Headless.Sms.Tests.Unit/DevSmsSenderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_write_sms_to_file` | File output |
| `should_include_destination_in_output` | Destination formatting |
| `should_include_text_in_output` | Message text |
| `should_include_message_id_when_present` | Optional MessageId |
| `should_include_properties_when_present` | Optional Properties |
| `should_append_to_existing_file` | AppendAllTextAsync |
| `should_return_success` | SendSingleSmsResponse.Succeeded |
| `should_throw_when_file_path_is_null` | Argument validation |

---

## Missing: NoopSmsSender Tests

**File:** `tests/Headless.Sms.Tests.Unit/NoopSmsSenderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_return_success` | Always succeeds |
| `should_not_throw` | No-op behavior |

---

## Missing: AwsSnsSmsSender Tests (Integration)

**File:** `tests/Headless.Sms.Aws.Tests.Integration/AwsSnsSmsSenderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_send_sms_successfully` | Happy path |
| `should_return_message_id` | AWS message ID |
| `should_handle_invalid_phone_number` | Validation error |
| `should_handle_rate_limiting` | TooManyRequestsException |

---

## Test Infrastructure

### Test Helpers

```csharp
public static class SmsTestHelpers
{
    public static SendSingleSmsRequest CreateValidRequest(
        int countryCode = 1,
        string number = "5551234567",
        string text = "Test message")
    {
        return new SendSingleSmsRequest
        {
            Destinations = [new SmsRequestDestination(countryCode, number)],
            Text = text,
        };
    }

    public static SendSingleSmsRequest CreateBatchRequest(
        params (int Code, string Number)[] destinations)
    {
        return new SendSingleSmsRequest
        {
            Destinations = destinations.Select(d => new SmsRequestDestination(d.Code, d.Number)).ToList(),
            Text = "Batch message",
        };
    }
}
```

---

## Test Summary

| Component | Existing | New Unit | New Integration | Total |
|-----------|----------|----------|-----------------|-------|
| SmsRequestDestination | 0 | 4 | 0 | 4 |
| SendSingleSmsRequest | 0 | 6 | 0 | 6 |
| DevSmsSender | 0 | 8 | 0 | 8 |
| NoopSmsSender | 0 | 2 | 0 | 2 |
| AwsSnsSmsSender | 0 | 0 | 4 | 4 |
| Other providers | 0 | 0 | ~24 | ~24 |
| **Total** | **0** | **20** | **~28** | **~48** |

---

## Priority Order

1. **SmsRequestDestination** - Core formatting logic
2. **SendSingleSmsRequest** - IsBatch property logic
3. **DevSmsSender** - Development testing
4. **Provider integrations** - Lower priority (require external services)

---

## Notes

1. **ISmsSender interface** - Single method `SendAsync(SendSingleSmsRequest, CancellationToken)`
2. **SmsRequestDestination** - Record with Code (country) and Number
3. **IsBatch** - True when Destinations.Count > 1
4. **DevSmsSender** - Writes SMS to JSON file for development
5. **NoopSmsSender** - Does nothing, always succeeds
6. **8 SMS providers** - AWS, Cequens, Connekio, Infobip, Twilio, VictoryLink, Vodafone, Dev

---

## ISmsSender Architecture

```
ISmsSender
├── SendAsync(SendSingleSmsRequest, CancellationToken)
└── Returns ValueTask<SendSingleSmsResponse>

Implementations:
├── AwsSnsSmsSender (AWS SNS)
├── CequensSmsSender (Cequens API)
├── ConnekioSmsSender (Connekio API)
├── InfobipSmsSender (Infobip API)
├── TwilioSmsSender (Twilio API)
├── VictoryLinkSmsSender (VictoryLink API)
├── VodafoneSmsSender (Vodafone API)
├── DevSmsSender (File)
└── NoopSmsSender (No-op)

Request:
├── Destinations: IReadOnlyList<SmsRequestDestination>
├── Text: string
├── MessageId?: string
├── Properties?: IDictionary<string, object>
└── IsBatch: bool (computed)
```

---

## Recommendation

**Low Priority** - SMS providers are integration-heavy. Unit tests for:
- Destination formatting
- Request validation
- DevSmsSender file output

Integration tests require external SMS provider accounts or mock servers.
