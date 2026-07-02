# Headless .NET Framework — Agent Instructions

This project uses the [Headless .NET Framework](https://github.com/xshaheen/headless-framework) — a modular .NET 10 framework that ships every feature as a thin `Headless.*.Abstractions` package plus one or more provider packages (Redis, Azure, AWS, EF Core, etc.). When generating, modifying, or reviewing code in this project, follow the rules below and fetch the relevant per-domain documentation when you need deeper context.

## Rules

### Package selection

- **Abstraction + provider pattern.** Depend on `Headless.*.Abstractions` interfaces. Add exactly one provider package per feature (e.g., `Headless.Caching.Redis`, `Headless.Blobs.Azure`). Never reference a provider type from application code.
- **Caching.** Use `ICache` from `Headless.Caching.Abstractions` for application cache operations. Use `Headless.Caching.Bcl` only when ASP.NET Core Session or another standard integration requires `Microsoft.Extensions.Caching.Distributed.IDistributedCache`. Do not use `IMemoryCache` directly.
- **Coordination.** Use `INodeMembership` from `Headless.Coordination.Abstractions` for node liveness and `node@incarnation` identity. Do not use it as a consensus system or ownership ledger.
- **Blob storage.** Use `IBlobStorage` from `Headless.Blobs.Abstractions`. Do not call cloud SDK clients (`Amazon.S3.IAmazonS3`, `Azure.Storage.Blobs.BlobServiceClient`) from application code.
- **Serialization.** Use `ISerializer` from `Headless.Serializer.Abstractions`. Default to `Headless.Serializer.Json`; use `Headless.Serializer.MessagePack` only when binary performance is required. Do not call `System.Text.Json.JsonSerializer` directly.
- **Distributed messaging.** Use `Headless.Messaging` abstractions. Do not use raw transport clients (`RabbitMQ.Client`, `Confluent.Kafka`, `Azure.Messaging.ServiceBus`) from application code.
- **Background jobs.** Use `Headless.Jobs` — mark job methods with `[JobFunction]` and add `Headless.Jobs.SourceGenerator`. Do not use Hangfire or Quartz.
- **Feature flags.** Use `Headless.Features`. Do not use `Microsoft.FeatureManagement`.
- **Distributed locks.** Use `IDistributedLease` from `Headless.DistributedLocks.Abstractions`, not ad-hoc Redis `SET NX` or database row locks.
- **Rate limiting.** The framework does not ship a rate-limiting package. Use `Microsoft.AspNetCore.RateLimiting` for in-process limits; for distributed scenarios, use `Polly.RateLimiting` composed with a community Redis-backed `RateLimiter` such as `RedisRateLimiting`.
- **Dev packages.** Use `*.Dev` packages (`Headless.Emails.Dev`, `Headless.Sms.Dev`, `Headless.PushNotifications.Dev`) in development so no real messages are sent.

### Argument validation and guard clauses

- Use `Argument.*` (e.g., `Argument.IsNotNull(value)`, `Argument.IsNotNullOrWhiteSpace(value)`, `Argument.IsPositive(value)`) and `Ensure.*` (e.g., `Ensure.True(condition)`, `Ensure.NotDisposed(this, _disposed)`) from `Headless.Checks`.
- Do not use `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfGreaterThan`, or other `*.ThrowIf*` static helpers.
- For request payloads, use FluentValidation rules rather than manual `if` guards.

### Options binding and validation

- Validate options only when there is something meaningful to enforce (required values, range/format constraints, cross-field invariants). Skip validation for plain DTO-style options with no rules.
- Register options with `services.AddOptions<TOptions, TValidator>()` or `services.Configure<TOptions, TValidator>(...)` from `Headless.Hosting`. These overloads wire up FluentValidation and `ValidateOnStart()` automatically.
- Place an `internal sealed class {OptionsName}Validator : AbstractValidator<{OptionsName}>` in the same file as the options class, directly below it.
- Never call `new TValidator().ValidateAndThrow()` manually — let the DI pipeline do it.
- Do not implement `IValidateOptions<T>` when the hosting overloads already cover validation.

### ASP.NET Core bootstrap

- Call `builder.AddHeadlessInfrastructure()` on `WebApplicationBuilder` for one-shot bootstrapping (compression, security headers, problem details, JWT, identity, validation). Do not register these manually.
- Call `app.UseHeadlessDefaults()` for the default middleware order (`UseStatusCodePages()` before `UseExceptionHandler()`); add auth/tenant middleware after, then map endpoints.
- Prefer `Headless.Api.MinimalApi` over `Headless.Api.Mvc` for new endpoints. Use `.Validate<T>()` on Minimal API endpoints for FluentValidation integration.
- Inject `IRequestContext` from `Headless.Api.Abstractions` for request-scoped user, tenant, locale, timezone, and correlation ID — do not access `HttpContext` directly from service-layer code.

### Multi-tenancy

- Configure tenancy at bootstrap with `builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()))`. Place `app.UseHeadlessTenancy()` after `UseAuthentication()` and before `UseAuthorization()`.
- When publishing messages, set `PublishOptions.TenantId`. When consuming, read `ConsumeContext.TenantId`. Do not write or read the `headless-tenant-id` header directly — the publish pipeline rejects raw-only and mismatched writes.

### Database access

- For EF Core, use `Headless.Orm.EntityFramework` — call `services.AddHeadlessDbContext<TContext>(...)` for framework conventions, global filters, soft deletes, and tenancy support.
- For raw SQL, use the connection factories in `Headless.Sql.PostgreSql` / `Headless.Sql.SqlServer` / `Headless.Sql.Sqlite` rather than constructing `NpgsqlConnection` / `SqlConnection` directly.

### Code style

- Follow modern C# style: file-scoped namespaces, primary constructors, `sealed` by default, collection expressions `[]`, pattern matching, target-typed `new()`.
- Prefer async + `CancellationToken` end-to-end; do not block on `.Result` or `.Wait()`.

### Testing

- Use `Headless.Testing` for unit tests.
- Inherit unit-test classes from `TestBase`. It supplies `Logger` (`ILogger`), `Faker` (Bogus), and `AbortToken` (`CancellationToken`) — use them instead of creating your own.
- Always pass `AbortToken` as the cancellation token argument for async calls inside test methods. `AbortToken` is the per-test `CancellationToken` exposed by `TestBase` (xUnit v3's `TestContext.Current.CancellationToken`) — it fires when the test framework cancels the test (timeout, abort, run cancellation), so propagating it makes the system-under-test cancel correctly with the test. Do not use `CancellationToken.None` or `default`.
- The test stack is `xunit.v3` (Microsoft Testing Platform), `AwesomeAssertions` (fork of FluentAssertions), `NSubstitute`, and `Bogus`. Do not introduce Moq, the original FluentAssertions, NUnit, or MSTest.
- For flaky tests (network/timing-dependent), use `[RetryFact(MaxRetries = N)]` / `[RetryTheory(MaxRetries = N)]` rather than retry loops inside the test body.
- For time-dependent logic, inject `TestClock` (a `TimeProvider`) and call `clock.Advance(...)` to simulate elapsed time. Do not call `DateTime.UtcNow` directly from production code under test.
- For tenant and user context in unit tests, use `TestCurrentTenant` and `TestCurrentUser` instead of mocking `IRequestContext`.
- For async test setup/teardown, override `InitializeAsync()` and `DisposeAsyncCore()` on `TestBase` subclasses (call `base.InitializeAsync()` / `base.DisposeAsyncCore()`).

Example:

```csharp
public sealed class OrderServiceTests : TestBase
{
    private readonly OrderService _sut;

    public OrderServiceTests()
    {
        _sut = new OrderService(Logger);
    }

    [Fact]
    public async Task should_create_order()
    {
        var order = Faker.OrderFaker().Generate();
        var result = await _sut.CreateAsync(order, AbortToken);
        result.Should().NotBeNull();
    }
}
```

### NuGet and project setup

- All Headless package versions are managed centrally in `Directory.Packages.props`. Never add a `Version` attribute on a `<PackageReference>` in a `.csproj` file.
- When adding a new Headless package, add it to `Directory.Packages.props` first, then reference it from the consuming project without a version.

## End-to-End Example

A small document service that shows how the pieces fit: validate the request, store the file through `IBlobStorage`, read it back through a read-through `ICache`, and schedule background processing through `Headless.Jobs`. Every feature is wired once at the composition root; application code depends only on the abstractions.

Packages: `Headless.Api.MinimalApi`, `Headless.Blobs.FileSystem`, `Headless.Caching.InMemory`, `Headless.Jobs.Core` (+ `Headless.Jobs.SourceGenerator`), `Headless.FluentValidation`. Swap a provider (e.g. `Headless.Blobs.Aws`, `Headless.Caching.Redis`) without touching the service code.

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// One-shot API bootstrap: problem details, compression, security headers, validation.
builder.AddHeadlessInfrastructure();

// Pick exactly one provider per feature; code only against the abstractions.
builder.Services.AddHeadlessBlobs(blobs =>
    blobs.UseFileSystem(options =>
        options.BaseDirectoryPath = Path.Combine(builder.Environment.ContentRootPath, "storage")
    )
);
builder.Services.AddHeadlessCaching(setup => setup.UseInMemory());
builder.Services.AddHeadlessJobs(options =>
    options.ConfigureScheduler(scheduler => scheduler.SchedulerTimeZone = TimeZoneInfo.Utc)
);

builder.Services.AddScoped<DocumentService>();

var app = builder.Build();

// Containers are never auto-created (UploadAsync treats a missing container as an error):
// provision once at startup via the DI-resolved manager, or out-of-band (IaC/dashboard).
await app.Services.GetRequiredService<IBlobContainerManager>().EnsureContainerAsync("documents");

app.UseHeadlessDefaults(); // StatusCodePages before ExceptionHandler, then auth/tenant, then endpoints

app.MapPost(
        "/documents",
        async (UploadRequest request, DocumentService documents, CancellationToken ct) =>
        {
            var id = await documents.StoreAsync(request, ct);
            return Results.Created($"/documents/{id}", new { id });
        }
    )
    .Validate<UploadRequest>(); // FluentValidation filter — returns 422 ProblemDetails on failure

app.Run();
```

```csharp
// Request contract + validator (the validator lives beside the type it validates).
public sealed record UploadRequest(string FileName, byte[] Content);

internal sealed class UploadRequestValidator : AbstractValidator<UploadRequest>
{
    public UploadRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.Content).NotEmpty();
    }
}
```

```csharp
// Application service — depends only on Headless abstractions.
public sealed record DocumentInfo(string Id, string FileName, long Size);

public interface IDocumentRepository
{
    ValueTask<DocumentInfo?> FindAsync(string id, CancellationToken ct);
}

public sealed class DocumentService(
    IBlobStorage storage,
    ICache cache,
    ITimeJobManager<TimeJobEntity> jobs,
    IDocumentRepository repository
)
{
    public async Task<string> StoreAsync(UploadRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");

        await using var stream = new MemoryStream(request.Content);
        await storage.UploadAsync(
            new BlobLocation("documents", id),
            stream,
            metadata: new Dictionary<string, string> { ["file-name"] = request.FileName },
            cancellationToken: ct
        );

        // Hand processing (virus scan, thumbnailing, ...) to a background job.
        await jobs.AddAsync(
            new TimeJobEntity
            {
                Function = "ProcessDocument",
                Description = $"process-{id}",
                ExecutionTime = DateTime.UtcNow,
                Request = JobsHelper.SerializeRequest(new { DocumentId = id }),
                Retries = 3,
                RetryIntervals = [30, 60, 120],
            },
            ct
        );

        return id;
    }

    // Read-through cache: the factory runs only on a miss.
    public async Task<DocumentInfo?> GetInfoAsync(string id, CancellationToken ct)
    {
        var cached = await cache.GetOrAddAsync(
            $"doc:{id}",
            token => repository.FindAsync(id, token),
            TimeSpan.FromHours(1),
            ct
        );

        return cached.HasValue ? cached.Value : null;
    }
}
```

```csharp
// Background job — registered at compile time by Headless.Jobs.SourceGenerator.
public sealed record ProcessDocumentRequest(string DocumentId);

public static class DocumentJobs
{
    [JobFunction("ProcessDocument")]
    public static Task ProcessAsync(JobFunctionContext<ProcessDocumentRequest> context, CancellationToken ct)
    {
        var id = context.Request.DocumentId;
        // ... process the stored document; context.RetryCount and context.ScheduledFor are available ...
        return Task.CompletedTask;
    }
}
```

For each feature's options, providers, and trade-offs, fetch the matching domain doc: [api.md](api.md), [blobs.md](blobs.md), [caching.md](caching.md), [jobs.md](jobs.md), [utilities.md](utilities.md).

## Domain documentation

Fetch only what's relevant to the task. Each file documents the domain's packages, quick orientation, setup, and domain-specific agent rules.

- [api.md](api.md) — ASP.NET Core API infrastructure (JWT, middleware, Minimal API, MVC, FluentValidation, Data Protection).
- [audit-log.md](audit-log.md) — Property-level audit logging for entity mutations and explicit business events with EF Core persistence.
- [core.md](core.md) — DDD building blocks, guard clauses, cross-cutting abstractions, string encryption/hashing, domain events.
- [extensions.md](extensions.md) — Base utility library: result pattern, domain primitives, value objects, collections, IO, threading, reflection helpers.
- [multi-tenancy.md](multi-tenancy.md) — Tenant context across HTTP, EF Core filters, permission caching, background processing.
- [blobs.md](blobs.md) — Unified blob storage (AWS S3, Azure, file system, Redis, SFTP).
- [caching.md](caching.md) — Memory, Redis, and Hybrid (L1+L2) caching with fail-safe, refresh, tagging, and distributed factory locks.
- [captcha.md](captcha.md) — CAPTCHA verification (Google reCAPTCHA v2/v3, Cloudflare Turnstile) behind one pass/fail abstraction.
- [commit-coordination.md](commit-coordination.md) — Post-commit and rollback callback coordination for outbox, jobs, cache, and events.
- [coordination.md](coordination.md) — Node membership, liveness, lifecycle events, and provider-backed fail-stop fencing.
- [emails.md](emails.md) — Email sending (Azure Communication Services, AWS SES, MailKit SMTP, dev no-op).
- [features.md](features.md) — Feature flags with caching, value providers, EF Core storage.
- [identity.md](identity.md) — ASP.NET Core Identity with EF Core integration.
- [imaging.md](imaging.md) — ImageSharp-based resizing and compression.
- [logging.md](logging.md) — Serilog configuration with structured logging defaults.
- [media.md](media.md) — Text extraction from PDF, Word, PowerPoint for indexing.
- [mediator.md](mediator.md) — In-process request/handler dispatch with pipeline behaviors; what belongs in the pipeline and what stays at the boundary.
- [messaging.md](messaging.md) — Distributed messaging with transactional outbox, 8 transports, 3 storage backends.
- [openapi.md](openapi.md) — NSwag OpenAPI generation and Scalar documentation UI.
- [orm.md](orm.md) — Entity Framework Core and Couchbase with DDD support.
- [payments.md](payments.md) — Paymob Accept (cash-in / cash-out).
- [permissions.md](permissions.md) — Permission management with caching, authorization, EF Core storage.
- [push-notifications.md](push-notifications.md) — Push notifications (Firebase FCM, dev no-op).
- [distributed-locks.md](distributed-locks.md) — Distributed locking (Redis, Postgres, in-memory).
- [serialization.md](serialization.md) — Serialization (System.Text.Json, MessagePack).
- [settings.md](settings.md) — Dynamic runtime settings with hierarchical providers and EF Core storage.
- [sms.md](sms.md) — SMS sending (Twilio, AWS SNS, Infobip, regional providers).
- [sql.md](sql.md) — SQL connection factories (PostgreSQL, SQL Server, SQLite).
- [testing.md](testing.md) — xUnit base classes and Testcontainers fixtures.
- [jobs.md](jobs.md) — Distributed background jobs with cron, monitoring, source generation.
- [tus.md](tus.md) — TUS protocol resumable file uploads with Azure and distributed lock support.
- [utilities.md](utilities.md) — FluentValidation extensions, source-generated primitives, sitemaps, slugs, Redis utilities.

## Packages

Catalog of all Headless packages, grouped by domain. Use this to identify which package to add via `dotnet add package`. For setup details, fetch the relevant domain doc above.

### API & Web
- `Headless.Api.Abstractions` — `IRequestContext`, `IWebClientInfoProvider`, request-scoped contracts.
- `Headless.Api.Core` — ASP.NET Core API primitives: problem details, response compression, antiforgery, JSON/time services, status-code rewriter, tenancy resolution.
- `Headless.Api.ServiceDefaults` — one-line bootstrap (`AddHeadless()`/`UseHeadless()`/`MapHeadlessEndpoints()`) combining the Core primitives with Aspire-style host conventions (OpenTelemetry, OpenAPI, service discovery, HttpClient resilience).
- `Headless.Api.DataProtection` — Persist ASP.NET Data Protection keys to any `IBlobStorage`.
- `Headless.Api.FluentValidation` — Validators for `IFormFile` uploads (size, content type, magic bytes).
- `Headless.Api.Idempotency` — Stripe-style idempotency middleware: cache full HTTP responses on first execution and replay them on identical retries.
- `Headless.Api.Logging.Serilog` — Serilog enrichers for per-request context.
- `Headless.Api.MinimalApi` — Minimal API integration (JSON config, validation filters, exception handling).
- `Headless.Api.Mvc` — MVC/Web API integration (controllers, filters, URL canonicalization).

### Core
- `Headless.Extensions` — Foundational extension methods, primitives, helpers.
- `Headless.Core` — Multi-tenancy, user context, cross-cutting abstractions.
- `Headless.Security.Abstractions` — String encryption and hashing contracts.
- `Headless.Security` — String encryption and hashing services.
- `Headless.Checks` — Guard clauses (`Argument.*`, `Ensure.*`).
- `Headless.Domain` — DDD entities, aggregate roots, value objects, auditing.
- `Headless.Domain.LocalEventBus` — DI-based `ILocalEventBus` for in-process domain events.

### Multi-Tenancy
- `Headless.MultiTenancy` — Tenancy composition root: `AddHeadlessTenancy(...)` builder, `UseHeadlessTenancy()` middleware, posture manifest, and startup validation. Seam packages (API, EF Core, messaging) contribute resolution strategies.

### Mediator
- `Headless.Mediator` — In-process request/handler dispatch with cross-cutting pipeline behaviors (validation, logging). Deliberately narrow: boundary concerns (auth, tenancy enforcement, idempotency, HTTP shaping) stay out of the pipeline.

### Audit Log
- `Headless.AuditLog.Abstractions` — Audit log contracts.
- `Headless.AuditLog.Storage.EntityFramework` — EF Core persistence for audit log.
- `Headless.AuditLog.Storage.PostgreSql` — PostgreSQL raw audit log storage.
- `Headless.AuditLog.Storage.SqlServer` — SQL Server raw audit log storage.

### Blob Storage
- `Headless.Blobs.Abstractions` — `IBlobStorage` interface.
- `Headless.Blobs.Core` — `AddHeadlessBlobs` setup builder and named `IBlobStorageProvider` resolution.
- `Headless.Blobs.Aws` — AWS S3 implementation.
- `Headless.Blobs.Azure` — Azure Blob Storage implementation.
- `Headless.Blobs.FileSystem` — Local file system implementation.
- `Headless.Blobs.Redis` — Redis implementation (small blobs).
- `Headless.Blobs.SshNet` — SFTP/SSH implementation.
- `Headless.Blobs.CloudflareR2` — Cloudflare R2 implementation (S3-compatible).

### Caching
- `Headless.Caching.Abstractions` — `ICache` interface.
- `Headless.Caching.Bcl` — BCL `IDistributedCache` adapter over a named Headless cache, for ASP.NET Core Session and standard integrations.
- `Headless.Caching.Core` — Shared factory-backed cache orchestration.
- `Headless.Caching.DistributedLocks` — Distributed factory-lock adapter for multi-node stampede protection.
- `Headless.Caching.InMemory` — In-process single-instance cache.
- `Headless.Caching.OutputCache` — ASP.NET Core `IOutputCacheStore` adapter over a named Headless cache; makes `AddOutputCache()` distributed and tag-aware.
- `Headless.Caching.Redis` — Redis distributed cache.
- `Headless.Caching.Hybrid` — L1 (memory) + L2 (distributed) cache.

### Captcha
- `Headless.Captcha.Abstractions` — `ICaptchaVerifier`, request/result contracts, the `AddHeadlessCaptcha` builder, and `ICaptchaProvider`.
- `Headless.Captcha.ReCaptcha` — Google reCAPTCHA v2 (checkbox) and v3 (invisible score) verification with Razor tag helpers.
- `Headless.Captcha.Turnstile` — Cloudflare Turnstile verification (pass/fail, `idempotency_key`, `cdata`) with Razor tag helpers.

### Commit Coordination
- `Headless.CommitCoordination.Abstractions` — Register-only commit coordinator contracts, work buffers, and capabilities.
- `Headless.CommitCoordination.Core` — In-process coordinator, ambient stack, scope factory, and relational capability implementation.
- `Headless.CommitCoordination.DurableWork` — Durable work buffer base with fail-closed relational provider policy.
- `Headless.CommitCoordination.EntityFramework` — EF Core commit coordination registration points.
- `Headless.CommitCoordination.InMemory` — Explicit in-process signal source for tests and owner-driven flows.
- `Headless.CommitCoordination.PostgreSql` — PostgreSQL inline commit signal source.
- `Headless.CommitCoordination.SqlServer` — SQL Server provider-key signal correlation.

### Coordination
- `Headless.Coordination.Abstractions` — Node identity, liveness, membership, and event contracts.
- `Headless.Coordination.Core` — Provider-agnostic heartbeat engine, event stream, and fail-stop membership service.
- `Headless.Coordination.Core.Database` — Shared relational substrate for native SQL providers.
- `Headless.Coordination.PostgreSql` — PostgreSQL membership provider using `clock_timestamp()`.
- `Headless.Coordination.Redis` — Redis membership provider using Lua and Redis `TIME`.
- `Headless.Coordination.SqlServer` — SQL Server membership provider using `SYSUTCDATETIME()`.

### Email
- `Headless.Emails.Abstractions` — Email sending interface.
- `Headless.Emails.Core` — Setup builder + MimeKit-based core utilities.
- `Headless.Emails.Aws` — AWS SES v2.
- `Headless.Emails.Azure` — Azure Communication Services Email.
- `Headless.Emails.Mailkit` — SMTP via MailKit.
- `Headless.Emails.Dev` — Dev no-op (use in local/dev).

### Feature Management
- `Headless.Features.Abstractions` — Feature flag interface.
- `Headless.Features.Core` — Feature management with caching and value providers.
- `Headless.Features.Storage.EntityFramework` — EF Core storage.
- `Headless.Features.Storage.PostgreSql` — PostgreSQL raw storage.
- `Headless.Features.Storage.SqlServer` — SQL Server raw storage.

### Identity
- `Headless.Identity.Storage.EntityFramework` — ASP.NET Core Identity EF Core storage with framework extensions.

### Imaging
- `Headless.Imaging.Abstractions` — Image processing interface.
- `Headless.Imaging.Core` — Contributor-based extensibility.
- `Headless.Imaging.ImageSharp` — ImageSharp-based resizing and compression.

### Logging
- `Headless.Logging.Serilog` — Serilog configuration factory with structured logging defaults.

### Media
- `Headless.Media.Indexing.Abstractions` — Media indexing interface.
- `Headless.Media.Indexing` — Text extraction (PDF, Word, PowerPoint).

### Messaging (Distributed Bus)
- `Headless.Messaging.Abstractions` — Standardized contracts for reliable messaging.
- `Headless.Messaging.Bus.Abstractions` — Broadcast (pub/subscribe) publisher contracts: `IBus` and `IOutboxBus`.
- `Headless.Messaging.Queue.Abstractions` — Point-to-point (work-queue) publisher contracts: `IQueue` and `IOutboxQueue`.
- `Headless.Messaging.Core` — Outbox runtime, retries, delayed delivery, consumer orchestration.
- `Headless.Messaging.Dashboard` — Web UI for monitoring messages, failures, retries.
- `Headless.Messaging.Dashboard.K8s` — Kubernetes node auto-discovery for the dashboard.
- `Headless.Messaging.OpenTelemetry` — Tracing, metrics, context propagation.
- `Headless.Messaging.RabbitMq` — RabbitMQ (AMQP) transport.
- `Headless.Messaging.Kafka` — Apache Kafka transport.
- `Headless.Messaging.Aws` — AWS SQS + SNS transport.
- `Headless.Messaging.AzureServiceBus` — Azure Service Bus transport.
- `Headless.Messaging.Nats` — NATS with JetStream transport.
- `Headless.Messaging.Pulsar` — Apache Pulsar transport.
- `Headless.Messaging.Redis` — Redis Streams queue transport and Redis Pub/Sub bus transport.
- `Headless.Messaging.InMemory` — In-memory transport (dev/testing).
- `Headless.Messaging.Storage.PostgreSql` — PostgreSQL durable storage.
- `Headless.Messaging.Storage.SqlServer` — SQL Server durable storage.
- `Headless.Messaging.InMemoryStorage` — Ephemeral storage (dev/testing).
- `Headless.Messaging.Testing` — In-process test harness for asserting dispatch/consume behavior (documented in [testing.md](testing.md)).

### OpenAPI
- `Headless.OpenApi.Nswag` — NSwag OpenAPI generation with framework processors.
- `Headless.OpenApi.Nswag.OData` — OData query parameter documentation.
- `Headless.OpenApi.Scalar` — Scalar API documentation UI.

### ORM
- `Headless.Orm.EntityFramework` — EF Core with framework conventions, global filters, DDD support.
- `Headless.Orm.EntityFramework.Messaging` — outbox bridge: dispatches integration events to the messaging outbox within the EF save transaction.
- `Headless.Orm.Couchbase` — Couchbase with bucket context and cluster management.

### Payments
- `Headless.Payments.Paymob.CashIn` — Paymob payment collection.
- `Headless.Payments.Paymob.CashOut` — Paymob disbursement.
- `Headless.Payments.Paymob.Services` — Higher-level Paymob services.

### Permissions
- `Headless.Permissions.Abstractions` — Permission management interface.
- `Headless.Permissions.Core` — Permission management with caching, providers, authorization.
- `Headless.Permissions.Storage.EntityFramework` — EF Core storage.
- `Headless.Permissions.Storage.PostgreSql` — PostgreSQL raw storage.
- `Headless.Permissions.Storage.SqlServer` — SQL Server raw storage.

### Push Notifications
- `Headless.PushNotifications.Abstractions` — Push notification interface.
- `Headless.PushNotifications.Firebase` — Firebase Cloud Messaging.
- `Headless.PushNotifications.Dev` — Dev no-op (use in local/dev).

### Distributed Locks
- `Headless.DistributedLocks.Abstractions` — Distributed lock interface.
- `Headless.DistributedLocks.Core` — Core implementation with storage abstraction.
- `Headless.DistributedLocks.InMemory` — In-process lock storage.
- `Headless.DistributedLocks.Core.Database` — Shared connection-scoped database lock engine.
- `Headless.DistributedLocks.PostgreSql` — PostgreSQL advisory-lock provider.
- `Headless.DistributedLocks.Redis` — Redis-based lock storage.
- `Headless.DistributedLocks.SqlServer` — SQL Server application-lock provider.

### Serialization
- `Headless.Serializer.Abstractions` — `ISerializer` interface.
- `Headless.Serializer.Json` — System.Text.Json implementation.
- `Headless.Serializer.MessagePack` — MessagePack binary implementation.

### Settings
- `Headless.Settings.Abstractions` — Dynamic settings interface.
- `Headless.Settings.Core` — Hierarchical value provider implementation.
- `Headless.Settings.Storage.EntityFramework` — EF Core storage.
- `Headless.Settings.Storage.PostgreSql` — PostgreSQL raw storage.
- `Headless.Settings.Storage.SqlServer` — SQL Server raw storage.

### SMS
- `Headless.Sms.Abstractions` — SMS sending interface.
- `Headless.Sms.Aws` — AWS SNS.
- `Headless.Sms.Cequens` — Cequens.
- `Headless.Sms.Connekio` — Connekio.
- `Headless.Sms.Infobip` — Infobip.
- `Headless.Sms.Twilio` — Twilio.
- `Headless.Sms.VictoryLink` — VictoryLink.
- `Headless.Sms.Vodafone` — Vodafone.
- `Headless.Sms.Dev` — Dev no-op (use in local/dev).

### SQL
- `Headless.Sql.Abstractions` — Connection factory interface.
- `Headless.Sql.PostgreSql` — PostgreSQL (Npgsql).
- `Headless.Sql.SqlServer` — SQL Server (Microsoft.Data.SqlClient).
- `Headless.Sql.Sqlite` — SQLite (Microsoft.Data.Sqlite).

### Testing
- `Headless.Testing` — xUnit base classes and testing utilities.
- `Headless.Testing.AspNetCore` — `WebApplicationFactory`-based fixtures with database reset for ASP.NET Core integration tests.
- `Headless.Testing.Testcontainers` — Testcontainers fixtures for integration tests.

### Jobs (Background Jobs)
- `Headless.Jobs.Abstractions` — Job scheduling interface.
- `Headless.Jobs.Core` — Reliable distributed job scheduling (cron, delayed execution, monitoring).
- `Headless.Jobs.SourceGenerator` — Compile-time codegen for `[JobFunction]`-marked methods.
- `Headless.Jobs.Dashboard` — Auth and web UI for job monitoring.
- `Headless.Jobs.OpenTelemetry` — Tracing and metrics.
- `Headless.Jobs.EntityFramework` — EF Core job state persistence; uses optional `Headless.Caching.ICache` for cron-expression caching.

### Dashboards (shared)
- `Headless.Dashboard.Authentication` — Shared authentication middleware (none / Basic / API key / host-app / custom) for the Jobs and Messaging dashboards.

### TUS (Resumable Uploads)
- `Headless.Tus` — Core TUS protocol utilities.
- `Headless.Tus.Azure` — Azure Blob Storage TUS store.
- `Headless.Tus.DistributedLocks` — TUS file locking via `Headless.DistributedLocks`.

### Utilities
- `Headless.FluentValidation` — FluentValidation rule and extension helpers.
- `Headless.Generator.Primitives` — Source generator for strongly-typed domain primitives.
- `Headless.Generator.Primitives.Abstractions` — Attributes for the primitives source generator.
- `Headless.Hosting` — Hosting utilities and options registration extensions.
- `Headless.NetTopologySuite` — Geospatial operations and SQL Server geography compatibility.
- `Headless.Redis` — Redis utilities and Lua script management for StackExchange.Redis.
- `Headless.Sitemaps` — XML sitemap generation.
- `Headless.Slugs` — URL-friendly slug generation.
