# Test Case Design: Headless.Api.Logging.Serilog

**Package:** `src/Headless.Api.Logging.Serilog`
**Test Projects:** None existing - **needs creation**
**Generated:** 2026-01-25

## Package Analysis

| File | Purpose | Testable |
|------|---------|----------|
| `SanitizedHeaderEnricher.cs` | Enricher that sanitizes HTTP headers for logging | High |
| `SanitizedHeaderEnricherExtensions.cs` | Extension methods to configure the enricher | Medium |
| `SerilogEnrichersMiddleware.cs` | Middleware to push user context to log context | High |
| `ApiSerilogFactory.cs` | Factory methods for API-specific logger configuration | Medium |
| `AddSerilogExtensions.cs` | DI registration extensions | Low |

---

## 1. SanitizedHeaderEnricher Tests

**File:** `tests/Headless.Api.Logging.Serilog.Tests.Unit/Enrichers/SanitizedHeaderEnricherTests.cs`

### Happy Path Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_add_header_value_as_property` | Valid header | Property added with value |
| `should_use_custom_property_name` | propertyName specified | Uses custom name |
| `should_use_header_name_without_dashes_as_default_property` | No propertyName | "User-Agent" → "UserAgent" |

### Sanitization Tests

| Test Case | Input | Expected Output |
|-----------|-------|-----------------|
| `should_remove_newline_characters` | "Value\r\nInjected" | "ValueInjected" |
| `should_remove_carriage_return` | "Value\rTest" | "ValueTest" |
| `should_remove_line_feed` | "Value\nTest" | "ValueTest" |
| `should_remove_ANSI_escape_sequences` | "Value\x1b[31mRed" | "ValueRed" |
| `should_remove_ANSI_OSC_sequences` | "Value\x1b]0;Title\x07Rest" | "ValueRest" |
| `should_remove_control_characters` | "Value\x00\x01\x1fTest" | "ValueTest" |
| `should_preserve_tab_character` | "Value\tTab" | "Value\tTab" |
| `should_truncate_to_max_length` | 600 chars, maxLength=512 | 512 chars |
| `should_not_truncate_when_under_max_length` | 100 chars, maxLength=512 | 100 chars |
| `should_handle_exactly_max_length` | 512 chars, maxLength=512 | 512 chars |

### Edge Case Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_not_add_property_when_no_HttpContext` | contextAccessor.HttpContext=null | No property added |
| `should_not_add_property_when_header_missing` | Header not in request | No property added |
| `should_not_add_property_when_header_empty` | Header value="" | No property added |
| `should_use_AddPropertyIfAbsent` | Property already exists | Not overwritten |

### Log Injection Prevention Tests

| Test Case | Input | Description |
|-----------|-------|-------------|
| `should_prevent_log_line_injection` | "Value\nFake: log" | Newlines removed |
| `should_prevent_terminal_color_injection` | "\x1b[31mRed\x1b[0m" | ANSI codes removed |
| `should_prevent_terminal_title_injection` | "\x1b]0;Malicious\x07" | OSC sequences removed |
| `should_handle_combined_injection_attempts` | All techniques combined | All removed |

### Property Name Sanitization Tests

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_convert_dashed_header_to_property_name` | "X-Custom-Header" | "XCustomHeader" |
| `should_preserve_non_dashed_header_name` | "Authorization" | "Authorization" |

---

## 2. SanitizedHeaderEnricherExtensions Tests

**File:** `tests/Headless.Api.Logging.Serilog.Tests.Unit/Enrichers/SanitizedHeaderEnricherExtensionsTests.cs`

### WithSanitizedRequestHeader (without ServiceProvider) Tests

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_enrichmentConfiguration_null` | ArgumentNullException |
| `should_throw_when_headerName_null` | ArgumentNullException |
| `should_throw_when_headerName_empty` | ArgumentException |
| `should_throw_when_headerName_whitespace` | ArgumentException |
| `should_create_enricher_with_default_max_length` | maxLength=512 |
| `should_create_enricher_with_custom_property_name` | propertyName used |
| `should_create_enricher_with_custom_max_length` | Custom maxLength used |
| `should_return_LoggerConfiguration_for_chaining` | Returns configuration |

### WithSanitizedRequestHeader (with ServiceProvider) Tests

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_serviceProvider_null` | ArgumentNullException |
| `should_resolve_IHttpContextAccessor_from_services` | GetRequiredService called |
| `should_use_resolved_context_accessor` | Accessor used in enricher |

---

## 3. SerilogEnrichersMiddleware Tests

**File:** `tests/Headless.Api.Logging.Serilog.Tests.Unit/SerilogEnrichersMiddlewareTests.cs`

### Property Pushing Tests

| Test Case | Context State | Expected Properties |
|-----------|---------------|---------------------|
| `should_push_UserId_when_present` | UserId="123" | UserId="123" in context |
| `should_push_AccountId_when_present` | AccountId="456" | AccountId="456" in context |
| `should_push_CorrelationId_when_present` | CorrelationId="abc" | CorrelationId="abc" in context |
| `should_push_all_properties_when_all_present` | All values set | All 3 properties in context |

### Null Handling Tests

| Test Case | Context State | Expected |
|-----------|---------------|----------|
| `should_not_push_UserId_when_null` | UserId=null | No UserId property |
| `should_not_push_AccountId_when_null` | AccountId=null | No AccountId property |
| `should_not_push_CorrelationId_when_null` | CorrelationId=null | No CorrelationId property |
| `should_handle_all_null_values` | All null | No properties pushed |

### Middleware Pipeline Tests

| Test Case | Description |
|-----------|-------------|
| `should_call_next_delegate` | next() invoked |
| `should_dispose_log_context_after_next` | Properties removed after pipeline |
| `should_await_next_with_AnyContext` | ConfigureAwait(false) used |

---

## 4. ApiSerilogFactory Tests

**File:** `tests/Headless.Api.Logging.Serilog.Tests.Unit/ApiSerilogFactoryTests.cs`

### Bootstrap Logger Tests

| Test Case | Description |
|-----------|-------------|
| `CreateApiBootstrapLogger_should_return_logger` | Non-null Logger returned |
| `CreateApiBootstrapLoggerConfiguration_should_return_configuration` | Non-null configuration |

### API Logger Tests

| Test Case | Description |
|-----------|-------------|
| `CreateApiLogger_with_WebApplication_should_create_logger` | Logger created from app |
| `CreateApiLogger_should_accept_null_services` | services=null works |
| `CreateApiLogger_should_use_default_options_when_null` | options=null creates defaults |

### Configuration Tests

| Test Case | Description |
|-----------|-------------|
| `ConfigureApiLoggerConfiguration_should_add_ClientIp_enricher` | WithClientIp called |
| `ConfigureApiLoggerConfiguration_should_add_UserAgent_header_enricher` | User-Agent enricher |
| `ConfigureApiLoggerConfiguration_should_add_ClientVersion_header_enricher` | X-Client-Version enricher |
| `ConfigureApiLoggerConfiguration_should_add_ApiVersion_header_enricher` | X-Api-Version enricher |
| `ConfigureApiLoggerConfiguration_should_use_options_MaxHeaderLength` | Custom maxLength from options |
| `ConfigureApiLoggerConfiguration_should_call_base_configuration` | ConfigureReloadableLoggerConfiguration called |

### Constants Tests

| Test Case | Description |
|-----------|-------------|
| `OutputTemplate_should_match_SerilogFactory` | OutputTemplate equals base |

---

## 5. AddSerilogExtensions Tests

**File:** `tests/Headless.Api.Logging.Serilog.Tests.Unit/AddSerilogExtensionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `AddHeadlessSerilogEnrichers_should_register_middleware_as_scoped` | Scoped registration |
| `AddHeadlessSerilogEnrichers_should_return_services_for_chaining` | Returns IServiceCollection |
| `UseHeadlessSerilogEnrichers_should_add_middleware` | Middleware registered |
| `UseHeadlessSerilogEnrichers_should_return_builder_for_chaining` | Returns IApplicationBuilder |

---

## Test Infrastructure

### Required Test Project Setup

```xml
<!-- tests/Headless.Api.Logging.Serilog.Tests.Unit/Headless.Api.Logging.Serilog.Tests.Unit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Framework.Api.Logging.Serilog\Framework.Api.Logging.Serilog.csproj" />
    <ProjectReference Include="..\Framework.Testing\Framework.Testing.csproj" />
  </ItemGroup>
</Project>
```

### Test Helpers

```csharp
public static class SerilogTestHelpers
{
    public static IHttpContextAccessor CreateContextAccessor(
        Dictionary<string, string>? headers = null)
    {
        var httpContext = new DefaultHttpContext();

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
            {
                httpContext.Request.Headers[key] = value;
            }
        }

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        return accessor;
    }

    public static (LogEvent LogEvent, ILogEventPropertyFactory Factory) CreateLogEventContext()
    {
        var logEvent = new LogEvent(
            DateTimeOffset.Now,
            LogEventLevel.Information,
            null,
            MessageTemplate.Empty,
            []);

        var factory = Substitute.For<ILogEventPropertyFactory>();
        factory.CreateProperty(Arg.Any<string>(), Arg.Any<object?>())
            .Returns(callInfo => new LogEventProperty(
                callInfo.ArgAt<string>(0),
                new ScalarValue(callInfo.ArgAt<object?>(1))));

        return (logEvent, factory);
    }

    public static IRequestContext CreateMockRequestContext(
        string? userId = null,
        string? accountId = null,
        string? correlationId = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.AccountId.Returns(accountId);

        var requestContext = Substitute.For<IRequestContext>();
        requestContext.User.Returns(user);
        requestContext.CorrelationId.Returns(correlationId);

        return requestContext;
    }
}
```

### Log Context Verification Helper

```csharp
public class LogContextCapture : IDisposable
{
    private readonly List<LogEvent> _events = [];
    private readonly IDisposable _subscription;

    public LogContextCapture()
    {
        var sink = new DelegatingSink(e => _events.Add(e));
        _subscription = LogContext.PushProperty("TestCapture", true);

        // Configure a test logger that writes to our sink
        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();
    }

    public IReadOnlyList<LogEvent> Events => _events;

    public void Dispose()
    {
        _subscription.Dispose();
        Log.CloseAndFlush();
    }
}
```

---

## Test Summary

| Component | Test Count | Priority |
|-----------|------------|----------|
| SanitizedHeaderEnricher | 22 | High |
| SanitizedHeaderEnricherExtensions | 11 | Medium |
| SerilogEnrichersMiddleware | 11 | High |
| ApiSerilogFactory | 12 | Medium |
| AddSerilogExtensions | 4 | Low |
| **Total** | **60** | - |

---

## Priority Order

1. **SanitizedHeaderEnricher** - Security-critical: log injection prevention
2. **SerilogEnrichersMiddleware** - Context propagation for observability
3. **SanitizedHeaderEnricherExtensions** - Configuration correctness
4. **ApiSerilogFactory** - Logger setup
5. **AddSerilogExtensions** - Simple DI registration

---

## Security Notes

1. **Log Injection Prevention** - The SanitizedHeaderEnricher is designed to prevent log injection attacks
   - Removes newlines to prevent fake log entries
   - Removes ANSI escape codes to prevent terminal color/title manipulation
   - Removes control characters that could confuse log parsers
   - Truncates to prevent DoS via oversized headers

2. **Regex Timeout** - Both regex patterns have 100ms timeout to prevent ReDoS attacks

3. **Property Preservation** - Uses `AddPropertyIfAbsent` to not overwrite existing properties

---

## Integration Test Considerations

For full integration testing:

**File:** `tests/Headless.Api.Logging.Serilog.Tests.Integration/`

| Test Case | Description |
|-----------|-------------|
| `should_enrich_logs_with_sanitized_headers_in_real_request` | Full pipeline test |
| `should_capture_user_context_in_log_events` | Middleware + Serilog integration |
| `should_work_with_Seq_sink` | Real sink integration |
| `should_work_with_console_sink` | Verify output format |

**Infrastructure:** Use TestServer to create real HTTP pipeline with Serilog configured.
