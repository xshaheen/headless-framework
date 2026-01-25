# Test Case Design: Framework.Testing

**Package:** `src/Framework.Testing`
**Test Projects:** None (this IS the test infrastructure)
**Generated:** 2026-01-25

## Package Analysis

| File | Purpose | Testable |
|------|---------|----------|
| `Tests/TestBase.cs` | Base class for all unit tests | N/A (infrastructure) |
| `Helpers/TestHelpers.cs` | Test utility methods | Low |
| `Helpers/TestClock.cs` | Fake IClock for testing | Medium |
| `Helpers/TestCurrentUser.cs` | Fake ICurrentUser for testing | Medium |
| `Helpers/TestCurrentTenant.cs` | Fake ICurrentTenant for testing | Medium |
| `Fakers/FakerExtensions.cs` | Bogus Faker extensions | Low |
| `Assertions/AsyncFunctionAssertionsExtensions.cs` | Async assertion helpers | Low |
| `Order/AlfaTestsOrderer.cs` | Test ordering | Low |
| `Retry/RetryFactAttribute.cs` | Retry on flaky tests | Medium |
| `Retry/RetryTheoryAttribute.cs` | Retry theory tests | Medium |
| `Retry/RetryTestCase.cs` | Retry test case implementation | Medium |
| `Retry/RetryTestCaseRunner.cs` | Retry runner | Medium |
| `Retry/RetryFactDiscoverer.cs` | Discovery for RetryFact | Low |
| `Retry/RetryTheoryDiscoverer.cs` | Discovery for RetryTheory | Low |
| `Retry/RetryDelayEnumeratedTestCase.cs` | Delay between retries | Medium |
| `Retry/DelayedMessageBus.cs` | Message bus for delays | Low |
| `Retry/RetryTestCaseRunnerContext.cs` | Runner context | Low |

## Current Test Coverage

**Testing infrastructure packages typically don't have tests** - they ARE the testing infrastructure.

---

## Recommended: TestClock Tests (if not tested)

**File:** `tests/Framework.Testing.Tests.Unit/Helpers/TestClockTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_return_configured_utc_now` | Fixed time |
| `should_advance_time_when_advanced` | Time manipulation |
| `should_support_setting_new_time` | SetUtcNow |

---

## Recommended: TestCurrentUser Tests

**File:** `tests/Framework.Testing.Tests.Unit/Helpers/TestCurrentUserTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_return_configured_user_id` | UserId property |
| `should_return_configured_roles` | Roles property |
| `should_return_configured_claims` | Claims property |
| `should_return_is_authenticated` | Authentication state |

---

## Recommended: TestCurrentTenant Tests

**File:** `tests/Framework.Testing.Tests.Unit/Helpers/TestCurrentTenantTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_return_configured_tenant_id` | TenantId property |
| `should_support_changing_tenant` | Tenant switching |

---

## Recommended: RetryFact Tests

**File:** `tests/Framework.Testing.Tests.Unit/Retry/RetryFactTests.cs`

| Test Case | Description |
|-----------|-------------|
| `should_retry_failed_test` | Basic retry |
| `should_stop_after_max_retries` | Max attempts |
| `should_succeed_after_retry` | Intermittent failure |
| `should_delay_between_retries` | Delay configuration |

---

## Test Summary

| Component | Existing | Recommended | Total |
|-----------|----------|-------------|-------|
| TestClock | 0 | 3 | 3 |
| TestCurrentUser | 0 | 4 | 4 |
| TestCurrentTenant | 0 | 2 | 2 |
| RetryFact | 0 | 4 | 4 |
| **Total** | **0** | **13** | **13** |

---

## Priority

**Very Low Priority** - Testing infrastructure is typically verified by its usage in other test projects.

---

## Notes

1. **TestBase provides**:
   - `Logger` - ILogger instance
   - `LoggerFactory` - ILoggerFactory instance
   - `LoggerProvider` - ILoggerProvider for xUnit output
   - `Faker` - Bogus Faker instance
   - `AbortToken` - CancellationToken from TestContext
   - `AbortCurrentTests()` - Cancel current test
2. **xUnit 3 integration**:
   - `[CaptureConsole]` assembly attribute
   - `TestContext.Current` for test context
   - `IAsyncLifetime` for async setup/teardown
3. **RetryFact/RetryTheory**:
   - Attributes for flaky integration tests
   - Configurable max retries and delay
   - Useful for tests with external dependencies

---

## TestBase Architecture

```
TestBase : IAsyncLifetime
├── Constructor (sync setup):
│   └── Creates LoggerProvider, LoggerFactory, Logger
├── InitializeAsync() (async setup):
│   └── Override for async initialization
├── DisposeAsync() (teardown):
│   └── Calls DisposeAsyncCore()
└── DisposeAsyncCore() (cleanup):
    └── Disposes LoggerFactory, LoggerProvider

Protected Members:
├── LoggerProvider (ILoggerProvider)
├── LoggerFactory (ILoggerFactory)
├── Logger (ILogger)
├── Faker (Bogus.Faker)
├── AbortToken (static CancellationToken)
└── AbortCurrentTests() (static void)
```

---

## Recommendation

**Very Low Priority** - This is test infrastructure. It's verified by:
1. Being used in all other test projects
2. Running successfully in CI/CD pipelines
3. Manual verification during test development

Creating tests for test infrastructure is unusual but could be valuable for:
- RetryFact/RetryTheory behavior verification
- TestClock time manipulation
- Test helper edge cases
