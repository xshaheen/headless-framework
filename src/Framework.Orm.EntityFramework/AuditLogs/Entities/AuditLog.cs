// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;
using Framework.Kernel.Domains;

namespace Framework.Orm.EntityFramework.AuditLogs.Entities;

public sealed class AuditLog : AggregateRoot<long>
{
    private AuditLog() { }

    public required string EntityKey { get; init; }

    public required string EntityType { get; init; }

    public required IReadOnlyList<AuditLogChange> Changes { get; init; }

    public static AuditLog Create(
        string entityKey,
        string entityName,
        IReadOnlyList<AuditLogChange> changes,
        long id = 0
    )
    {
        Argument.IsNotEmpty(changes);

        var auditLog = new AuditLog
        {
            Id = id,
            EntityKey = entityKey,
            EntityType = entityName,
            Changes = changes,
        };

        return auditLog;
    }
}
