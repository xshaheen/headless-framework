// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.Primitives;

namespace Headless.Permissions.Entities;

/// <summary>
/// Aggregate root representing the DB-persisted snapshot of a single <see cref="Models.PermissionDefinition"/>.
/// Serialized by <see cref="Definitions.IPermissionDefinitionSerializer"/> and stored by
/// <see cref="Repositories.IPermissionDefinitionRecordRepository"/>. All string length constraints are
/// defined in <see cref="PermissionDefinitionRecordConstants"/>.
/// </summary>
public sealed class PermissionDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    /// <summary>The name of the owning <see cref="PermissionGroupDefinitionRecord"/>.</summary>
    public string GroupName { get; set; }

    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>
    /// Name of the parent permission, or <see langword="null"/> if this permission is a direct child of the group.
    /// Used to reconstruct the permission hierarchy when rebuilding the in-memory cache from DB records.
    /// </summary>
    public string? ParentName { get; set; }

    /// <summary>
    /// Comma-separated list of grant-provider names that are allowed to manage this permission, or
    /// <see langword="null"/> when there are no restrictions (all providers may manage it).
    /// Serialized/deserialized by <see cref="Definitions.IPermissionDefinitionSerializer"/>.
    /// </summary>
    public required string? Providers { get; set; }

    public ExtraProperties ExtraProperties { get; private init; } = [];

    [UsedImplicitly]
    private PermissionDefinitionRecord()
    {
        GroupName = null!;
        Name = null!;
        DisplayName = null!;
        ExtraProperties = [];
    }

    [SetsRequiredMembers]
    public PermissionDefinitionRecord(
        Guid id,
        string groupName,
        string name,
        string? parentName,
        string displayName,
        bool isEnabled = true,
        string? providers = null
    )
    {
        Argument.IsLessThanOrEqualTo(groupName.Length, PermissionGroupDefinitionRecordConstants.NameMaxLength);

        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, PermissionDefinitionRecordConstants.NameMaxLength);

        Argument.IsNotNullOrWhiteSpace(displayName);
        Argument.IsLessThanOrEqualTo(displayName.Length, PermissionDefinitionRecordConstants.DisplayNameMaxLength);

        if (parentName != null)
        {
            Argument.IsLessThanOrEqualTo(parentName.Length, PermissionDefinitionRecordConstants.NameMaxLength);
        }

        Id = id;
        GroupName = groupName;
        Name = name;
        DisplayName = displayName;
        ParentName = parentName;
        IsEnabled = isEnabled;
        Providers = providers;
    }

    /// <summary>
    /// Returns <see langword="true"/> when all observable fields (name, group, display name, parent, enabled flag,
    /// providers, and extra properties) are equal to those of <paramref name="otherRecord"/>. Used during the save
    /// diff to skip unchanged records.
    /// </summary>
    public bool HasSameData(PermissionDefinitionRecord otherRecord)
    {
        if (!string.Equals(Name, otherRecord.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(GroupName, otherRecord.GroupName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(DisplayName, otherRecord.DisplayName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(ParentName, otherRecord.ParentName, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsEnabled != otherRecord.IsEnabled)
        {
            return false;
        }

        if (!string.Equals(Providers, otherRecord.Providers, StringComparison.Ordinal))
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
    /// Called during the save diff when a record is detected as changed; the patched instance is then
    /// passed to the repository for an update.
    /// </summary>
    public void Patch(PermissionDefinitionRecord otherRecord)
    {
        Name = otherRecord.Name;
        GroupName = otherRecord.GroupName;
        DisplayName = otherRecord.DisplayName;
        ParentName = otherRecord.ParentName;
        IsEnabled = otherRecord.IsEnabled;
        Providers = otherRecord.Providers;

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
