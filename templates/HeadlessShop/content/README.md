# HeadlessShop

A production-shaped modular API showing Headless Framework tenancy, Mediator commands, EF Core persistence, and durable integration events without hiding the application boundaries.

## Run locally

Prerequisites: .NET 10 SDK and Docker.

```bash
docker compose up -d --wait
dotnet tool restore
dotnet restore HeadlessShop.slnx
dotnet run --project HeadlessShop.Api
```

Open `/scalar` in Development. The tour authentication handler accepts `X-Tour-User`, `X-Tour-Tenant`, and `X-Tour-Permissions` headers only when `HeadlessShop:AllowFakeTourAuth` is enabled; replace it with your identity provider before deployment.

Run all tests (integration tests start an isolated PostgreSQL container):

```bash
dotnet test HeadlessShop.slnx
```

Reset local data:

```bash
docker compose down --volumes
```

## Messaging and outbox

Creating a product adds `ProductCreated` inside the aggregate. `CatalogDbContext.SaveChangesAsync` persists the product and writes the integration event through the Headless EF outbox bridge. PostgreSQL is also the durable messaging store. The background dispatcher then publishes the event and the Ordering consumer maintains a tenant-scoped product snapshot. The upsert makes replaying the same product event safe.

The template deliberately uses the in-memory transport so the capability tour stays single-process. It is not a distributed production broker: select a Headless transport such as RabbitMQ, Kafka, or NATS before splitting modules into separate processes. Persisted dispatch has a bounded retry budget; exhausted messages remain observable in messaging storage rather than disappearing.

See `docs/architecture.md` for boundaries, `docs/validation.md` for verification, and `docs/recipes/add-command.md` for an extension walkthrough.

## Database migrations

The template includes normal EF Core migrations and model snapshots for both module-owned schemas. With PostgreSQL running, add a migration to the owning module:

```bash
dotnet ef migrations add DescribeCatalogChange \
  --project HeadlessShop.Catalog.Infrastructure \
  --startup-project HeadlessShop.Api \
  --context CatalogDbContext

dotnet ef migrations add DescribeOrderingChange \
  --project HeadlessShop.Ordering.Infrastructure \
  --startup-project HeadlessShop.Api \
  --context OrderingDbContext
```

Review generated SQL before deployment. The tour migrates on startup for convenience; production systems commonly run migrations as a separate deployment step.

## Production notes

- Override `ConnectionStrings__Shop` and all `HeadlessShop` encryption/hash settings with secrets.
- Replace fake tour authentication and define real permission claims.
- Run migrations as a controlled deployment step if startup migration is not acceptable.
- Use least-privilege PostgreSQL roles, TLS, backups, and monitoring.
- Keep consumers idempotent: delivery is at least once, so external side effects need their own durable idempotency key.
- Add a distributed transport before running more than one API process.
