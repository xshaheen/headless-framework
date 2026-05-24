# Headless.AuditLog.Storage.PostgreSql

PostgreSQL raw storage provider for `Headless.AuditLog`.

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

The provider creates the audit table at host startup and stores JSON columns as `jsonb` by default. Override `AuditLogStorageOptions.JsonColumnType` when a different PostgreSQL JSON/text column type is required.
