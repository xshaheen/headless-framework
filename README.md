<div align="center">

# .NET Headless Framework

**The modular .NET framework that stays out of your way.**

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![GitHub Stars](https://img.shields.io/github/stars/xshaheen/headless-framework?style=social)](https://github.com/xshaheen/headless-framework)

116+ NuGet packages &bull; Abstraction + Provider pattern &bull; Zero lock-in

[Quick Start](#quick-start) &bull; [Packages](#packages) &bull; [Architecture](#architecture) &bull; [LLM Context](#llm-context) &bull; [Contributing](#contributing)

</div>

---

## Why Headless?

Most .NET frameworks force opinions on you — folder structures, ORM choices, messaging transports, cloud providers. **Headless doesn't.**

Every feature ships as a pair: a thin **abstraction** package (interfaces and contracts) and one or more **provider** packages (concrete implementations). You pick the pieces you need, wire them up, and own the result.

- **Composable** — 116+ standalone packages. Use one or use fifty.
- **Swappable** — Switch from Redis to in-memory caching, or AWS to Azure blob storage, by changing one line.
- **Explicit** — No hidden conventions, no magic. Every behavior is visible in your code.
- **Testable** — Every abstraction is mockable. Every provider is integration-tested with Testcontainers.

## Quick Start

```bash
dotnet add package Headless.Api
dotnet add package Headless.Caching.Redis
dotnet add package Headless.Orm.EntityFramework
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddHeadless();

builder.Services.AddHeadlessRedisCache(options =>
{
    options.ConnectionString = "localhost:6379";
});

builder.Services.AddAzureBlobStorage(options =>
{
    options.ConnectionString = "your-connection-string";
    options.ContainerName = "uploads";
});

builder.Services.AddAwsSesEmail(options =>
{
    options.FromEmail = "noreply@example.com";
});

var app = builder.Build();
app.UseResponseCompression();
app.UseHsts();
app.Run();
```

## Packages

### API & Web

Everything you need to stand up production-grade ASP.NET Core APIs — request/response conventions, validation pipelines, structured logging, and OpenAPI documentation out of the box.

| Package | Description |
|---------|-------------|
| [Headless.Api](src/Headless.Api/README.md) | ASP.NET Core API utilities and conventions |
| [Headless.Api.Abstractions](src/Headless.Api.Abstractions/README.md) | API abstractions and contracts |
| [Headless.Api.DataProtection](src/Headless.Api.DataProtection/README.md) | Data protection key storage |
| [Headless.Api.FluentValidation](src/Headless.Api.FluentValidation/README.md) | FluentValidation integration for APIs |
| [Headless.Api.Logging.Serilog](src/Headless.Api.Logging.Serilog/README.md) | Serilog logging integration |
| [Headless.Api.MinimalApi](src/Headless.Api.MinimalApi/README.md) | Minimal API utilities |
| [Headless.Api.Mvc](src/Headless.Api.Mvc/README.md) | MVC-specific utilities |

### Core

Foundational building blocks shared across the framework — domain primitives, DDD base types, guard clauses, and entity/event infrastructure.

| Package | Description |
|---------|-------------|
| [Headless.Extensions](src/Headless.Extensions/README.md) | Core primitives and utilities |
| [Headless.Core](src/Headless.Core/README.md) | Domain-Driven Design building blocks |
| [Headless.Security.Abstractions](src/Headless.Security.Abstractions/README.md) | Security contracts and options |
| [Headless.Security](src/Headless.Security/README.md) | String encryption and hashing services |
| [Headless.Checks](src/Headless.Checks/README.md) | Guard clauses and argument validation |
| [Headless.Domain](src/Headless.Domain/README.md) | Domain entities and events |
| [Headless.Domain.LocalPublisher](src/Headless.Domain.LocalPublisher/README.md) | In-process domain event publishing |

### Audit Log

Property-level audit logging for tracking entity mutations and explicit business events. Records what changed, who changed it, and when — with EF Core persistence.

| Package | Description |
|---------|-------------|
| [Headless.AuditLog.Abstractions](src/Headless.AuditLog.Abstractions/README.md) | Audit log contracts and interfaces |
| [Headless.AuditLog.EntityFramework](src/Headless.AuditLog.EntityFramework/README.md) | EF Core audit log persistence |

### Blob Storage

Unified blob storage interface with providers for every major cloud and protocol. Store and retrieve files without coupling to any single vendor.

| Package | Description |
|---------|-------------|
| [Headless.Blobs.Abstractions](src/Headless.Blobs.Abstractions/README.md) | Blob storage interfaces |
| [Headless.Blobs.Aws](src/Headless.Blobs.Aws/README.md) | AWS S3 blob storage |
| [Headless.Blobs.Azure](src/Headless.Blobs.Azure/README.md) | Azure Blob storage |
| [Headless.Blobs.FileSystem](src/Headless.Blobs.FileSystem/README.md) | Local filesystem storage |
| [Headless.Blobs.Redis](src/Headless.Blobs.Redis/README.md) | Redis blob storage |
| [Headless.Blobs.SshNet](src/Headless.Blobs.SshNet/README.md) | SFTP blob storage |

### Caching

Multi-tier caching with a clean abstraction layer. Supports in-memory, Redis, and hybrid (L1/L2) strategies — swap providers without touching business logic.

| Package | Description |
|---------|-------------|
| [Headless.Caching.Abstractions](src/Headless.Caching.Abstractions/README.md) | Caching interfaces |
| [Headless.Caching.Hybrid](src/Headless.Caching.Hybrid/README.md) | Hybrid caching (L1/L2) |
| [Headless.Caching.Memory](src/Headless.Caching.Memory/README.md) | In-memory caching |
| [Headless.Caching.Redis](src/Headless.Caching.Redis/README.md) | Redis caching |

### Email

Send transactional and marketing emails through a unified interface. Plug in AWS SES, SMTP via MailKit, or a no-op dev provider for local testing.

| Package | Description |
|---------|-------------|
| [Headless.Emails.Abstractions](src/Headless.Emails.Abstractions/README.md) | Email sending interfaces |
| [Headless.Emails.Core](src/Headless.Emails.Core/README.md) | Core email implementation |
| [Headless.Emails.Aws](src/Headless.Emails.Aws/README.md) | AWS SES email provider |
| [Headless.Emails.Dev](src/Headless.Emails.Dev/README.md) | Development email provider |
| [Headless.Emails.Mailkit](src/Headless.Emails.Mailkit/README.md) | MailKit SMTP provider |

### Feature Management

Runtime feature flags backed by persistent storage. Toggle features without redeployment and query flag state from anywhere in your application.

| Package | Description |
|---------|-------------|
| [Headless.Features.Abstractions](src/Headless.Features.Abstractions/README.md) | Feature flag interfaces |
| [Headless.Features.Core](src/Headless.Features.Core/README.md) | Feature management implementation |
| [Headless.Features.Storage.EntityFramework](src/Headless.Features.Storage.EntityFramework/README.md) | EF Core feature storage |

### Identity

Identity persistence and storage extensions for ASP.NET Core Identity, built on EF Core.

| Package | Description |
|---------|-------------|
| [Headless.Identity.Storage.EntityFramework](src/Headless.Identity.Storage.EntityFramework/README.md) | EF Core identity storage |

### Imaging

Image processing pipeline with pluggable backends. Resize, crop, convert, and optimize images through a clean abstraction.

| Package | Description |
|---------|-------------|
| [Headless.Imaging.Abstractions](src/Headless.Imaging.Abstractions/README.md) | Image processing interfaces |
| [Headless.Imaging.Core](src/Headless.Imaging.Core/README.md) | Core image processing |
| [Headless.Imaging.ImageSharp](src/Headless.Imaging.ImageSharp/README.md) | ImageSharp implementation |

### Logging

Structured logging utilities and enrichers built on top of Serilog.

| Package | Description |
|---------|-------------|
| [Headless.Logging.Serilog](src/Headless.Logging.Serilog/README.md) | Serilog logging utilities |

### Media

Content indexing and metadata extraction for media files — images, video, and documents.

| Package | Description |
|---------|-------------|
| [Headless.Media.Indexing.Abstractions](src/Headless.Media.Indexing.Abstractions/README.md) | Media indexing interfaces |
| [Headless.Media.Indexing](src/Headless.Media.Indexing/README.md) | Media indexing implementation |

### Messaging

Reliable distributed message bus with transactional outbox, automatic retries, delayed delivery, and type-safe consumers. 7 transport providers and 3 storage backends — swap the underlying infrastructure without changing application code.

| Package | Description |
|---------|-------------|
| [Headless.Messaging.Abstractions](src/Headless.Messaging.Abstractions/README.md) | Core messaging interfaces and contracts |
| [Headless.Messaging.Core](src/Headless.Messaging.Core/README.md) | Runtime engine: outbox, retries, delayed delivery, consumer orchestration |
| [Headless.Messaging.Dashboard](src/Headless.Messaging.Dashboard/README.md) | Web UI for monitoring messages, failures, and system health |
| [Headless.Messaging.Dashboard.K8s](src/Headless.Messaging.Dashboard.K8s/README.md) | Kubernetes node auto-discovery for the dashboard |
| [Headless.Messaging.OpenTelemetry](src/Headless.Messaging.OpenTelemetry/README.md) | Tracing, metrics, and context propagation |

**Transports:**

| Package | Description |
|---------|-------------|
| [Headless.Messaging.RabbitMq](src/Headless.Messaging.RabbitMq/README.md) | RabbitMQ (AMQP) |
| [Headless.Messaging.Kafka](src/Headless.Messaging.Kafka/README.md) | Apache Kafka |
| [Headless.Messaging.AwsSqs](src/Headless.Messaging.AwsSqs/README.md) | AWS SQS + SNS |
| [Headless.Messaging.AzureServiceBus](src/Headless.Messaging.AzureServiceBus/README.md) | Azure Service Bus |
| [Headless.Messaging.Nats](src/Headless.Messaging.Nats/README.md) | NATS with JetStream |
| [Headless.Messaging.Pulsar](src/Headless.Messaging.Pulsar/README.md) | Apache Pulsar |
| [Headless.Messaging.RedisStreams](src/Headless.Messaging.RedisStreams/README.md) | Redis Streams |
| [Headless.Messaging.InMemoryQueue](src/Headless.Messaging.InMemoryQueue/README.md) | In-memory (dev/testing) |

**Storage backends:**

| Package | Description |
|---------|-------------|
| [Headless.Messaging.PostgreSql](src/Headless.Messaging.PostgreSql/README.md) | PostgreSQL message persistence |
| [Headless.Messaging.SqlServer](src/Headless.Messaging.SqlServer/README.md) | SQL Server message persistence |
| [Headless.Messaging.InMemoryStorage](src/Headless.Messaging.InMemoryStorage/README.md) | Ephemeral storage (dev/testing) |

### Jobs

Distributed background job scheduling with cron expressions, delayed execution, monitoring dashboard, and OpenTelemetry observability. Source-generated for compile-time safety.

| Package | Description |
|---------|-------------|
| [Headless.Jobs.Abstractions](src/Headless.Jobs.Abstractions/README.md) | Job scheduling interfaces |
| [Headless.Jobs.Core](src/Headless.Jobs.Core/README.md) | Job engine: cron, delays, retries, monitoring |
| [Headless.Jobs.SourceGenerator](src/Headless.Jobs.SourceGenerator/README.md) | Compile-time code gen for `[Jobs]`-marked jobs |
| [Headless.Jobs.Dashboard](src/Headless.Jobs.Dashboard/README.md) | Web UI for job monitoring |
| [Headless.Jobs.OpenTelemetry](src/Headless.Jobs.OpenTelemetry/README.md) | Tracing and metrics for job execution |
| [Headless.Jobs.EntityFramework](src/Headless.Jobs.EntityFramework/README.md) | EF Core job state persistence |
| [Headless.Jobs.Caching.Redis](src/Headless.Jobs.Caching.Redis/README.md) | Redis node registry and heartbeats |

### OpenAPI

API documentation generation and interactive UIs. Supports NSwag for spec generation, OData query conventions, and Scalar for a modern API explorer.

| Package | Description |
|---------|-------------|
| [Headless.OpenApi.Nswag](src/Headless.OpenApi.Nswag/README.md) | NSwag OpenAPI generation |
| [Headless.OpenApi.Nswag.OData](src/Headless.OpenApi.Nswag.OData/README.md) | NSwag OData support |
| [Headless.OpenApi.Scalar](src/Headless.OpenApi.Scalar/README.md) | Scalar API documentation |

### ORM

Database access utilities for Entity Framework Core and Couchbase — conventions, seed data, soft deletes, and multi-tenancy support.

| Package | Description |
|---------|-------------|
| [Headless.Orm.EntityFramework](src/Headless.Orm.EntityFramework/README.md) | Entity Framework Core utilities |
| [Headless.Orm.Couchbase](src/Headless.Orm.Couchbase/README.md) | Couchbase ORM utilities |

### Payments

Payment gateway integrations for the MENA region. Cash-in (collection) and cash-out (disbursement) flows through Paymob.

| Package | Description |
|---------|-------------|
| [Headless.Payments.Paymob.CashIn](src/Headless.Payments.Paymob.CashIn/README.md) | Paymob cash-in payments |
| [Headless.Payments.Paymob.CashOut](src/Headless.Payments.Paymob.CashOut/README.md) | Paymob cash-out payments |
| [Headless.Payments.Paymob.Services](src/Headless.Payments.Paymob.Services/README.md) | Paymob shared services |

### Permissions

Dynamic, database-backed permission system. Define permissions as code, store assignments in EF Core, and query access control at runtime.

| Package | Description |
|---------|-------------|
| [Headless.Permissions.Abstractions](src/Headless.Permissions.Abstractions/README.md) | Permission system interfaces |
| [Headless.Permissions.Core](src/Headless.Permissions.Core/README.md) | Permission system implementation |
| [Headless.Permissions.Storage.EntityFramework](src/Headless.Permissions.Storage.EntityFramework/README.md) | EF Core permission storage |

### Push Notifications

Send push notifications through Firebase Cloud Messaging with a clean abstraction. Includes a no-op dev provider for local testing.

| Package | Description |
|---------|-------------|
| [Headless.PushNotifications.Abstractions](src/Headless.PushNotifications.Abstractions/README.md) | Push notification interfaces |
| [Headless.PushNotifications.Dev](src/Headless.PushNotifications.Dev/README.md) | Development push provider |
| [Headless.PushNotifications.Firebase](src/Headless.PushNotifications.Firebase/README.md) | Firebase Cloud Messaging |

### Distributed Locking

Coordinate access to shared resources across distributed services. Supports regular locks with expiration and throttling locks for rate limiting.

| Package | Description |
|---------|-------------|
| [Headless.DistributedLocks.Abstractions](src/Headless.DistributedLocks.Abstractions/README.md) | Distributed locking interfaces |
| [Headless.DistributedLocks.Core](src/Headless.DistributedLocks.Core/README.md) | Distributed locking implementation |
| [Headless.DistributedLocks.Cache](src/Headless.DistributedLocks.Cache/README.md) | Cache-based locking |
| [Headless.DistributedLocks.Redis](src/Headless.DistributedLocks.Redis/README.md) | Redis-based locking |

### Serialization

Pluggable serialization with providers for System.Text.Json and MessagePack. Use the same interface for JSON APIs and binary wire formats.

| Package | Description |
|---------|-------------|
| [Headless.Serializer.Abstractions](src/Headless.Serializer.Abstractions/README.md) | Serialization interfaces |
| [Headless.Serializer.Json](src/Headless.Serializer.Json/README.md) | System.Text.Json serializer |
| [Headless.Serializer.MessagePack](src/Headless.Serializer.MessagePack/README.md) | MessagePack serializer |

### Settings

Dynamic application settings stored in a database. Change configuration at runtime without redeployment, with caching and change notification support.

| Package | Description |
|---------|-------------|
| [Headless.Settings.Abstractions](src/Headless.Settings.Abstractions/README.md) | Dynamic settings interfaces |
| [Headless.Settings.Core](src/Headless.Settings.Core/README.md) | Settings management implementation |
| [Headless.Settings.Storage.EntityFramework](src/Headless.Settings.Storage.EntityFramework/README.md) | EF Core settings storage |

### SMS

Send SMS messages through a unified interface with providers for major regional and global carriers.

| Package | Description |
|---------|-------------|
| [Headless.Sms.Abstractions](src/Headless.Sms.Abstractions/README.md) | SMS sending interfaces |
| [Headless.Sms.Aws](src/Headless.Sms.Aws/README.md) | AWS SNS SMS provider |
| [Headless.Sms.Cequens](src/Headless.Sms.Cequens/README.md) | Cequens SMS provider |
| [Headless.Sms.Connekio](src/Headless.Sms.Connekio/README.md) | Connekio SMS provider |
| [Headless.Sms.Dev](src/Headless.Sms.Dev/README.md) | Development SMS provider |
| [Headless.Sms.Infobip](src/Headless.Sms.Infobip/README.md) | Infobip SMS provider |
| [Headless.Sms.Twilio](src/Headless.Sms.Twilio/README.md) | Twilio SMS provider |
| [Headless.Sms.VictoryLink](src/Headless.Sms.VictoryLink/README.md) | VictoryLink SMS provider |
| [Headless.Sms.Vodafone](src/Headless.Sms.Vodafone/README.md) | Vodafone SMS provider |

### SQL

Lightweight connection factories for raw SQL access when you need to drop below the ORM. Supports PostgreSQL, SQL Server, and SQLite.

| Package | Description |
|---------|-------------|
| [Headless.Sql.Abstractions](src/Headless.Sql.Abstractions/README.md) | SQL connection interfaces |
| [Headless.Sql.PostgreSql](src/Headless.Sql.PostgreSql/README.md) | PostgreSQL connection factory |
| [Headless.Sql.SqlServer](src/Headless.Sql.SqlServer/README.md) | SQL Server connection factory |
| [Headless.Sql.Sqlite](src/Headless.Sql.Sqlite/README.md) | SQLite connection factory |

### Testing

Test infrastructure and utilities — base classes, builders, fixtures, and Testcontainers integration for real-database integration tests.

| Package | Description |
|---------|-------------|
| [Headless.Testing](src/Headless.Testing/README.md) | Testing utilities and base classes |
| [Headless.Testing.Testcontainers](src/Headless.Testing.Testcontainers/README.md) | Testcontainers fixtures |

### TUS (Resumable Uploads)

[TUS protocol](https://tus.io) support for reliable, resumable file uploads. Handles large files gracefully with Azure Blob Storage and distributed locking.

| Package | Description |
|---------|-------------|
| [Headless.Tus](src/Headless.Tus/README.md) | TUS protocol utilities |
| [Headless.Tus.Azure](src/Headless.Tus.Azure/README.md) | Azure Blob TUS store |
| [Headless.Tus.DistributedLocks](src/Headless.Tus.DistributedLocks/README.md) | TUS file locking |

### Utilities

Cross-cutting utilities that don't belong to a specific domain — validation extensions, source generators, hosting helpers, geospatial, and more.

| Package | Description |
|---------|-------------|
| [Headless.FluentValidation](src/Headless.FluentValidation/README.md) | FluentValidation extensions |
| [Headless.Generator.Primitives](src/Headless.Generator.Primitives/README.md) | Primitive types source generator |
| [Headless.Generator.Primitives.Abstractions](src/Headless.Generator.Primitives.Abstractions/README.md) | Generator abstractions |
| [Headless.Hosting](src/Headless.Hosting/README.md) | .NET hosting utilities |
| [Headless.NetTopologySuite](src/Headless.NetTopologySuite/README.md) | Geospatial utilities |
| [Headless.ReCaptcha](src/Headless.ReCaptcha/README.md) | Google reCAPTCHA integration |
| [Headless.Redis](src/Headless.Redis/README.md) | Redis utilities |
| [Headless.Sitemaps](src/Headless.Sitemaps/README.md) | XML sitemap generation |
| [Headless.Slugs](src/Headless.Slugs/README.md) | URL slug generation |

## Architecture

Every feature follows the **abstraction + provider** pattern:

```
Headless.*.Abstractions  →  Interfaces and contracts (what)
Headless.*.<Provider>    →  Concrete implementation (how)
```

This means you can:
- **Swap providers** without touching business logic
- **Mock any dependency** in unit tests
- **Add your own provider** by implementing the abstraction

## LLM Context

Machine-readable project context for AI-assisted development:

- [`llms.txt`](llms.txt) — Compact overview
- [`llms-full.txt`](llms-full.txt) — Full package documentation

## For Consumers

If your project uses Headless packages, add the following to your `CLAUDE.md` or `AGENTS.md` so AI agents can fetch the correct documentation:

```markdown
## Headless Framework

This project uses [Headless .NET Framework](https://github.com/xshaheen/headless-framework) packages.

Documentation index: https://raw.githubusercontent.com/xshaheen/headless-framework/main/llms.txt

When working with a Headless domain, fetch the relevant domain doc:
- API & Web: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/api.md
- Core: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/core.md
- Multi-Tenancy: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/multi-tenancy.md
- Blob Storage: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/blobs.md
- Caching: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/caching.md
- Email: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/emails.md
- Feature Management: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/features.md
- Identity: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/identity.md
- Imaging: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/imaging.md
- Logging: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/logging.md
- Media: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/media.md
- Messaging (Distributed Bus): https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/messaging.md
- Audit Log: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/audit-log.md
- OpenAPI: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/openapi.md
- ORM: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/orm.md
- Payments: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/payments.md
- Permissions: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/permissions.md
- Push Notifications: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/push-notifications.md
- Distributed Locks: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/distributed-locks.md
- Serialization: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/serialization.md
- Settings: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/settings.md
- SMS: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/sms.md
- SQL: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/sql.md
- Testing: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/testing.md
- Jobs (Background Jobs): https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/jobs.md
- TUS (Resumable Uploads): https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/tus.md
- Utilities: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/utilities.md

Key rules:
- Use `ICache` from `Headless.Caching.Abstractions`, NOT `Microsoft.Extensions.Caching.Distributed.IDistributedCache`
- Use `IBlobStorage` from `Headless.Blobs.Abstractions`, not cloud SDK clients directly
- Use `Headless.Messaging` for distributed messaging, not raw transport clients (`RabbitMQ.Client`, `Confluent.Kafka`)
- Use `Headless.Jobs` for background jobs with `[Jobs]` attribute + source generator, not Hangfire/Quartz
- Use `*.Dev` packages (Emails.Dev, Sms.Dev, PushNotifications.Dev) in development
- Always depend on `*.Abstractions` packages for interfaces, add one provider for implementation
```

## Contributing

Contributions are welcome — issues, feature requests, and PRs. See individual package READMEs for package-specific details.
