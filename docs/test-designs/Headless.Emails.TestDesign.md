# Test Case Design: Headless.Emails (All Packages)

**Packages:**
- `src/Headless.Emails.Abstractions`
- `src/Headless.Emails.Core`
- `src/Headless.Emails.Aws`
- `src/Headless.Emails.Mailkit`
- `src/Headless.Emails.Dev`

**Test Projects:** None (new projects needed)
**Generated:** 2026-01-25

## Package Analysis

### Headless.Emails.Abstractions

| File | Purpose | Testable |
|------|---------|----------|
| `IEmailSender.cs` | Email sender interface | Low (interface) |
| `Contracts/SendSingleEmailRequest.cs` | Request record with From, Destination, Subject, attachments | Medium |
| `Contracts/SendSingleEmailResponse.cs` | Response with success/failure state | Medium |

### Headless.Emails.Core

| File | Purpose | Testable |
|------|---------|----------|
| `EmailToMimMessageConverter.cs` | Convert SendSingleEmailRequest to MimeMessage | High |

### Headless.Emails.Aws

| File | Purpose | Testable |
|------|---------|----------|
| `AwsSesEmailSender.cs` | IEmailSender using AWS SES v2 | High (integration) |
| `Setup.cs` | DI registration | Low |

### Headless.Emails.Mailkit

| File | Purpose | Testable |
|------|---------|----------|
| `MailkitEmailSender.cs` | IEmailSender using SMTP via MailKit | High (integration) |
| `MailkitSmtpOptions.cs` | SMTP configuration options | Medium |
| `SmtpClientPooledObjectPolicy.cs` | ObjectPool policy for SmtpClient | Medium |
| `Setup.cs` | DI registration | Low |

### Headless.Emails.Dev

| File | Purpose | Testable |
|------|---------|----------|
| `DevEmailSender.cs` | File-based email sender for development | High |
| `NoopEmailSender.cs` | No-op email sender | Low |
| `Setup.cs` | DI registration | Low |

## Current Test Coverage

**Existing Tests:** None

---

## Missing: EmailRequestAddress Tests

**File:** `tests/Headless.Emails.Tests.Unit/Contracts/EmailRequestAddressTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_create_with_email_only` | Constructor with email |
| `should_create_with_email_and_display_name` | Constructor with both |
| `should_implicitly_convert_from_string` | Implicit operator |
| `should_convert_from_string_explicitly` | FromString method |
| `should_format_toString_with_email_only` | ToString without display name |
| `should_format_toString_with_display_name` | ToString with "Name <email>" format |

---

## Missing: EmailRequestDestination Tests

**File:** `tests/Headless.Emails.Tests.Unit/Contracts/EmailRequestDestinationTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_require_to_addresses` | Required property |
| `should_default_bcc_to_empty` | Default BccAddresses |
| `should_default_cc_to_empty` | Default CcAddresses |

---

## Missing: SendSingleEmailRequest Tests

**File:** `tests/Headless.Emails.Tests.Unit/Contracts/SendSingleEmailRequestTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_require_from_address` | Required From |
| `should_require_destination` | Required Destination |
| `should_require_subject` | Required Subject |
| `should_default_attachments_to_empty` | Default [] |
| `should_allow_null_message_html` | Optional MessageHtml |
| `should_allow_null_message_text` | Optional MessageText |

---

## Missing: EmailToMimMessageConverter Tests

**File:** `tests/Headless.Emails.Tests.Unit/EmailToMimMessageConverterTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_set_subject` | Subject mapping |
| `should_set_from_address` | From mapping |
| `should_set_from_address_with_display_name` | From with DisplayName |
| `should_set_to_addresses` | To addresses |
| `should_set_cc_addresses` | CC addresses |
| `should_set_bcc_addresses` | BCC addresses |
| `should_set_text_body` | MessageText mapping |
| `should_set_html_body` | MessageHtml mapping |
| `should_set_both_text_and_html_body` | Both bodies |
| `should_add_attachments` | Attachment handling |
| `should_dispose_message_on_exception` | Exception safety |

---

## Missing: DevEmailSender Tests

**File:** `tests/Headless.Emails.Tests.Unit/DevEmailSenderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_write_email_to_file` | File output |
| `should_include_from_address` | From in output |
| `should_include_to_addresses` | To in output |
| `should_include_cc_when_present` | CC in output |
| `should_include_bcc_when_present` | BCC in output |
| `should_include_subject` | Subject in output |
| `should_include_attachments_when_present` | Attachment names |
| `should_prefer_text_message_over_html` | Text priority |
| `should_use_html_when_no_text` | HTML fallback |
| `should_append_to_existing_file` | AppendAllText |
| `should_return_success` | SendSingleEmailResponse.Succeeded |
| `should_throw_when_file_path_is_null` | Argument validation |
| `should_throw_when_file_path_is_empty` | Argument validation |

---

## Missing: NoopEmailSender Tests

**File:** `tests/Headless.Emails.Tests.Unit/NoopEmailSenderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_return_success` | Always succeeds |
| `should_not_throw` | No-op behavior |

---

## Missing: MailkitEmailSender Tests (Integration)

**File:** `tests/Headless.Emails.Mailkit.Tests.Integration/MailkitEmailSenderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_send_email_successfully` | Happy path |
| `should_throw_when_no_message_content` | Validation |
| `should_return_failed_on_smtp_command_error` | SmtpCommandException handling |
| `should_return_failed_on_smtp_protocol_error` | SmtpProtocolException handling |
| `should_throw_on_authentication_failure` | AuthenticationException |
| `should_connect_when_not_connected` | _EnsureConnectedAsync |
| `should_skip_connect_when_already_connected` | Reuse connection |
| `should_authenticate_when_credentials_present` | HasCredentials |
| `should_return_client_to_pool` | ObjectPool.Return |
| `should_return_client_to_pool_on_error` | Finally block |

---

## Missing: AwsSesEmailSender Tests (Integration)

**File:** `tests/Headless.Emails.Aws.Tests.Integration/AwsSesEmailSenderTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_send_simple_email` | Without attachments |
| `should_send_raw_email_with_attachments` | With attachments |
| `should_return_success_on_200` | IsSuccessStatusCode |
| `should_return_failed_on_non_success` | Non-200 response |
| `should_throw_on_message_rejected` | MessageRejectedException |
| `should_throw_on_bad_request` | BadRequestException |
| `should_throw_on_not_found` | NotFoundException |
| `should_throw_on_account_suspended` | AccountSuspendedException |
| `should_throw_on_domain_not_verified` | MailFromDomainNotVerifiedException |
| `should_throw_on_limit_exceeded` | LimitExceededException |
| `should_throw_on_too_many_requests` | TooManyRequestsException |
| `should_throw_on_sending_paused` | SendingPausedException |

---

## Missing: SmtpClientPooledObjectPolicy Tests

**File:** `tests/Headless.Emails.Tests.Unit/SmtpClientPooledObjectPolicyTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_create_new_smtp_client` | Create method |
| `should_return_true_when_connected` | Return connected client |
| `should_return_false_when_disconnected` | Reject disconnected client |
| `should_disconnect_on_return_false` | Cleanup disconnected |

---

## Test Infrastructure

### Required Test Project Setup

```xml
<!-- tests/Headless.Emails.Tests.Unit/Headless.Emails.Tests.Unit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Headless.Emails.Abstractions\Headless.Emails.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\Headless.Emails.Core\Headless.Emails.Core.csproj" />
    <ProjectReference Include="..\..\src\Headless.Emails.Dev\Headless.Emails.Dev.csproj" />
    <ProjectReference Include="..\Headless.Testing\Headless.Testing.csproj" />
  </ItemGroup>
</Project>
```

### Test Helpers

```csharp
public static class EmailTestHelpers
{
    public static SendSingleEmailRequest CreateValidRequest(
        string? from = "sender@example.com",
        string? to = "recipient@example.com",
        string? subject = "Test Subject",
        string? messageText = "Test message",
        string? messageHtml = null)
    {
        return new SendSingleEmailRequest
        {
            From = from!,
            Destination = new EmailRequestDestination
            {
                ToAddresses = [to!]
            },
            Subject = subject!,
            MessageText = messageText,
            MessageHtml = messageHtml,
        };
    }
}
```

---

## Test Summary

| Component | Existing | New Unit | New Integration | Total |
|-----------|----------|----------|-----------------|-------|
| EmailRequestAddress | 0 | 6 | 0 | 6 |
| EmailRequestDestination | 0 | 3 | 0 | 3 |
| SendSingleEmailRequest | 0 | 6 | 0 | 6 |
| EmailToMimMessageConverter | 0 | 11 | 0 | 11 |
| DevEmailSender | 0 | 13 | 0 | 13 |
| NoopEmailSender | 0 | 2 | 0 | 2 |
| MailkitEmailSender | 0 | 0 | 10 | 10 |
| AwsSesEmailSender | 0 | 0 | 12 | 12 |
| SmtpClientPooledObjectPolicy | 0 | 4 | 0 | 4 |
| **Total** | **0** | **45** | **22** | **67** |

---

## Priority Order

1. **EmailToMimMessageConverter** - Core conversion logic used by all senders
2. **DevEmailSender** - Development testing
3. **Contract tests** - Request/Response validation
4. **MailkitEmailSender** (integration) - SMTP provider
5. **AwsSesEmailSender** (integration) - AWS provider

---

## Notes

1. **IEmailSender interface** - Single method `SendAsync(SendSingleEmailRequest, CancellationToken)`
2. **EmailRequestAddress** - Supports implicit string conversion and formatted ToString
3. **DevEmailSender** - Writes emails to file for development/debugging
4. **NoopEmailSender** - Does nothing, always succeeds
5. **MailkitEmailSender** - Uses ObjectPool for SmtpClient connection pooling
6. **AwsSesEmailSender** - Uses simple API for no attachments, raw MIME for attachments
7. **Exception handling** - AWS SES has specific exception types to rethrow

---

## IEmailSender Architecture

```
IEmailSender
├── SendAsync(SendSingleEmailRequest, CancellationToken)
└── Returns ValueTask<SendSingleEmailResponse>

Implementations:
├── AwsSesEmailSender (AWS SES v2)
│   ├── Simple API (no attachments)
│   └── Raw MIME (with attachments)
├── MailkitEmailSender (SMTP)
│   ├── ObjectPool<SmtpClient>
│   └── Auto-reconnect logic
├── DevEmailSender (File)
│   └── Appends to file
└── NoopEmailSender
    └── Always succeeds
```

---

## Recommendation

**Medium Priority** - Email sending is a critical feature. Unit tests for:
- Contract validation
- MimeMessage conversion
- DevEmailSender file output

Integration tests (lower priority, require infrastructure):
- MailkitEmailSender with test SMTP server
- AwsSesEmailSender with LocalStack or real AWS
