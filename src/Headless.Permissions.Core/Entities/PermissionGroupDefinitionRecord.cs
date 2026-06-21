// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.Primitives;

namespace Headless.Permissions.Entities;

/// <summary>
/// Aggregate root representing the DB-persisted snapshot of a <see cref="Models.PermissionGroupDefinition"/>.
/// Serialized by <see cref="Definitions.IPermissionDefinitionSerializer"/> and stored by
/// <see cref="Repositories.IPermissionDefinitionRecordRepository"/>. All string length constraints are
/// defined in <see cref="PermissionGroupDefinitionRecordConstants"/>.
/// </summary>
public sealed class PermissionGroupDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    public ExtraProperties ExtraProperties { get; private init; }

    public PermissionGroupDefinitionRecord()
    {
        ExtraProperties = [];
    }

    [SetsRequiredMembers]
    public PermissionGroupDefinitionRecord(Guid id, string name, string displayName)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, PermissionGroupDefinitionRecordConstants.NameMaxLength);
        Argument.IsNotNullOrWhiteSpace(displayName);
        Argument.IsLessThanOrEqualTo(displayName.Length, PermissionGroupDefinitionRecordConstants.DisplayNameMaxLength);

        Id = id;
        Name = name;
        DisplayName = displayName;
        ExtraProperties = [];
    }

    /// <summary>
    /// Returns <see langword="true"/> when all observable fields (name, display name, and extra properties)
    /// match those of <paramref name="otherRecord"/>. Used during the save diff to skip unchanged groups.
    /// </summary>
    public bool HasSameData(PermissionGroupDefinitionRecord otherRecord)
    {
        if (!string.Equals(Name, otherRecord.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(DisplayName, otherRecord.DisplayName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!this.HasSameExtraProperties(otherRecord))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Overwrites all mutable fields on this record with values from <paramref name="otherRecord"/>.
    /// Called during the save diff when a group is detected as changed.
    /// </summary>
    public void Patch(PermissionGroupDefinitionRecord otherRecord)
    {
        Name = otherRecord.Name;
        DisplayName = otherRecord.DisplayName;

        if (!this.HasSameExtraProperties(otherRecord))
        {
            ExtraProperties.Clear();

            foreach (var property in otherRecord.ExtraProperties)
            {
                ExtraProperties.Add(property.Key, property.Value);
            }
        }
    }
}
