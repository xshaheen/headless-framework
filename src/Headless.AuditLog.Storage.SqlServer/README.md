# Headless.AuditLog.Storage.SqlServer

SQL Server raw storage provider for `Headless.AuditLog`. No Entity Framework dependency — uses `Microsoft.Data.SqlClient` directly. Creates the audit table at host startup and stores JSON payloads as `nvarchar(max)` by default.

```bash
dotnet add package Headless.AuditLog.Storage.SqlServer
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
    setup.UseSqlServer(connectionString);
});
```

Override `AuditLogStorageOptions.JsonColumnType` when a different SQL Server column type is required.

Set `AuditLogStorageOptions.InitializeOnStartup = false` to skip the startup DDL when the schema is provisioned out-of-band (a migrations job or DBA). The initializer becomes a no-op but still reports `IsInitialized = true`, so dependents awaiting `WaitForInitializationAsync` do not block. Defaults to `true`.

```csharp
services.AddHeadlessAuditLog(setup =>
{
    setup.ConfigureStorage(o => o.InitializeOnStartup = false);
    setup.UseSqlServer(connectionString);
});
```

## Mixed mode (raw store + HeadlessDbContext)

When the consumer also uses `AddHeadlessDbContext<TContext>`, EF change-capture is wired automatically (registered alongside the SaveChanges pipeline in `Headless.Orm.EntityFramework`). No extra setup is required. The raw store enrolls in the consumer's ambient `DbContext` transaction when the providers match (both SqlClient) so audit rows commit atomically with the entity batch; on provider mismatch the store falls back to its own connection with a one-time warning log.

## Standalone mode (no EF)

The package's only external dependencies are `Microsoft.Data.SqlClient` and `Headless.AuditLog.Abstractions`. Consumers that don't use a `HeadlessDbContext` get the audit log without pulling Entity Framework into the dependency graph.
