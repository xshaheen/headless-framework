// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

internal static class AuditActionNames
{
    public const string SoftDeleted = "entity.soft_deleted";
    public const string Restored = "entity.restored";
    public const string Suspended = "entity.suspended";
    public const string Unsuspended = "entity.unsuspended";
    public const string Created = "entity.created";
    public const string Updated = "entity.updated";
    public const string Deleted = "entity.deleted";
    public const string Unknown = "entity.unknown";
}
