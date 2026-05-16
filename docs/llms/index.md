# Headless .NET Framework — LLM Index

> A modular .NET 10 framework for APIs and backend services. 116+ NuGet packages organized by domain, unopinionated, zero lock-in. Every feature ships as a thin `*.Abstractions` package plus one or more provider packages.

Start from an `*.Abstractions` package (interfaces/contracts) and add exactly one provider implementation (Redis/Azure/Aws/etc) per feature.

## Agent Instructions

Follow these rules when generating code that uses Headless packages:

- **Abstraction + Provider pattern**: Always depend on `Headless.*.Abstractions` interfaces. Add exactly one provider package (e.g., `Headless.Caching.Redis`) for the concrete implementation.
- **Use `ICache` not `IDistributedCache`**: Headless provides its own `ICache` from `Headless.Caching.Abstractions`. Never use `Microsoft.Extensions.Caching.Distributed.IDistributedCache`.
- **Use `IBlobStorage` not cloud SDKs directly**: Code against `Headless.Blobs.Abstractions.IBlobStorage`, not `Amazon.S3.IAmazonS3` or `Azure.Storage.Blobs.BlobServiceClient`.
- **Use `ISerializer` not `JsonSerializer` directly**: For serialization, use `Headless.Serializer.Abstractions.ISerializer`. Default to `Headless.Serializer.Json` unless binary performance is critical.
- **Use `Headless.Checks` for guard clauses**: Prefer `Argument.IsNotNull()` / `Ensure.*` over `ArgumentNullException.ThrowIfNull()`.
- **Use `Headless.Jobs` for background jobs**: Do not use Hangfire or Quartz. Mark jobs with `[Jobs]` and add `Headless.Jobs.SourceGenerator`.
- **Use `Headless.Messaging` for distributed messaging**: Do not use raw transport clients (`RabbitMQ.Client`, `Confluent.Kafka`). Use the Headless abstraction layer. Set `PublishOptions.TenantId` for typed envelope tenancy and read `ConsumeContext.TenantId` on consume; do not write the `headless-tenant-id` header directly (the publish pipeline rejects raw-only and mismatched writes).
- **Use `Headless.Features` for feature flags**: Do not use `Microsoft.FeatureManagement`.
- **Dev packages for local development**: Use `*.Dev` packages (e.g., `Emails.Dev`, `Sms.Dev`, `PushNotifications.Dev`) to avoid sending real messages during development.
- **NuGet versions**: All package versions are managed in `Directory.Packages.props`. Never add `Version` attributes in `.csproj` files.
- **C# conventions**: File-scoped namespaces, primary constructors, `sealed` by default, collection expressions `[]`, pattern matching.

## Domain Documentation

Each domain has a dedicated file with package READMEs, quick orientation, and per-domain agent instructions. Fetch the ones relevant to your task.

### API & Web
- [api.md](api.md) — ASP.NET Core API infrastructure, JWT, middleware, Minimal API and MVC integrations.

### Core
- [core.md](core.md) — Foundation utilities, DDD building blocks, guard clauses, domain events.

### Multi-Tenancy
- [multi-tenancy.md](multi-tenancy.md) — Tenant context setup across HTTP requests, EF Core filters, permission caching, and background processing.

### Blob Storage
- [blobs.md](blobs.md) — Unified file storage across AWS S3, Azure, file system, Redis, SFTP.

### Caching
- [caching.md](caching.md) — Unified caching with Memory, Redis, and Hybrid (L1+L2) providers.

### Email
- [emails.md](emails.md) — Email sending via AWS SES, MailKit SMTP, and dev no-op.

### Feature Management
- [features.md](features.md) — Feature flags with caching, value providers, and EF Core storage.

### Identity
- [identity.md](identity.md) — ASP.NET Core Identity with EF Core and framework extensions.

### Imaging
- [imaging.md](imaging.md) — Image processing with ImageSharp-based resizing and compression.

### Logging
- [logging.md](logging.md) — Serilog configuration factory with structured logging defaults.

### Media
- [media.md](media.md) — Text extraction from PDF, Word, and PowerPoint for search indexing.

### Messaging
- [messaging.md](messaging.md) — Distributed messaging with transactional outbox, 7 transports, 3 storage backends.

### OpenAPI
- [openapi.md](openapi.md) — NSwag OpenAPI generation and Scalar documentation UI.

### ORM
- [orm.md](orm.md) — Entity Framework Core and Couchbase integrations with DDD support.

### Payments
- [payments.md](payments.md) — Paymob Accept integration for cash-in and cash-out operations.

### Permissions
- [permissions.md](permissions.md) — Permission management with caching, authorization, and EF Core storage.

### Push Notifications
- [push-notifications.md](push-notifications.md) — Push notifications via Firebase FCM and dev no-op.

### Distributed Locks
- [distributed-locks.md](distributed-locks.md) — Distributed locking with Redis, cache-based, and in-memory backends.

### Serialization
- [serialization.md](serialization.md) — Unified serialization with System.Text.Json and MessagePack.

### Settings
- [settings.md](settings.md) — Dynamic runtime settings with hierarchical providers and EF Core storage.

### SMS
- [sms.md](sms.md) — SMS sending via Twilio, AWS SNS, Infobip, and regional providers.

### SQL
- [sql.md](sql.md) — SQL connection factories for PostgreSQL, SQL Server, and SQLite.

### Testing
- [testing.md](testing.md) — xUnit base classes and Testcontainers fixtures for integration tests.

### Jobs (Background Jobs)
- [jobs.md](jobs.md) — Distributed background job scheduling with cron, monitoring, and source generation.

### TUS (Resumable Uploads)
- [tus.md](tus.md) — TUS protocol for resumable file uploads with Azure and distributed lock support.

### Utilities
- [utilities.md](utilities.md) — FluentValidation extensions, source-generated primitives, reCAPTCHA, sitemaps, slugs, Redis utilities.
