# Test Case Design: Headless.Api.Mvc

**Package:** `src/Headless.Api.Mvc`
**Test Projects:** None existing - **needs creation**
**Generated:** 2026-01-25

## Package Analysis

| File | Purpose | Testable |
|------|---------|----------|
| `MvcApiExceptionFilter.cs` | Exception handling for MVC controllers | High |
| `BlockInEnvironmentAttribute.cs` | Block endpoints in specific environments | High |
| `RequireEnvironmentAttribute.cs` | Require specific environment for endpoints | High |
| `NoTrailingSlashAttribute.cs` | Reject URLs with trailing slash | High |
| `NoLowercaseQueryStringAttribute.cs` | Marker attribute (metadata only) | Low |
| `RedirectToCanonicalUrlRule.cs` | URL canonicalization rewrite rule | High |
| `ApiControllerBase.cs` | Base controller with helper methods | Medium |
| `ApiResultMvcExtensions.cs` | Result-to-ActionResult mapping | High |
| `ControllerBaseExtensions.cs` | Controller helper extensions | Medium |
| `ActionDescriptorExtensions.cs` | ActionDescriptor type helpers | Medium |
| `ConfigureMvcApiOptions.cs` | MVC options configuration | Low |
| `ConfigureMvcJsonOptions.cs` | JSON options configuration | Low |
| `AddMvcExtensions.cs` | DI registration | Low |

---

## 1. MvcApiExceptionFilter Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Filters/MvcApiExceptionFilterTests.cs`

### Happy Path Tests

| Test Case | Exception | Expected Result |
|-----------|-----------|-----------------|
| `should_skip_when_exception_already_handled` | Any | No action taken |
| `should_skip_when_not_accepting_json` | Any | Exception propagates |

### Exception Mapping Tests

| Test Case | Exception Type | Expected Status | Expected Body |
|-----------|---------------|-----------------|---------------|
| `should_return_409_for_ConflictException` | ConflictException | 409 | ProblemDetails with errors |
| `should_return_422_for_ValidationException` | ValidationException | 422 | ProblemDetails with validation errors |
| `should_return_404_for_EntityNotFoundException` | EntityNotFoundException | 404 | ProblemDetails with entity/key |
| `should_return_409_for_DbUpdateConcurrencyException` | DbUpdateConcurrencyException | 409 | ConcurrencyFailure error |
| `should_return_408_for_TimeoutException` | TimeoutException | 408 | "Request Timeout" |
| `should_return_501_for_NotImplementedException` | NotImplementedException | 501 | "Not Implemented" |
| `should_return_499_for_OperationCanceledException` | OperationCanceledException | 499 | No body |
| `should_return_499_for_inner_OperationCanceledException` | Exception with inner OCE | 499 | No body |

### Logging Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_log_critical_for_DbUpdateConcurrencyException` | DbUpdateConcurrencyException | Critical log with EventId 5003 |
| `should_log_debug_for_TimeoutException` | TimeoutException | Debug log with EventId 5004 |

### Edge Case Tests

| Test Case | Scenario | Description |
|-----------|----------|-------------|
| `should_set_ExceptionHandled_to_true` | Handled exception | ExceptionHandled flag set |
| `should_accept_application_json` | Accept: application/json | Exception handled |
| `should_accept_problem_json` | Accept: application/problem+json | Exception handled |
| `should_not_modify_when_unknown_exception` | CustomException | Returns null, exception propagates |

---

## 2. BlockInEnvironmentAttribute Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Filters/BlockInEnvironmentAttributeTests.cs`

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_block_in_matching_environment` | env=Production, attr=Production | 404 ProblemDetails |
| `should_allow_in_non_matching_environment` | env=Development, attr=Production | next() called |
| `should_be_case_sensitive_for_environment_name` | env=production, attr=Production | next() called (case mismatch) |
| `should_expose_Environment_property` | Any | Property matches constructor arg |

---

## 3. RequireEnvironmentAttribute Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Filters/RequireEnvironmentAttributeTests.cs`

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_allow_in_matching_environment` | env=Development, attr=Development | next() called |
| `should_block_in_non_matching_environment` | env=Production, attr=Development | 404 ProblemDetails |
| `should_be_case_sensitive_for_environment_name` | env=development, attr=Development | 404 (case mismatch) |
| `should_expose_Environment_property` | Any | Property matches constructor arg |

---

## 4. NoTrailingSlashAttribute Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Filters/NoTrailingSlashAttributeTests.cs`

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_allow_path_without_trailing_slash` | /api/users | next() called |
| `should_block_path_with_trailing_slash` | /api/users/ | 404 ProblemDetails |
| `should_allow_root_path` | / | next() called |
| `should_allow_empty_path` | (no path) | next() called |
| `should_throw_when_context_null` | null context | ArgumentNullException |

---

## 5. RedirectToCanonicalUrlRule Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Middlewares/RedirectToCanonicalUrlRuleTests.cs`

### Constructor Tests

| Test Case | Description |
|-----------|-------------|
| `should_throw_when_options_null` | ArgumentNullException for null IOptions |
| `should_use_options_values` | AppendTrailingSlash/LowercaseUrls from RouteOptions |
| `should_accept_explicit_values` | Direct bool parameters constructor |

### Trailing Slash Tests (AppendTrailingSlash=true)

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_append_trailing_slash` | /api/users | 301 to /api/users/ |
| `should_not_modify_when_already_has_slash` | /api/users/ | No redirect |
| `should_not_modify_home_page` | / | No redirect |
| `should_respect_NoTrailingSlashAttribute` | /robots.txt (with attr) | No redirect |

### Trailing Slash Tests (AppendTrailingSlash=false)

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_strip_trailing_slash` | /api/users/ | 301 to /api/users |
| `should_not_modify_when_no_slash` | /api/users | No redirect |

### Lowercase Tests (LowercaseUrls=true)

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_lowercase_path` | /API/Users | 301 to /api/users |
| `should_lowercase_query_string` | ?Name=John | 301 to ?name=john |
| `should_not_modify_already_lowercase` | /api/users | No redirect |
| `should_respect_NoLowercaseQueryStringAttribute` | ?Token=ABC (with attr) | Query unchanged |

### Combined Tests

| Test Case | Input | Expected |
|-----------|-------|----------|
| `should_apply_both_trailing_slash_and_lowercase` | /API/Users | 301 to /api/users/ |
| `should_only_redirect_GET_requests` | POST /API/Users | No redirect |

### Edge Cases

| Test Case | Description |
|-----------|-------------|
| `should_use_301_permanent_redirect` | StatusCode = 301 |
| `should_set_EndResponse_result` | RuleResult.EndResponse |
| `should_set_Location_header` | Canonical URL in Location |

---

## 6. ApiControllerBase Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Controllers/ApiControllerBaseTests.cs`

### Service Resolution Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_resolve_Configuration_from_services` | Service registered | IConfiguration returned |
| `should_throw_when_Configuration_not_registered` | Not registered | InvalidOperationException |
| `should_resolve_Sender_from_services` | Service registered | ISender returned |
| `should_throw_when_Sender_not_registered` | Not registered | InvalidOperationException |
| `should_resolve_LocaleAccessor_from_services` | Service registered | IEnumLocaleAccessor returned |

### Helper Method Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `LocaleValues_should_return_enum_locale_array` | Valid enum type | OkObjectResult with array |
| `NoContent_should_send_request_and_return_204` | Valid request | NoContentResult |
| `NoContent_should_return_MalformedSyntax_when_null` | null request | BadRequestObjectResult |
| `Ok_with_IRequest_T_should_return_value` | Valid request | OkObjectResult with value |
| `Ok_with_IRequest_T_should_return_MalformedSyntax_when_null` | null request | BadRequestObjectResult |
| `Ok_with_IRequest_should_send_and_return_ok` | Valid request | OkResult |
| `Ok_with_Unit_should_return_ok` | Unit value | OkResult |

### ProblemDetails Helper Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `MalformedSyntax_should_return_BadRequest_with_ProblemDetails` | Any | BadRequestObjectResult |
| `UnprocessableEntityProblemDetails_should_return_422` | ValidationFailures | UnprocessableEntityObjectResult |
| `NotFoundProblemDetails_should_return_404` | entity, key | NotFoundObjectResult |
| `ConflictProblemDetails_should_return_409_with_collection` | ErrorDescriptor[] | ConflictObjectResult |
| `ConflictProblemDetails_should_return_409_with_single` | ErrorDescriptor | ConflictObjectResult |

---

## 7. ApiResultMvcExtensions Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Extensions/ApiResultMvcExtensionsTests.cs`

### ApiResult<T> Tests

| Test Case | Result State | Expected |
|-----------|--------------|----------|
| `should_return_ok_with_value_when_success` | Success | OkObjectResult(value) |
| `should_return_problem_when_error` | Error | Error-specific result |

### ApiResult (void) Tests

| Test Case | Result State | Expected |
|-----------|--------------|----------|
| `should_return_NoContent_when_success` | Success | NoContentResult |
| `should_return_problem_when_error` | Error | Error-specific result |

### ResultError Mapping Tests

| Test Case | Error Type | Expected Status | Expected Body |
|-----------|------------|-----------------|---------------|
| `should_map_NotFoundError_to_404` | NotFoundError | 404 | EntityNotFound ProblemDetails |
| `should_map_ValidationError_to_422` | ValidationError | 422 | UnprocessableEntity ProblemDetails |
| `should_map_ForbiddenError_to_403` | ForbiddenError | 403 | Forbidden ProblemDetails |
| `should_map_UnauthorizedError_to_401` | UnauthorizedError | 401 | Unauthorized ProblemDetails |
| `should_map_AggregateError_to_409` | AggregateError | 409 | Conflict with all errors |
| `should_map_ConflictError_to_409` | ConflictError | 409 | Conflict with single error |
| `should_map_unknown_error_to_409` | CustomError | 409 | Default conflict |

---

## 8. ControllerBaseExtensions Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Extensions/ControllerBaseExtensionsTests.cs`

### ChallengeOrForbid Tests

| Test Case | User State | Expected |
|-----------|------------|----------|
| `should_return_Forbid_when_authenticated` | IsAuthenticated=true | ForbidResult |
| `should_return_Challenge_when_not_authenticated` | IsAuthenticated=false | ChallengeResult |
| `should_return_Challenge_when_identity_null` | Identity=null | ChallengeResult |
| `should_pass_schemes_to_Forbid` | Authenticated + schemes | ForbidResult with schemes |
| `should_pass_schemes_to_Challenge` | Not authenticated + schemes | ChallengeResult with schemes |

### Redirect Tests

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `LocalRedirect_should_not_escape_when_false` | escapeUrl=false | Original URL |
| `LocalRedirect_should_escape_when_true` | escapeUrl=true | Escaped URL |
| `Redirect_should_not_escape_when_false` | escapeUrl=false | Original URL |
| `Redirect_should_escape_when_true` | escapeUrl=true | Escaped URL |

---

## 9. ActionDescriptorExtensions Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Extensions/ActionDescriptorExtensionsTests.cs`

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `IsControllerAction_should_return_true_for_ControllerActionDescriptor` | ControllerActionDescriptor | true |
| `IsControllerAction_should_return_false_for_PageActionDescriptor` | PageActionDescriptor | false |
| `IsPageAction_should_return_true_for_PageActionDescriptor` | PageActionDescriptor | true |
| `IsPageAction_should_return_false_for_ControllerActionDescriptor` | ControllerActionDescriptor | false |
| `AsControllerActionDescriptor_should_cast_successfully` | ControllerActionDescriptor | Casted instance |
| `AsControllerActionDescriptor_should_throw_for_non_controller` | PageActionDescriptor | InvalidOperationException |
| `AsPageAction_should_cast_successfully` | PageActionDescriptor | Casted instance |
| `AsPageAction_should_throw_for_non_page` | ControllerActionDescriptor | InvalidOperationException |
| `GetMethodInfo_should_return_method_info` | ControllerActionDescriptor | MethodInfo |
| `GetReturnType_should_return_return_type` | ControllerActionDescriptor | Type |

---

## 10. ConfigureMvcApiOptions Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Options/ConfigureMvcApiOptionsTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_disable_NoContentOutputFormatter` | TreatNullValueAsNoContent=false |
| `should_enable_ReturnHttpNotAcceptable` | ReturnHttpNotAcceptable=true |
| `should_clear_ModelValidatorProviders` | Providers cleared |
| `should_add_SystemTextJsonValidationMetadataProvider` | Provider added |
| `should_add_MvcApiExceptionFilter` | Filter registered |
| `should_suppress_ModelStateInvalidFilter` | SuppressModelStateInvalidFilter=true |

---

## 11. ConfigureMvcJsonOptions Tests

**File:** `tests/Headless.Api.Mvc.Tests.Unit/Options/ConfigureMvcJsonOptionsTests.cs`

| Test Case | Scenario | Expected |
|-----------|----------|----------|
| `should_configure_web_json_options` | Any | JsonConstants.ConfigureWebJsonOptions called |
| `should_enable_WriteIndented_in_Development` | env=Development | WriteIndented=true |
| `should_enable_WriteIndented_in_Test` | env=Test | WriteIndented=true |
| `should_disable_WriteIndented_in_Production` | env=Production | WriteIndented=false |

---

## Test Infrastructure

### Required Test Project Setup

```xml
<!-- tests/Headless.Api.Mvc.Tests.Unit/Headless.Api.Mvc.Tests.Unit.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Framework.Api.Mvc\Framework.Api.Mvc.csproj" />
    <ProjectReference Include="..\Framework.Testing\Framework.Testing.csproj" />
  </ItemGroup>
</Project>
```

### Test Helpers

```csharp
public static class MvcTestHelpers
{
    public static ExceptionContext CreateExceptionContext(
        Exception exception,
        HttpContext? httpContext = null)
    {
        httpContext ??= new DefaultHttpContext();
        httpContext.Request.Headers.Accept = "application/json";

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ExceptionContext(actionContext, [])
        {
            Exception = exception
        };
    }

    public static ResourceExecutingContext CreateResourceExecutingContext(
        HttpContext? httpContext = null)
    {
        httpContext ??= new DefaultHttpContext();

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new ResourceExecutingContext(actionContext, [], []);
    }

    public static RewriteContext CreateRewriteContext(
        HttpContext? httpContext = null)
    {
        httpContext ??= new DefaultHttpContext();

        return new RewriteContext
        {
            HttpContext = httpContext
        };
    }
}
```

### Mock Services

```csharp
public static class MockServiceProvider
{
    public static IServiceProvider CreateWithEnvironment(string environmentName)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);

        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(IWebHostEnvironment)).Returns(env);
        services.GetService(typeof(IProblemDetailsCreator))
            .Returns(new TestProblemDetailsCreator());

        return services;
    }
}
```

---

## Test Summary

| Component | Test Count | Priority |
|-----------|------------|----------|
| MvcApiExceptionFilter | 16 | High |
| BlockInEnvironmentAttribute | 4 | High |
| RequireEnvironmentAttribute | 4 | High |
| NoTrailingSlashAttribute | 5 | High |
| RedirectToCanonicalUrlRule | 18 | High |
| ApiControllerBase | 15 | Medium |
| ApiResultMvcExtensions | 11 | High |
| ControllerBaseExtensions | 9 | Medium |
| ActionDescriptorExtensions | 10 | Medium |
| ConfigureMvcApiOptions | 6 | Low |
| ConfigureMvcJsonOptions | 4 | Low |
| **Total** | **102** | - |

---

## Priority Order

1. **MvcApiExceptionFilter** - Critical path for error handling
2. **ApiResultMvcExtensions** - Core result mapping
3. **RedirectToCanonicalUrlRule** - SEO-critical URL canonicalization
4. **BlockInEnvironmentAttribute / RequireEnvironmentAttribute** - Environment security
5. **NoTrailingSlashAttribute** - URL validation
6. **ApiControllerBase** - Base controller helpers
7. **ControllerBaseExtensions** - Controller utilities
8. **ActionDescriptorExtensions** - Type helpers
9. **Configuration classes** - Simple DI setup

---

## Notes

1. MvcApiExceptionFilter is nearly identical to MinimalApiExceptionFilter - consider shared test helpers
2. Environment attributes are inverse of each other - test both together
3. RedirectToCanonicalUrlRule has complex logic with multiple attribute checks
4. ApiControllerBase tests require mock HttpContext with RequestServices
5. The `ToUriComponents()` extension is used in redirect helpers - verify it exists in Framework.Base
