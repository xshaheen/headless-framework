# Headless.AuditLog.Storage.PostgreSql

PostgreSQL raw storage provider for `Headless.AuditLog`. No Entity Framework dependency — uses Npgsql directly. Creates the audit table at host startup and stores JSON columns as `jsonb` by default.

```bash
dotnet add package Headless.AuditLog.Storage.PostgreSql
```

```csharp
services.AddHeadlessAuditLog();
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(options =>
    {
        options.Schema = "audit";
        options.TableName = "audit_log";
    });
    setup.UsePostgreSql(connectionString);
});
```

Override `AuditLogStorageOptions.JsonColumnType` when a different PostgreSQL JSON/text column type is required (`Jsonb`, `Json`, `NvarcharMax`).

## Mixed mode (raw store + HeadlessDbContext)

When the consumer also uses `AddHeadlessDbContext<TContext>`, EF change-capture is wired automatically (registered alongside the SaveChanges pipeline in `Headless.Orm.EntityFramework`). No extra setup is required. The raw store enrolls in the consumer's ambient `DbContext` transaction when the providers match (both Npgsql) so audit rows commit atomically with the entity batch; on provider mismatch the store falls back to its own connection with a one-time warning log.

## Standalone mode (no EF)

The package's only external dependencies are `Npgsql` and `Headless.AuditLog.Abstractions`. Consumers that don't use a `HeadlessDbContext` get the audit log without pulling Entity Framework into the dependency graph.
