# AGENTS.md

## Project Overview

**headless-framework** is a modular .NET 10 framework for building APIs and backend services. Composed of ~94 NuGet packages organized by functional domains (API, Blobs, Caching, Messaging, ORM, etc.). Unopinionated, zero lock-in design.

**This is a framework, not a finished application.**
It is designed to support multiple projects and packages, both internal and external. As such, it may contain abstractions, extension points, and utility classes or methods that are not directly used within this repository. These elements exist deliberately to enable extensibility, customization, and reuse by downstream consumers and future integrations.

**Coverage targets:**
- **Line coverage**: ≥85% (minimum: 80%)
- **Branch coverage**: ≥80% (minimum: 70%)
- **Mutation score**: ≥70% (goal: 85%+)

## Architecture Pattern

Each feature follows **abstraction + provider pattern**:
- `Headless.*.Abstractions` — interfaces and contracts
- `Headless.*.<Provider>` — concrete implementation

Example: `Headless.Caching.Abstractions` + `Headless.Caching.Redis`

## Test Structure

- `*.Tests.Unit` — isolated, mocked, no external deps
- `*.Tests.Integration` — real deps via Testcontainers (requires Docker)
- `*.Tests.Harness` — shared fixtures and builders

**Stack**: xUnit, AwesomeAssertions (fork of FluentAssertions), NSubstitute, Bogus

## Code Conventions (Strictly Enforced)

**Required C# features**:

- File-scoped namespaces: `namespace X;`
- Primary constructors for DI
- `required`/`init` for properties
- `sealed` by default if not designed for inheritance
- Collection expressions: `[]`
- Pattern matching over old-style checks

## Package Management

- All versions in `Directory.Packages.props`. **Never** add `Version` attribute in `.csproj` files.

## Documentation

- Make sure to sync XML docs of the public APIs.
- Make sure to sync project README.md files for each package (exist in `src/Headless.*` folders).

## Tools

```bash
dotnet tool restore  # Install: csharpier, dotnet-ef, minver-cli, husky
```

## Design Decisions

### Input Validation Responsibility

This framework delegates certain input validation to consuming applications:

- **Cache key length limits**: Not enforced by `ICache` implementations. Consumers should validate key lengths at their application boundaries if DoS protection is needed.
- **Message payload sizes**: `CacheInvalidationMessage` and similar DTOs don't enforce size limits. Consumers should configure their messaging infrastructure (RabbitMQ, Redis, etc.) with appropriate limits.

## For Consumers

If your project uses Headless packages, add the following to your `CLAUDE.md` or `AGENTS.md` so AI agents can fetch the correct documentation:

```markdown
## Headless Framework

This project uses [Headless .NET Framework](https://github.com/xshaheen/headless-framework) packages.

Documentation index: https://raw.githubusercontent.com/xshaheen/headless-framework/main/llms.txt

When working with a Headless domain, fetch the relevant domain doc:
- API & Web: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/api.txt
- Core: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/core.txt
- Blob Storage: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/blobs.txt
- Caching: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/caching.txt
- Email: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/emails.txt
- Feature Management: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/features.txt
- Identity: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/identity.txt
- Imaging: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/imaging.txt
- Logging: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/logging.txt
- Media: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/media.txt
- Messaging: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/messaging.txt
- OpenAPI: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/openapi.txt
- ORM: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/orm.txt
- Payments: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/payments.txt
- Permissions: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/permissions.txt
- Push Notifications: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/push-notifications.txt
- Distributed Locks: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/distributed-locks.txt
- Serialization: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/serialization.txt
- Settings: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/settings.txt
- SMS: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/sms.txt
- SQL: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/sql.txt
- Testing: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/testing.txt
- Ticker (Background Jobs): https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/ticker.txt
- TUS (Resumable Uploads): https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/tus.txt
- Utilities: https://raw.githubusercontent.com/xshaheen/headless-framework/main/docs/llms/utilities.txt

Full documentation (all domains): https://raw.githubusercontent.com/xshaheen/headless-framework/main/llms-full.txt

Key rules:
- Use `ICache` from `Headless.Caching.Abstractions`, NOT `Microsoft.Extensions.Caching.Distributed.IDistributedCache`
- Use `IBlobStorage` from `Headless.Blobs.Abstractions`, not cloud SDK clients directly
- Use `*.Dev` packages (Emails.Dev, Sms.Dev, PushNotifications.Dev) in development
- Always depend on `*.Abstractions` packages for interfaces, add one provider for implementation
- All NuGet versions are in `Directory.Packages.props` — never add `Version` in `.csproj`
```