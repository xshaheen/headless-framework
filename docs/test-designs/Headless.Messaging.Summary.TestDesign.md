# Test Case Design Summary: Headless.Messaging.*

**Generated:** 2026-01-25
**Total Packages:** 16
**Branch:** `xshaheen/messaging-consume`

## Coverage Overview

| Package | Unit Tests | Integration Tests | Priority | Status |
|---------|------------|-------------------|----------|--------|
| **Abstractions** | ~25 cases | 0 | P2 | ❌ Needs creation |
| **Core** | ~40 cases (gap) | ~10 cases | P1 | ✅ Partial |
| **Dashboard** | ~50 cases | ~10 cases | P1 Security | ❌ Needs creation |
| **Dashboard.K8s** | ~10 cases | ~3 cases | P3 | ❌ Needs creation |
| **InMemoryQueue** | ~15 cases | 0 | P2 | ❌ Needs creation |
| **InMemoryStorage** | ~20 cases | 0 | P2 | ❌ Needs creation |
| **PostgreSql** | ~15 cases (gap) | ~5 cases (gap) | P1 | ✅ Partial |
| **SqlServer** | ~20 cases (gap) | ~5 cases (gap) | P1 | ✅ Partial |
| **RabbitMq** | ~20 cases (gap) | ~5 cases | P1 | ✅ Partial |
| **Kafka** | ~20 cases | ~5 cases | P1 | ❌ Needs creation |
| **Nats** | ~20 cases | ~5 cases | **P1 CRITICAL** | ❌ Needs creation |
| **AwsSqs** | ~15 cases (gap) | existing | **P1 CRITICAL** | ✅ Partial |
| **AzureServiceBus** | ~15 cases (gap) | ~5 cases | P2 | ✅ Partial |
| **Pulsar** | ~15 cases | ~5 cases | P3 | ❌ Needs creation |
| **RedisStreams** | ~25 cases | ~5 cases | **P1 CRITICAL** | ❌ Needs creation |
| **OpenTelemetry** | ~20 cases | ~5 cases | P2 | ❌ Needs creation |

## Critical Bug Coverage Requirements

These bugs MUST have test coverage before merge:

| Bug | Package | Test Required |
|-----|---------|---------------|
| async void handler | Nats | `handler_exception_should_not_crash_process` |
| Double semaphore release | AwsSqs | `should_not_double_release_semaphore_on_exception` |
| Sync-over-async constructor | RedisStreams | `constructor_should_not_block` |
| Thread-unsafe Random | Core | `should_be_thread_safe_under_concurrent_load` |
| SSRF in PingServices | Dashboard | `should_reject_internal_ip_ranges` |
| Unbounded page size | Dashboard | `should_limit_page_size_to_maximum` |
| Missing schema validation | SqlServer | `should_reject_sql_injection_patterns` |

## Estimated Test Counts

| Category | Count |
|----------|-------|
| **New Unit Tests Required** | ~300 cases |
| **New Integration Tests Required** | ~60 cases |
| **Gap in Existing Tests** | ~100 cases |
| **Total New Test Cases** | ~460 cases |

## Test Project Creation Order (Priority)

### Phase 1: Critical Bugs (Week 1)
1. `Headless.Messaging.Nats.Tests.Unit` - async void bug
2. `Headless.Messaging.RedisStreams.Tests.Unit` - sync-over-async bug
3. `Headless.Messaging.Dashboard.Tests.Unit` - SSRF + auth bugs
4. Fill gaps in `AwsSqs.Tests.Unit` - double semaphore

### Phase 2: Core Functionality (Week 2)
5. `Headless.Messaging.Abstractions.Tests.Unit`
6. Fill gaps in `Core.Tests.Unit`
7. Fill gaps in `PostgreSql.Tests.Unit`
8. Fill gaps in `SqlServer.Tests.Unit`

### Phase 3: Transport Coverage (Week 3)
9. `Headless.Messaging.Kafka.Tests.Unit`
10. `Headless.Messaging.Kafka.Tests.Integration`
11. Fill gaps in `RabbitMq.Tests.Unit`
12. `Headless.Messaging.RabbitMq.Tests.Integration`

### Phase 4: Test Infrastructure (Week 4)
13. `Headless.Messaging.InMemoryQueue.Tests.Unit`
14. `Headless.Messaging.InMemoryStorage.Tests.Unit`
15. `Headless.Messaging.OpenTelemetry.Tests.Unit`

### Phase 5: Lower Priority (Optional)
16. `Headless.Messaging.Pulsar.Tests.Unit`
17. `Headless.Messaging.AzureServiceBus.Tests.Integration`
18. `Headless.Messaging.Dashboard.K8s.Tests.Unit`

## Test Infrastructure Requirements

### Testcontainers Required
```xml
<PackageReference Include="Testcontainers.PostgreSql" />
<PackageReference Include="Testcontainers.MsSql" />
<PackageReference Include="Testcontainers.RabbitMq" />
<PackageReference Include="Testcontainers.Kafka" />
<PackageReference Include="Testcontainers.Redis" />
<PackageReference Include="Testcontainers.LocalStack" /> <!-- SQS -->
<PackageReference Include="Testcontainers.Pulsar" />
<!-- NATS: Use DotNet.Testcontainers with custom image -->
```

### Testing Packages
```xml
<PackageReference Include="xunit" />
<PackageReference Include="AwesomeAssertions" />
<PackageReference Include="NSubstitute" />
<PackageReference Include="Bogus" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" /> <!-- Dashboard -->
```

## Coverage Targets

| Metric | Target | Minimum |
|--------|--------|---------|
| Line Coverage | ≥85% | 80% |
| Branch Coverage | ≥80% | 70% |
| Mutation Score | ≥70% | N/A |

## Individual Package Test Designs

See detailed test designs in:
- [Abstractions](./Headless.Messaging.Abstractions.TestDesign.md)
- [Core](./Headless.Messaging.Core.TestDesign.md)
- [Dashboard](./Headless.Messaging.Dashboard.TestDesign.md)
- [Dashboard.K8s](./Headless.Messaging.Dashboard.K8s.TestDesign.md)
- [InMemoryQueue](./Headless.Messaging.InMemoryQueue.TestDesign.md)
- [InMemoryStorage](./Headless.Messaging.InMemoryStorage.TestDesign.md)
- [PostgreSql](./Headless.Messaging.PostgreSql.TestDesign.md)
- [SqlServer](./Headless.Messaging.SqlServer.TestDesign.md)
- [RabbitMq](./Headless.Messaging.RabbitMq.TestDesign.md)
- [Kafka](./Headless.Messaging.Kafka.TestDesign.md)
- [Nats](./Headless.Messaging.Nats.TestDesign.md)
- [AwsSqs](./Headless.Messaging.AwsSqs.TestDesign.md)
- [AzureServiceBus](./Headless.Messaging.AzureServiceBus.TestDesign.md)
- [Pulsar](./Headless.Messaging.Pulsar.TestDesign.md)
- [RedisStreams](./Headless.Messaging.RedisStreams.TestDesign.md)
- [OpenTelemetry](./Headless.Messaging.OpenTelemetry.TestDesign.md)
