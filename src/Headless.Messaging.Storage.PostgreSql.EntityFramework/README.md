# Headless.Messaging.Storage.PostgreSql.EntityFramework

Connects `Headless.Messaging.Storage.PostgreSql` to an EF Core `DbContext` and enables the transactional outbox.

Install this adapter in addition to the raw PostgreSQL storage package when messaging should reuse a `DbContext` connection and enlist outbox writes in commit coordination.

```csharp
services.AddHeadlessMessaging(setup => setup.UseEntityFramework<AppDbContext>());
```
