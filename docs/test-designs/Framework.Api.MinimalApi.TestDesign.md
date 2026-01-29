# Test Case Design: Headless.Api.MinimalApi

**Package:** `src/Headless.Api.MinimalApi`
**Test Projects:** None existing - **needs creation**
**Generated:** 2026-01-25

## Package Analysis

| File | Purpose | Testable |
|------|---------|----------|
| `MinimalApiExceptionFilter.cs` | Exception handling filter | High |
| `MinimalApiValidatorFilter.cs` | FluentValidation filter | High |
| `ApiResultExtensions.cs` | Result-to-HTTP mapping | High |
| `ConfigureMinimalApiJsonOptions.cs` | JSON config | Low |
| `AddMinimalApiExtensions.cs` | DI setup | Low |
| `RouteBuilderExtensions.cs` | Route helpers | Medium |

---

## 1. MinimalApiExceptionFilter Tests

**File:** `tests/Headless.Api.MinimalApi.Tests.Unit/Filters/MinimalApiExceptionFilterTests.cs`

### Happy Path Tests

| Test Case | Exception | Expected Result |
|-----------|-----------|-----------------|
| `should_pass_through_when_no_exception` | None | Returns next() result |
| `should_pass_through_when_not_accepting_json` | Any | Lets exception propagate |

### Exception Mapping Tests

| Test Case | Exception Type | Expected Status | Expected Body |
|-----------|---------------|-----------------|---------------|
| `should_return_409_for_ConflictException` | ConflictException | 409 | ProblemDetails with errors |
| `should_return_422_for_ValidationException` | ValidationException | 422 | ProblemDetails with errors |
| `should_return_404_for_EntityNotFoundException` | EntityNotFoundException | 404 | ProblemDetails |
| `should_return_409_for_DbUpdateConcurrencyException` | DbUpdateConcurrencyException* | 409 | ConcurrencyFailure error |
| `should_return_408_for_TimeoutException` | TimeoutException | 408 | "Request Timeout" |
| `should_return_501_for_NotImplementedException` | NotImplementedException | 501 | "Not Implemented" |
| `should_return_499_for_OperationCanceledException` | OperationCanceledException | 499 | No body |
| `should_return_499_for_inner_OperationCanceledException` | Exception with inner OCE | 499 | No body |

*Note: DbUpdateConcurrencyException detected by type name (duck typing)

### Edge Case Tests

| Test Case | Scenario | Description |
|-----------|----------|-------------|
| `should_check_accept_header_for_json` | Accept: text/html | Should not handle |
| `should_check_accept_header_for_problem_json` | Accept: application/problem+json | Should handle |
| `should_log_warning_for_db_concurrency` | DbUpdateConcurrencyException | Verify logger called |
| `should_log_debug_for_timeout` | TimeoutException | Verify logger called |

### Mocking Requirements

```csharp
// Create mock EndpointFilterInvocationContext
var httpContext = new DefaultHttpContext();
httpContext.Request.Headers.Accept = "application/json";

var context = Substitute.For<EndpointFilterInvocationContext>();
context.HttpContext.Returns(httpContext);

var next = Substitute.For<EndpointFilterDelegate>();
```

---

## 2. MinimalApiValidatorFilter Tests

**File:** `tests/Headless.Api.MinimalApi.Tests.Unit/Filters/MinimalApiValidatorFilterTests.cs`

### Happy Path Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_call_next_when_no_validators_registered` | No IValidator<T> | next() called |
| `should_call_next_when_validation_passes` | Valid request | next() called |
| `should_return_validation_problem_when_fails` | Invalid request | ValidationProblem result |

### Validation Behavior Tests

| Test Case | Scenario | Description |
|-----------|----------|-------------|
| `should_use_fast_path_for_single_validator` | 1 validator | No Task.WhenAll overhead |
| `should_run_multiple_validators_in_parallel` | 3 validators | Task.WhenAll used |
| `should_aggregate_errors_from_multiple_validators` | 2 validators with errors | All errors combined |
| `should_group_errors_by_property_name` | Multiple errors same property | Grouped in response |
| `should_return_problem_when_request_type_mismatch` | Wrong TRequest | "Invalid request type" |

### Edge Case Tests

| Test Case | Scenario | Description |
|-----------|----------|-------------|
| `should_handle_null_request_in_arguments` | No matching argument | Problem result |
| `should_pass_cancellation_token_to_validators` | Request aborted | Token propagated |
| `should_filter_null_validation_failures` | Validator returns null error | Null filtered out |
| `should_use_ordinal_string_comparison_for_grouping` | Case-sensitive properties | Exact match grouping |

### Performance Tests (Optional)

| Test Case | Description |
|-----------|-------------|
| `should_not_allocate_list_when_validators_is_list` | Avoid ToList() when already list |
| `should_exit_early_when_all_valid` | Skip dictionary allocation |

---

## 3. ApiResultExtensions Tests

**File:** `tests/Headless.Api.MinimalApi.Tests.Unit/Extensions/ApiResultExtensionsTests.cs`

### ApiResult<T> Tests

| Test Case | Result State | Expected |
|-----------|--------------|----------|
| `should_return_ok_with_value_when_success` | Success | Ok(value) |
| `should_return_problem_when_error` | Error | Problem details |

### ApiResult (void) Tests

| Test Case | Result State | Expected |
|-----------|--------------|----------|
| `should_return_no_content_when_success` | Success | NoContent |
| `should_return_problem_when_error` | Error | Problem details |

### ResultError Mapping Tests

| Test Case | Error Type | Expected Status | Expected Details |
|-----------|------------|-----------------|------------------|
| `should_map_NotFoundError_to_404` | NotFoundError | 404 | EntityNotFound |
| `should_map_ValidationError_to_422` | ValidationError | 422 | UnprocessableEntity |
| `should_map_ForbiddenError_to_403` | ForbiddenError | 403 | Forbidden with reason |
| `should_map_UnauthorizedError_to_401` | UnauthorizedError | 401 | Unauthorized |
| `should_map_AggregateError_to_409` | AggregateError | 409 | Conflict with all errors |
| `should_map_ConflictError_to_409` | ConflictError | 409 | Conflict with single error |
| `should_map_unknown_error_to_409` | CustomError | 409 | Default conflict |

### Edge Case Tests

| Test Case | Scenario | Description |
|-----------|----------|-------------|
| `should_include_entity_and_key_in_not_found` | NotFoundError("User", "123") | Entity/key in details |
| `should_convert_validation_errors_to_dictionary` | ValidationError | Proper format |
| `should_map_all_aggregate_errors` | AggregateError with 3 errors | All errors present |

---

## 4. RouteBuilderExtensions Tests

**File:** `tests/Headless.Api.MinimalApi.Tests.Unit/Filters/RouteBuilderExtensionsTests.cs`

| Test Case | Method | Description |
|-----------|--------|-------------|
| `should_add_exception_filter` | AddExceptionFilter | Filter registered |
| `should_add_validator_filter` | AddValidatorFilter<T> | Filter registered |
| `should_chain_multiple_filters` | Both filters | Both registered |

---

## Test Infrastructure

### Required Test Project Setup

```xml
<!-- tests/Headless.Api.MinimalApi.Tests.Unit/Headless.Api.MinimalApi.Tests.Unit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Framework.Api.MinimalApi\Framework.Api.MinimalApi.csproj" />
    <ProjectReference Include="..\Framework.Testing\Framework.Testing.csproj" />
  </ItemGroup>
</Project>
```

### Test Helpers

```csharp
public static class MinimalApiTestHelpers
{
    public static EndpointFilterInvocationContext CreateContext(
        HttpContext? httpContext = null,
        params object[] arguments)
    {
        httpContext ??= new DefaultHttpContext();
        httpContext.Request.Headers.Accept = "application/json";

        var context = Substitute.For<EndpointFilterInvocationContext>();
        context.HttpContext.Returns(httpContext);
        context.Arguments.Returns(arguments);

        return context;
    }

    public static EndpointFilterDelegate CreateNext(object? result = null)
    {
        return _ => ValueTask.FromResult(result);
    }

    public static EndpointFilterDelegate CreateThrowingNext<TException>(TException ex)
        where TException : Exception
    {
        return _ => throw ex;
    }
}
```

### Mock DbUpdateConcurrencyException

```csharp
// Since we can't reference EF Core, create a test exception with the same name
public class DbUpdateConcurrencyException : Exception
{
    public DbUpdateConcurrencyException() : base("Concurrency conflict") { }
}
```

---

## Test Summary

| Component | Test Count | Priority |
|-----------|------------|----------|
| MinimalApiExceptionFilter | 16 | High |
| MinimalApiValidatorFilter | 14 | High |
| ApiResultExtensions | 15 | High |
| RouteBuilderExtensions | 3 | Low |
| **Total** | **48** | - |

---

## Priority Order

1. **MinimalApiExceptionFilter** - Critical path for error handling
2. **ApiResultExtensions** - Core result mapping
3. **MinimalApiValidatorFilter** - Validation pipeline
4. **RouteBuilderExtensions** - Simple DI registration

---

## Integration Test Considerations

For full integration testing, use `WebApplicationFactory`:

```csharp
public class MinimalApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task endpoint_should_return_422_for_invalid_request()
    {
        // Test full pipeline with real filters
    }
}
```

Place in: `tests/Headless.Api.MinimalApi.Tests.Integration/`
