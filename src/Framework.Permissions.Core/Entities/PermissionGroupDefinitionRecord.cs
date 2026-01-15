// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
using Framework.Domain;
using Framework.Primitives;

namespace Framework.Permissions.Entities;

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
