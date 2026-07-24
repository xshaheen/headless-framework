# Validation

Run these commands from the generated checkout:

```bash
docker compose up -d --wait
dotnet tool restore
dotnet restore HeadlessShop.slnx
dotnet test HeadlessShop.Tests.Architecture/HeadlessShop.Tests.Architecture.csproj
dotnet test HeadlessShop.Tests.Integration/HeadlessShop.Tests.Integration.csproj
```

The architecture tests enforce:

- no direct Catalog-to-Ordering or Ordering-to-Catalog references
- thin endpoint files
- Headless messaging abstractions instead of raw broker clients
- HTTP, Mediator, Messaging, and EF tenant posture configured together

The integration tests prove:

- product creation requires tenant context and permission
- product creation publishes `ProductCreated`
- product data and its outbox row commit or roll back together
- Ordering consumes the event and accepts an order for the projected product
- replaying `ProductCreated` does not duplicate the Ordering projection
- transient consumer failures retry and permanent failures remain in PostgreSQL
- duplicate SKUs return `409 Conflict`
- tenant B cannot read tenant A product data, even with a spoof header
- OpenAPI/Scalar are Development-only
- fake tour authentication headers are rejected in Production

Do not add reusable secrets, tokens, passwords, or production credentials to generated files. Keep local fake identity values in tests and examples only.

Integration tests require a running Docker daemon. Testcontainers starts an isolated PostgreSQL 17 container; it does not use the database from `compose.yaml`.
