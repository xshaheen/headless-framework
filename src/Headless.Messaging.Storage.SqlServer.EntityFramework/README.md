# Headless.Messaging.Storage.SqlServer.EntityFramework

Connects `Headless.Messaging.Storage.SqlServer` to an EF Core `DbContext` and enables the transactional outbox.

Install this adapter in addition to the raw SQL Server storage package when messaging should reuse a `DbContext` connection and enlist outbox writes in commit coordination.

```csharp
services.AddHeadlessMessaging(setup => setup.UseEntityFramework<AppDbContext>());
```
