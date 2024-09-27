// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Framework.Kernel.BuildingBlocks;
using Framework.Kernel.Domains;

namespace Framework.Orm.EntityFramework.AuditLogs.Entities;

public sealed class AuditLogChange : Entity<long>
{
    private AuditLogChange() { }

    public long AuditLogId { get; init; }

    public required string ParentEntityKey { get; init; }

    public required string ParentEntityType { get; init; }

    public required string EntityKey { get; init; }

    public required string EntityType { get; init; }

    public required string Action { get; init; }

    public required string Changes { get; init; }

    [NotMapped]
    public required IReadOnlyDictionary<string, object> ChangesMap { get; init; }

    public static AuditLogChange CreateInstance(
        string entityKey,
        string entityType,
        string parentEntityKey,
        string parentEntityType,
        string action,
        IReadOnlyDictionary<string, object> change,
        long auditLogId = 0,
        long id = 0
    )
    {
        return new()
        {
            Id = id,
            AuditLogId = auditLogId,
            EntityKey = entityKey,
            EntityType = entityType,
            ParentEntityKey = parentEntityKey,
            ParentEntityType = parentEntityType,
            Action = action,
            ChangesMap = change,
            Changes = JsonSerializer.Serialize(change, FrameworkJsonConstants.DefaultWebJsonOptions),
        };
    }
}
