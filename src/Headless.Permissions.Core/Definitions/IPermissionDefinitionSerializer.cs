// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Core;
using Headless.Permissions.Entities;
using Headless.Permissions.Models;
using Headless.Primitives;

namespace Headless.Permissions.Definitions;

/// <summary>
/// Converts in-memory permission definitions into their database record equivalents so they can be
/// persisted by <see cref="Repositories.IPermissionDefinitionRecordRepository"/>.
/// </summary>
public interface IPermissionDefinitionSerializer
{
    /// <summary>
    /// Serializes a collection of permission groups and all their flattened child permissions into
    /// parallel record lists ready for batch persistence. Each record receives a new <see cref="Guid"/>
    /// id from <c>IGuidGenerator</c>.
    /// </summary>
    (IReadOnlyCollection<PermissionGroupDefinitionRecord>, IReadOnlyCollection<PermissionDefinitionRecord>) Serialize(
        IEnumerable<PermissionGroupDefinition> groups
    );

    /// <summary>Serializes a single permission group definition into a database record.</summary>
    PermissionGroupDefinitionRecord Serialize(PermissionGroupDefinition group);

    /// <summary>
    /// Serializes a single permission definition into a database record. The owning
    /// <paramref name="group"/> is required to populate <see cref="PermissionDefinitionRecord.GroupName"/>.
    /// The permission's <see cref="PermissionDefinition.Providers"/> list is stored as a comma-joined string.
    /// </summary>
    /// <param name="permission">The permission definition to serialize.</param>
    /// <param name="group">The group that owns this permission; used to set <c>GroupName</c> on the record.</param>
    PermissionDefinitionRecord Serialize(PermissionDefinition permission, PermissionGroupDefinition group);
}
