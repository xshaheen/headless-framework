// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Excludes a property or entire entity from audit log change tracking.
/// On a property: excluded from OldValues, NewValues, and ChangedFields.
/// On a class: the entity is excluded entirely (used in AuditAllEntities mode).
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class AuditIgnoreAttribute : Attribute;
