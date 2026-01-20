# Headless .NET Framework

[![GitHub Stars](https://img.shields.io/github/stars/xshaheen/headless-framework?style=social)](https://github.com/xshaheen/headless-framework)

**[`headless-framework`](https://github.com/xshaheen/headless-framework)** is a modern, open-source **headless framework** for .NET developers who want full control with zero constraints.

## Key Features

- **Unopinionated Design** — Use your own patterns and architectures; the framework stays out of your way.
- **Supports Most Flows** — Built to integrate seamlessly with real-world use cases: CRUD, CQRS, messaging, file uploads, and more.
- **Composable & Modular** — Pick only what you need. Each piece is a standalone NuGet package.
- **Zero Lock-in** — Use with any storage, any frontend, any transport (REST, gRPC, GraphQL, etc.).
- **Developer-Centric** — Designed with clean architecture support, vertical slice support, CQRS, extensibility, and performance in mind.
- **No Magic, Just Code** — Everything is explicit. No hidden conventions or forced scaffolding.

## Ideal For .NET Developers Who:

- Prefer flexibility to convention
- Want to bootstrap quickly but scale cleanly
- Care about performance, testability, and maintainability

## Installation

All packages are available on NuGet. Install only what you need:

```bash
dotnet add package Framework.Api
dotnet add package Framework.Orm.EntityFramework
dotnet add package Framework.Caching.Foundatio.Redis
# ... and many more
```

## LLms

- [LLM Context (compact)](llms.txt)
- [LLM Context (full)](llms-full.txt)

## Packages

### API & Web

| Package | Description |
|---------|-------------|
| [Framework.Api](src/Framework.Api/README.md) | ASP.NET Core API utilities and conventions |
| [Framework.Api.Abstractions](src/Framework.Api.Abstractions/README.md) | API abstractions and contracts |
| [Framework.Api.DataProtection](src/Framework.Api.DataProtection/README.md) | Data protection key storage |
| [Framework.Api.FluentValidation](src/Framework.Api.FluentValidation/README.md) | FluentValidation integration for APIs |
| [Framework.Api.Logging.Serilog](src/Framework.Api.Logging.Serilog/README.md) | Serilog logging integration |
| [Framework.Api.MinimalApi](src/Framework.Api.MinimalApi/README.md) | Minimal API utilities |
| [Framework.Api.Mvc](src/Framework.Api.Mvc/README.md) | MVC-specific utilities |

### Core

| Package | Description |
|---------|-------------|
| [Framework.Base](src/Framework.Base/README.md) | Core primitives and utilities |
| [Framework.BuildingBlocks](src/Framework.BuildingBlocks/README.md) | Domain-Driven Design building blocks |
| [Framework.Checks](src/Framework.Checks/README.md) | Guard clauses and argument validation |
| [Framework.Domain](src/Framework.Domain/README.md) | Domain entities and events |
| [Framework.Specifications](src/Framework.Specifications/README.md) | Specification pattern implementation |

### Blob Storage

| Package | Description |
|---------|-------------|
| [Framework.Blobs.Abstractions](src/Framework.Blobs.Abstractions/README.md) | Blob storage interfaces |
| [Framework.Blobs.Aws](src/Framework.Blobs.Aws/README.md) | AWS S3 blob storage |
| [Framework.Blobs.Azure](src/Framework.Blobs.Azure/README.md) | Azure Blob storage |
| [Framework.Blobs.FileSystem](src/Framework.Blobs.FileSystem/README.md) | Local filesystem storage |
| [Framework.Blobs.Redis](src/Framework.Blobs.Redis/README.md) | Redis blob storage |
| [Framework.Blobs.SshNet](src/Framework.Blobs.SshNet/README.md) | SFTP blob storage |

### Caching

| Package | Description |
|---------|-------------|
| [Framework.Caching.Abstractions](src/Framework.Caching.Abstractions/README.md) | Caching interfaces |
| [Framework.Caching.Foundatio.Memory](src/Framework.Caching.Foundatio.Memory/README.md) | In-memory caching |
| [Framework.Caching.Foundatio.Redis](src/Framework.Caching.Foundatio.Redis/README.md) | Redis caching |

### Email

| Package | Description |
|---------|-------------|
| [Framework.Emails.Abstractions](src/Framework.Emails.Abstractions/README.md) | Email sending interfaces |
| [Framework.Emails.Core](src/Framework.Emails.Core/README.md) | Core email implementation |
| [Framework.Emails.Aws](src/Framework.Emails.Aws/README.md) | AWS SES email provider |
| [Framework.Emails.Dev](src/Framework.Emails.Dev/README.md) | Development email provider |
| [Framework.Emails.Mailkit](src/Framework.Emails.Mailkit/README.md) | MailKit SMTP provider |

### Feature Management

| Package | Description |
|---------|-------------|
| [Framework.Features.Abstractions](src/Framework.Features.Abstractions/README.md) | Feature flag interfaces |
| [Framework.Features.Core](src/Framework.Features.Core/README.md) | Feature management implementation |
| [Framework.Features.Storage.EntityFramework](src/Framework.Features.Storage.EntityFramework/README.md) | EF Core feature storage |

### Identity

| Package | Description |
|---------|-------------|
| [Framework.Identity.Storage.EntityFramework](src/Framework.Identity.Storage.EntityFramework/README.md) | EF Core identity storage |

### Imaging

| Package | Description |
|---------|-------------|
| [Framework.Imaging.Abstractions](src/Framework.Imaging.Abstractions/README.md) | Image processing interfaces |
| [Framework.Imaging.Core](src/Framework.Imaging.Core/README.md) | Core image processing |
| [Framework.Imaging.ImageSharp](src/Framework.Imaging.ImageSharp/README.md) | ImageSharp implementation |

### Logging

| Package | Description |
|---------|-------------|
| [Framework.Logging.Serilog](src/Framework.Logging.Serilog/README.md) | Serilog logging utilities |

### Media

| Package | Description |
|---------|-------------|
| [Framework.Media.Indexing.Abstractions](src/Framework.Media.Indexing.Abstractions/README.md) | Media indexing interfaces |
| [Framework.Media.Indexing](src/Framework.Media.Indexing/README.md) | Media indexing implementation |

### Messaging

| Package | Description |
|---------|-------------|
| [Framework.Messaging.Abstractions](src/Framework.Messaging.Abstractions/README.md) | Pub/sub messaging interfaces |
| [Framework.Messaging.Foundatio](src/Framework.Messaging.Foundatio/README.md) | Foundatio messaging |
| [Framework.Domain.LocalPublisher](src/Framework.Domain.LocalPublisher/README.md) | In-process messaging |

### OpenAPI

| Package | Description |
|---------|-------------|
| [Framework.OpenApi.Nswag](src/Framework.OpenApi.Nswag/README.md) | NSwag OpenAPI generation |
| [Framework.OpenApi.Nswag.OData](src/Framework.OpenApi.Nswag.OData/README.md) | NSwag OData support |
| [Framework.OpenApi.Scalar](src/Framework.OpenApi.Scalar/README.md) | Scalar API documentation |

### ORM

| Package | Description |
|---------|-------------|
| [Framework.Orm.EntityFramework](src/Framework.Orm.EntityFramework/README.md) | Entity Framework Core utilities |
| [Framework.Orm.Couchbase](src/Framework.Orm.Couchbase/README.md) | Couchbase ORM utilities |

### Payments

| Package | Description |
|---------|-------------|
| [Framework.Payments.Paymob.CashIn](src/Framework.Payments.Paymob.CashIn/README.md) | Paymob cash-in payments |
| [Framework.Payments.Paymob.CashOut](src/Framework.Payments.Paymob.CashOut/README.md) | Paymob cash-out payments |
| [Framework.Payments.Paymob.Services](src/Framework.Payments.Paymob.Services/README.md) | Paymob shared services |

### Permissions

| Package | Description |
|---------|-------------|
| [Framework.Permissions.Abstractions](src/Framework.Permissions.Abstractions/README.md) | Permission system interfaces |
| [Framework.Permissions.Core](src/Framework.Permissions.Core/README.md) | Permission system implementation |
| [Framework.Permissions.Storage.EntityFramework](src/Framework.Permissions.Storage.EntityFramework/README.md) | EF Core permission storage |

### Push Notifications

| Package | Description |
|---------|-------------|
| [Framework.PushNotifications.Abstractions](src/Framework.PushNotifications.Abstractions/README.md) | Push notification interfaces |
| [Framework.PushNotifications.Dev](src/Framework.PushNotifications.Dev/README.md) | Development push provider |
| [Framework.PushNotifications.Firebase](src/Framework.PushNotifications.Firebase/README.md) | Firebase Cloud Messaging |

### Queueing

| Package | Description |
|---------|-------------|
| [Framework.Queueing.Abstractions](src/Framework.Queueing.Abstractions/README.md) | Queue interfaces |
| [Framework.Queueing.Foundatio](src/Framework.Queueing.Foundatio/README.md) | Foundatio queuing |

### Resource Locking

| Package | Description |
|---------|-------------|
| [Framework.ResourceLocks.Abstractions](src/Framework.ResourceLocks.Abstractions/README.md) | Distributed locking interfaces |
| [Framework.ResourceLocks.Core](src/Framework.ResourceLocks.Core/README.md) | Distributed locking implementation |
| [Framework.ResourceLocks.Cache](src/Framework.ResourceLocks.Cache/README.md) | Cache-based locking |
| [Framework.ResourceLocks.Redis](src/Framework.ResourceLocks.Redis/README.md) | Redis-based locking |

### Serialization

| Package | Description |
|---------|-------------|
| [Framework.Serializer.Abstractions](src/Framework.Serializer.Abstractions/README.md) | Serialization interfaces |
| [Framework.Serializer.Json](src/Framework.Serializer.Json/README.md) | System.Text.Json serializer |
| [Framework.Serializer.MessagePack](src/Framework.Serializer.MessagePack/README.md) | MessagePack serializer |

### Settings

| Package | Description |
|---------|-------------|
| [Framework.Settings.Abstractions](src/Framework.Settings.Abstractions/README.md) | Dynamic settings interfaces |
| [Framework.Settings.Core](src/Framework.Settings.Core/README.md) | Settings management implementation |
| [Framework.Settings.Storage.EntityFramework](src/Framework.Settings.Storage.EntityFramework/README.md) | EF Core settings storage |

### SMS

| Package | Description |
|---------|-------------|
| [Framework.Sms.Abstractions](src/Framework.Sms.Abstractions/README.md) | SMS sending interfaces |
| [Framework.Sms.Aws](src/Framework.Sms.Aws/README.md) | AWS SNS SMS provider |
| [Framework.Sms.Cequens](src/Framework.Sms.Cequens/README.md) | Cequens SMS provider |
| [Framework.Sms.Connekio](src/Framework.Sms.Connekio/README.md) | Connekio SMS provider |
| [Framework.Sms.Dev](src/Framework.Sms.Dev/README.md) | Development SMS provider |
| [Framework.Sms.Infobip](src/Framework.Sms.Infobip/README.md) | Infobip SMS provider |
| [Framework.Sms.Twilio](src/Framework.Sms.Twilio/README.md) | Twilio SMS provider |
| [Framework.Sms.VictoryLink](src/Framework.Sms.VictoryLink/README.md) | VictoryLink SMS provider |
| [Framework.Sms.Vodafone](src/Framework.Sms.Vodafone/README.md) | Vodafone SMS provider |

### SQL

| Package | Description |
|---------|-------------|
| [Framework.Sql.Abstractions](src/Framework.Sql.Abstractions/README.md) | SQL connection interfaces |
| [Framework.Sql.PostgreSql](src/Framework.Sql.PostgreSql/README.md) | PostgreSQL connection factory |
| [Framework.Sql.SqlServer](src/Framework.Sql.SqlServer/README.md) | SQL Server connection factory |
| [Framework.Sql.Sqlite](src/Framework.Sql.Sqlite/README.md) | SQLite connection factory |

### Testing

| Package | Description |
|---------|-------------|
| [Framework.Testing](src/Framework.Testing/README.md) | Testing utilities and base classes |
| [Framework.Testing.Testcontainers](src/Framework.Testing.Testcontainers/README.md) | Testcontainers fixtures |

### TUS (Resumable Uploads)

| Package | Description |
|---------|-------------|
| [Framework.Tus](src/Framework.Tus/README.md) | TUS protocol utilities |
| [Framework.Tus.Azure](src/Framework.Tus.Azure/README.md) | Azure Blob TUS store |
| [Framework.Tus.ResourceLock](src/Framework.Tus.ResourceLock/README.md) | TUS file locking |

### Utilities

| Package | Description |
|---------|-------------|
| [Framework.FluentValidation](src/Framework.FluentValidation/README.md) | FluentValidation extensions |
| [Framework.Generator.Primitives](src/Framework.Generator.Primitives/README.md) | Primitive types source generator |
| [Framework.Generator.Primitives.Abstractions](src/Framework.Generator.Primitives.Abstractions/README.md) | Generator abstractions |
| [Framework.Hosting](src/Framework.Hosting/README.md) | .NET hosting utilities |
| [Framework.NetTopologySuite](src/Framework.NetTopologySuite/README.md) | Geospatial utilities |
| [Framework.Recaptcha](src/Framework.Recaptcha/README.md) | Google reCAPTCHA integration |
| [Framework.Redis](src/Framework.Redis/README.md) | Redis utilities |
| [Framework.Sitemaps](src/Framework.Sitemaps/README.md) | XML sitemap generation |
| [Framework.Slugs](src/Framework.Slugs/README.md) | URL slug generation |

## Quick Start

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add API infrastructure
builder.Services.AddHeadlessApi();

// Add caching
builder.Services.AddFoundatioRedisCache(options =>
{
    options.ConnectionString = "localhost:6379";
});

// Add blob storage
builder.Services.AddAzureBlobStorage(options =>
{
    options.ConnectionString = "your-connection-string";
    options.ContainerName = "uploads";
});

// Add email
builder.Services.AddAwsSesEmail(options =>
{
    options.FromEmail = "noreply@example.com";
});

var app = builder.Build();
app.UseHeadlessApi();
app.Run();
```

## Architecture Pattern

Each feature follows the **abstraction + provider pattern**:

- `Framework.*.Abstractions` — Interfaces and contracts
- `Framework.*.<Provider>` — Concrete implementation

This enables easy swapping of implementations and testing with mocks.

## Contributing

Feel free to submit issues, feature requests, or PRs. Let's build the ultimate headless .NET foundation together!
