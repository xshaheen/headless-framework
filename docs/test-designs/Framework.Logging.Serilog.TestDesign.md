# Test Case Design: Framework.Logging.Serilog

**Package:** `src/Framework.Logging.Serilog`
**Test Projects:** None existing - **needs creation**
**Generated:** 2026-01-25

## Package Analysis

| File | Purpose | Testable |
|------|---------|----------|
| `SerilogFactory.cs` | Factory methods for bootstrap and reloadable logger configurations | Medium |
| `SerilogOptions.cs` | Configuration options for Serilog file sinks | Low |

---

## 1. SerilogOptions Tests

**File:** `tests/Framework.Logging.Serilog.Tests.Unit/SerilogOptionsTests.cs`

### Default Values Tests

| Test Case | Description | Expected |
|-----------|-------------|----------|
| `should_default_WriteToFiles_to_true` | Default value | true |
| `should_default_LogDirectory_to_Logs` | Default value | "Logs" |
| `should_default_Buffered_to_true` | Default value | true |
| `should_default_FlushToDiskInterval_to_1_second` | Default value | 1 second |
| `should_default_RollingInterval_to_Day` | Default value | RollingInterval.Day |
| `should_default_RetainedFileCountLimit_to_5` | Default value | 5 |
| `should_default_MaxHeaderLength_to_512` | Default value | 512 |

### Property Assignment Tests

| Test Case | Description |
|-----------|-------------|
| `should_allow_custom_WriteToFiles` | Can set false |
| `should_allow_custom_LogDirectory` | Can set path |
| `should_allow_custom_FlushToDiskInterval` | Can set timespan |
| `should_allow_custom_RollingInterval` | Can set interval |
| `should_allow_custom_RetainedFileCountLimit` | Can set count |

---

## 2. SerilogFactory Tests

**File:** `tests/Framework.Logging.Serilog.Tests.Unit/SerilogFactoryTests.cs`

### OutputTemplate Tests

| Test Case | Description |
|-----------|-------------|
| `should_have_OutputTemplate_with_timestamp` | Contains "{Timestamp:HH:mm:ss.fff zzz}" |
| `should_have_OutputTemplate_with_level` | Contains "{Level:u3}" |
| `should_have_OutputTemplate_with_request_path` | Contains "{RequestPath}" |
| `should_have_OutputTemplate_with_source_context` | Contains "{SourceContext}" |
| `should_have_OutputTemplate_with_message` | Contains "{Message:lj}" |
| `should_have_OutputTemplate_with_exception` | Contains "{Exception}" |

### CreateBootstrapLoggerConfiguration Tests

| Test Case | Description |
|-----------|-------------|
| `should_return_non_null_configuration` | Returns LoggerConfiguration |
| `should_accept_null_options` | No exception with null |
| `should_use_default_options_when_null` | Defaults applied |

### ConfigureBootstrapLoggerConfiguration Tests

| Test Case | Description |
|-----------|-------------|
| `should_return_same_configuration_instance` | Fluent chaining |
| `should_configure_IPAddress_destructure` | Transforms IPAddress to string |
| `should_enrich_with_environment_name` | EnvironmentName enricher |
| `should_enrich_with_thread_id` | ThreadId enricher |
| `should_enrich_with_process_id` | ProcessId enricher |
| `should_enrich_with_process_name` | ProcessName enricher |
| `should_enrich_with_machine_name` | MachineName enricher |
| `should_configure_console_sink` | Console sink added |
| `should_configure_async_file_sink_for_bootstrap` | bootstrap-.log file |
| `should_filter_file_sink_to_fatal_error_warning` | Only high severity |

### CreateReloadableLoggerConfiguration Tests

| Test Case | Description |
|-----------|-------------|
| `should_return_non_null_configuration` | Returns LoggerConfiguration |
| `should_accept_null_services` | services=null works |
| `should_accept_null_options` | options=null works |

### ConfigureReloadableLoggerConfiguration Tests

| Test Case | Description |
|-----------|-------------|
| `should_return_same_configuration_instance` | Fluent chaining |
| `should_read_from_configuration` | ReadFrom.Configuration called |
| `should_read_from_services_when_provided` | ReadFrom.Services called |
| `should_not_read_from_services_when_null` | Skips ReadFrom.Services |
| `should_configure_IPAddress_destructure` | Transforms IPAddress to string |
| `should_enrich_from_log_context` | FromLogContext enricher |
| `should_enrich_with_span` | WithSpan enricher |
| `should_enrich_with_environment_name` | EnvironmentName enricher |
| `should_enrich_with_thread_id` | ThreadId enricher |
| `should_enrich_with_process_id` | ProcessId enricher |
| `should_enrich_with_process_name` | ProcessName enricher |
| `should_enrich_with_machine_name` | MachineName enricher |
| `should_enrich_with_Application_property` | Application property from environment |
| `should_enrich_with_Version_property` | Version from AssemblyInformation |
| `should_enrich_with_CommitHash_property` | CommitNumber from AssemblyInformation |
| `should_use_AnsiConsoleTheme_Code_in_development` | Development theme |
| `should_use_ConsoleTheme_None_in_production` | Production theme |

### File Sink Configuration Tests (WriteToFiles=true)

| Test Case | Description |
|-----------|-------------|
| `should_write_fatal_to_separate_file` | fatal-.log |
| `should_write_error_to_separate_file` | error-.log |
| `should_write_warning_to_separate_file` | warning-.log |
| `should_use_LogDirectory_from_options` | Custom path used |
| `should_use_Buffered_from_options` | Buffered setting |
| `should_use_FlushToDiskInterval_from_options` | Flush interval |
| `should_use_RollingInterval_from_options` | Rolling interval |
| `should_use_RetainedFileCountLimit_from_options` | Retention limit |
| `should_wrap_file_sinks_in_async` | Async wrapper |

### File Sink Configuration Tests (WriteToFiles=false)

| Test Case | Description |
|-----------|-------------|
| `should_not_configure_file_sinks_when_disabled` | No file sinks |

---

## 3. IPAddress Destructure Tests

**File:** `tests/Framework.Logging.Serilog.Tests.Unit/IPAddressDestructureTests.cs`

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_transform_ipv4_address_to_string` | IPAddress.Parse("192.168.1.1") | "192.168.1.1" |
| `should_transform_ipv6_address_to_string` | IPAddress.IPv6Loopback | "::1" |
| `should_transform_loopback_to_string` | IPAddress.Loopback | "127.0.0.1" |
| `should_transform_null_to_empty_string` | null | "" |

---

## Test Infrastructure

### Required Test Project Setup

```xml
<!-- tests/Framework.Logging.Serilog.Tests.Unit/Framework.Logging.Serilog.Tests.Unit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Framework.Logging.Serilog\Framework.Logging.Serilog.csproj" />
    <ProjectReference Include="..\Framework.Testing\Framework.Testing.csproj" />
  </ItemGroup>
</Project>
```

### Test Helpers

```csharp
public static class SerilogTestHelpers
{
    public static IConfiguration CreateEmptyConfiguration()
    {
        return new ConfigurationBuilder().Build();
    }

    public static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    public static IHostEnvironment CreateMockEnvironment(
        string environmentName = "Production",
        string applicationName = "TestApp")
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        environment.ApplicationName.Returns(applicationName);
        return environment;
    }

    public static IHostEnvironment CreateDevelopmentEnvironment() =>
        CreateMockEnvironment(Environments.Development);

    public static IHostEnvironment CreateProductionEnvironment() =>
        CreateMockEnvironment(Environments.Production);
}
```

### Log Event Capture Helper

```csharp
public sealed class TestLogEventSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];

    public IReadOnlyList<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent) => _events.Add(logEvent);

    public bool HasProperty(string name) =>
        _events.Any(e => e.Properties.ContainsKey(name));

    public object? GetPropertyValue(string name) =>
        _events
            .SelectMany(e => e.Properties)
            .FirstOrDefault(p => p.Key == name)
            .Value switch
            {
                ScalarValue sv => sv.Value,
                _ => null
            };
}
```

---

## Test Summary

| Component | Test Count | Priority |
|-----------|------------|----------|
| SerilogOptions | 12 | Low |
| SerilogFactory - Bootstrap | 13 | Medium |
| SerilogFactory - Reloadable | 25 | Medium |
| IPAddress Destructure | 4 | Low |
| **Total** | **54** | - |

---

## Priority Order

1. **ConfigureReloadableLoggerConfiguration** - Main production logger configuration
2. **ConfigureBootstrapLoggerConfiguration** - Early startup logging
3. **File Sink Configuration** - Persistence behavior
4. **SerilogOptions** - Default value verification
5. **IPAddress Destructure** - Edge case handling

---

## Notes

### Configuration-Heavy Package

This package is primarily configuration/factory code. Testing focuses on:
- Verifying enrichers are added
- Verifying sinks are configured correctly
- Verifying options are respected
- Verifying default values

### Testing Challenges

1. **Serilog Internal State** - LoggerConfiguration builds internal state that's hard to inspect directly
2. **Conditional Compilation** - `_WriteToDebug` uses `[Conditional("DEBUG")]`, so it only executes in Debug builds
3. **External Dependencies** - Relies on `AssemblyInformation.Entry` for Version and CommitNumber

### Recommended Testing Approach

1. **Behavioral Tests** - Create logger, write log event, capture and verify output
2. **Integration Tests** - Use TestLogEventSink to capture events and verify enrichments
3. **Snapshot Tests** - Verify OutputTemplate format doesn't change unexpectedly

### What NOT to Test

- Serilog library functionality (already tested by Serilog)
- Enricher packages (WithThreadId, WithProcessId, etc.)
- Sink packages (Console, File, Async)

### Integration Test Considerations

**File:** `tests/Framework.Logging.Serilog.Tests.Integration/`

| Test Case | Description |
|-----------|-------------|
| `should_write_logs_to_file_with_correct_path` | Verify file creation |
| `should_roll_files_at_configured_interval` | Rolling behavior |
| `should_retain_configured_file_count` | Retention behavior |
| `should_flush_at_configured_interval` | Flush timing |
| `should_include_all_enrichers_in_output` | Full enricher chain |

**Infrastructure:** Use temp directory for file sink tests, clean up after.

---

## Recommendation

**Medium Priority** - This is a configuration package. Testing value:
- Ensures defaults don't accidentally change
- Validates option wiring
- Documents expected behavior

Primary value comes from integration tests that verify:
- Log files are created correctly
- Enrichers produce expected output
- Configuration from appsettings.json is read correctly
