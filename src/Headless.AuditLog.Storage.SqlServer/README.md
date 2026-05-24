# Headless.AuditLog.Storage.SqlServer

SQL Server raw storage provider for `Headless.AuditLog`.

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

The provider creates the audit table at host startup and stores JSON payloads as `nvarchar(max)` by default. Override `AuditLogStorageOptions.JsonColumnType` when a different SQL Server column type is required.
