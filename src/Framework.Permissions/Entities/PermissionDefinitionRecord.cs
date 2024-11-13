// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.Checks;
using Framework.Kernel.Domains;
using Framework.Kernel.Primitives;

namespace Framework.Permissions.Entities;

public sealed class PermissionDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    public string GroupName { get; set; }

    public required string Name { get; set; }

    public required string DisplayName { get; set; }

    public bool IsEnabled { get; set; }

    public string? ParentName { get; set; }

    /// <summary>Comma separated the list of provider names.</summary>
    public required string? Providers { get; set; }

    public ExtraProperties ExtraProperties { get; private set; } = [];

    private PermissionDefinitionRecord()
    {
        GroupName = default!;
        Name = default!;
        DisplayName = default!;
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

    public void Patch(PermissionDefinitionRecord otherRecord)
    {
        if (!string.Equals(Name, otherRecord.Name, StringComparison.Ordinal))
        {
            Name = otherRecord.Name;
        }

        if (!string.Equals(GroupName, otherRecord.GroupName, StringComparison.Ordinal))
        {
            GroupName = otherRecord.GroupName;
        }

        if (!string.Equals(DisplayName, otherRecord.DisplayName, StringComparison.Ordinal))
        {
            DisplayName = otherRecord.DisplayName;
        }

        if (!string.Equals(ParentName, otherRecord.ParentName, StringComparison.Ordinal))
        {
            ParentName = otherRecord.ParentName;
        }

        if (IsEnabled != otherRecord.IsEnabled)
        {
            IsEnabled = otherRecord.IsEnabled;
        }

        if (!string.Equals(Providers, otherRecord.Providers, StringComparison.Ordinal))
        {
            Providers = otherRecord.Providers;
        }

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
