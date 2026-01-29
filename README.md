# .NET Headless Framework

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
dotnet add package Headless.Api
dotnet add package Headless.Orm.EntityFramework
dotnet add package Headless.Caching.Redis
# ... and many more
```

## LLms

- [LLM Context (compact)](llms.txt)
- [LLM Context (full)](llms-full.txt)

## Packages

### API & Web

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

| Package | Description |
|---------|-------------|
| [Headless.Base](src/Headless.Base/README.md) | Core primitives and utilities |
| [Headless.BuildingBlocks](src/Headless.BuildingBlocks/README.md) | Domain-Driven Design building blocks |
| [Headless.Checks](src/Headless.Checks/README.md) | Guard clauses and argument validation |
| [Headless.Domain](src/Headless.Domain/README.md) | Domain entities and events |
| [Headless.Specifications](src/Headless.Specifications/README.md) | Specification pattern implementation |

### Blob Storage

| Package | Description |
|---------|-------------|
| [Headless.Blobs.Abstractions](src/Headless.Blobs.Abstractions/README.md) | Blob storage interfaces |
| [Headless.Blobs.Aws](src/Headless.Blobs.Aws/README.md) | AWS S3 blob storage |
| [Headless.Blobs.Azure](src/Headless.Blobs.Azure/README.md) | Azure Blob storage |
| [Headless.Blobs.FileSystem](src/Headless.Blobs.FileSystem/README.md) | Local filesystem storage |
| [Headless.Blobs.Redis](src/Headless.Blobs.Redis/README.md) | Redis blob storage |
| [Headless.Blobs.SshNet](src/Headless.Blobs.SshNet/README.md) | SFTP blob storage |

### Caching

| Package | Description |
|---------|-------------|
| [Headless.Caching.Abstractions](src/Headless.Caching.Abstractions/README.md) | Caching interfaces |
| [Headless.Caching.Foundatio.Memory](src/Headless.Caching.Foundatio.Memory/README.md) | In-memory caching |
| [Headless.Caching.Foundatio.Redis](src/Headless.Caching.Foundatio.Redis/README.md) | Redis caching |

### Email

| Package | Description |
|---------|-------------|
| [Headless.Emails.Abstractions](src/Headless.Emails.Abstractions/README.md) | Email sending interfaces |
| [Headless.Emails.Core](src/Headless.Emails.Core/README.md) | Core email implementation |
| [Headless.Emails.Aws](src/Headless.Emails.Aws/README.md) | AWS SES email provider |
| [Headless.Emails.Dev](src/Headless.Emails.Dev/README.md) | Development email provider |
| [Headless.Emails.Mailkit](src/Headless.Emails.Mailkit/README.md) | MailKit SMTP provider |

### Feature Management

| Package | Description |
|---------|-------------|
| [Headless.Features.Abstractions](src/Headless.Features.Abstractions/README.md) | Feature flag interfaces |
| [Headless.Features.Core](src/Headless.Features.Core/README.md) | Feature management implementation |
| [Headless.Features.Storage.EntityFramework](src/Headless.Features.Storage.EntityFramework/README.md) | EF Core feature storage |

### Identity

| Package | Description |
|---------|-------------|
| [Headless.Identity.Storage.EntityFramework](src/Headless.Identity.Storage.EntityFramework/README.md) | EF Core identity storage |

### Imaging

| Package | Description |
|---------|-------------|
| [Headless.Imaging.Abstractions](src/Headless.Imaging.Abstractions/README.md) | Image processing interfaces |
| [Headless.Imaging.Core](src/Headless.Imaging.Core/README.md) | Core image processing |
| [Headless.Imaging.ImageSharp](src/Headless.Imaging.ImageSharp/README.md) | ImageSharp implementation |

### Logging

| Package | Description |
|---------|-------------|
| [Headless.Logging.Serilog](src/Headless.Logging.Serilog/README.md) | Serilog logging utilities |

### Media

| Package | Description |
|---------|-------------|
| [Headless.Media.Indexing.Abstractions](src/Headless.Media.Indexing.Abstractions/README.md) | Media indexing interfaces |
| [Headless.Media.Indexing](src/Headless.Media.Indexing/README.md) | Media indexing implementation |

### Messaging

| Package | Description |
|---------|-------------|
| [Headless.Domain.LocalPublisher](src/Headless.Domain.LocalPublisher/README.md) | In-process messaging |

### OpenAPI

| Package | Description |
|---------|-------------|
| [Headless.OpenApi.Nswag](src/Headless.OpenApi.Nswag/README.md) | NSwag OpenAPI generation |
| [Headless.OpenApi.Nswag.OData](src/Headless.OpenApi.Nswag.OData/README.md) | NSwag OData support |
| [Headless.OpenApi.Scalar](src/Headless.OpenApi.Scalar/README.md) | Scalar API documentation |

### ORM

| Package | Description |
|---------|-------------|
| [Headless.Orm.EntityFramework](src/Headless.Orm.EntityFramework/README.md) | Entity Framework Core utilities |
| [Headless.Orm.Couchbase](src/Headless.Orm.Couchbase/README.md) | Couchbase ORM utilities |

### Payments

| Package | Description |
|---------|-------------|
| [Headless.Payments.Paymob.CashIn](src/Headless.Payments.Paymob.CashIn/README.md) | Paymob cash-in payments |
| [Headless.Payments.Paymob.CashOut](src/Headless.Payments.Paymob.CashOut/README.md) | Paymob cash-out payments |
| [Headless.Payments.Paymob.Services](src/Headless.Payments.Paymob.Services/README.md) | Paymob shared services |

### Permissions

| Package | Description |
|---------|-------------|
| [Headless.Permissions.Abstractions](src/Headless.Permissions.Abstractions/README.md) | Permission system interfaces |
| [Headless.Permissions.Core](src/Headless.Permissions.Core/README.md) | Permission system implementation |
| [Headless.Permissions.Storage.EntityFramework](src/Headless.Permissions.Storage.EntityFramework/README.md) | EF Core permission storage |

### Push Notifications

| Package | Description |
|---------|-------------|
| [Headless.PushNotifications.Abstractions](src/Headless.PushNotifications.Abstractions/README.md) | Push notification interfaces |
| [Headless.PushNotifications.Dev](src/Headless.PushNotifications.Dev/README.md) | Development push provider |
| [Headless.PushNotifications.Firebase](src/Headless.PushNotifications.Firebase/README.md) | Firebase Cloud Messaging |

### Queueing

| Package | Description |
|---------|-------------|
| [Headless.Queueing.Abstractions](src/Headless.Queueing.Abstractions/README.md) | Queue interfaces |
| [Headless.Queueing.Foundatio](src/Headless.Queueing.Foundatio/README.md) | Foundatio queuing |

### Resource Locking

| Package | Description |
|---------|-------------|
| [Headless.ResourceLocks.Abstractions](src/Headless.ResourceLocks.Abstractions/README.md) | Distributed locking interfaces |
| [Headless.ResourceLocks.Core](src/Headless.ResourceLocks.Core/README.md) | Distributed locking implementation |
| [Headless.ResourceLocks.Cache](src/Headless.ResourceLocks.Cache/README.md) | Cache-based locking |
| [Headless.ResourceLocks.Redis](src/Headless.ResourceLocks.Redis/README.md) | Redis-based locking |

### Serialization

| Package | Description |
|---------|-------------|
| [Headless.Serializer.Abstractions](src/Headless.Serializer.Abstractions/README.md) | Serialization interfaces |
| [Headless.Serializer.Json](src/Headless.Serializer.Json/README.md) | System.Text.Json serializer |
| [Headless.Serializer.MessagePack](src/Headless.Serializer.MessagePack/README.md) | MessagePack serializer |

### Settings

| Package | Description |
|---------|-------------|
| [Headless.Settings.Abstractions](src/Headless.Settings.Abstractions/README.md) | Dynamic settings interfaces |
| [Headless.Settings.Core](src/Headless.Settings.Core/README.md) | Settings management implementation |
| [Headless.Settings.Storage.EntityFramework](src/Headless.Settings.Storage.EntityFramework/README.md) | EF Core settings storage |

### SMS

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

| Package | Description |
|---------|-------------|
| [Headless.Sql.Abstractions](src/Headless.Sql.Abstractions/README.md) | SQL connection interfaces |
| [Headless.Sql.PostgreSql](src/Headless.Sql.PostgreSql/README.md) | PostgreSQL connection factory |
| [Headless.Sql.SqlServer](src/Headless.Sql.SqlServer/README.md) | SQL Server connection factory |
| [Headless.Sql.Sqlite](src/Headless.Sql.Sqlite/README.md) | SQLite connection factory |

### Testing

| Package | Description |
|---------|-------------|
| [Headless.Testing](src/Headless.Testing/README.md) | Testing utilities and base classes |
| [Headless.Testing.Testcontainers](src/Headless.Testing.Testcontainers/README.md) | Testcontainers fixtures |

### TUS (Resumable Uploads)

| Package | Description |
|---------|-------------|
| [Headless.Tus](src/Headless.Tus/README.md) | TUS protocol utilities |
| [Headless.Tus.Azure](src/Headless.Tus.Azure/README.md) | Azure Blob TUS store |
| [Headless.Tus.ResourceLock](src/Headless.Tus.ResourceLock/README.md) | TUS file locking |

### Utilities

| Package | Description |
|---------|-------------|
| [Headless.FluentValidation](src/Headless.FluentValidation/README.md) | FluentValidation extensions |
| [Headless.Generator.Primitives](src/Headless.Generator.Primitives/README.md) | Primitive types source generator |
| [Headless.Generator.Primitives.Abstractions](src/Headless.Generator.Primitives.Abstractions/README.md) | Generator abstractions |
| [Headless.Hosting](src/Headless.Hosting/README.md) | .NET hosting utilities |
| [Headless.NetTopologySuite](src/Headless.NetTopologySuite/README.md) | Geospatial utilities |
| [Headless.Recaptcha](src/Headless.Recaptcha/README.md) | Google reCAPTCHA integration |
| [Headless.Redis](src/Headless.Redis/README.md) | Redis utilities |
| [Headless.Sitemaps](src/Headless.Sitemaps/README.md) | XML sitemap generation |
| [Headless.Slugs](src/Headless.Slugs/README.md) | URL slug generation |

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

- `Headless.*.Abstractions` — Interfaces and contracts
- `Headless.*.<Provider>` — Concrete implementation

This enables easy swapping of implementations and testing with mocks.

## Contributing

Feel free to submit issues, feature requests, or PRs. Let's build the ultimate headless .NET foundation together!
