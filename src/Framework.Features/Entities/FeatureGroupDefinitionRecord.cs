// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.FeatureManagement;
using Framework.Kernel.Checks;
using Framework.Kernel.Domains;
using Framework.Kernel.Primitives;

namespace Framework.Features.Entities;

public sealed class FeatureGroupDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    public string Name { get; set; }

    public string DisplayName { get; set; }

    public ExtraProperties ExtraProperties { get; init; }

    public FeatureGroupDefinitionRecord(Guid id, string name, string displayName)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, FeatureGroupDefinitionRecordConsts.MaxNameLength);
        Argument.IsNotNullOrWhiteSpace(displayName);
        Argument.IsLessThanOrEqualTo(displayName.Length, FeatureGroupDefinitionRecordConsts.MaxDisplayNameLength);

        Id = id;
        Name = name;
        DisplayName = displayName;
        ExtraProperties = [];
    }

    public bool HasSameData(FeatureGroupDefinitionRecord otherRecord)
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

    public void Patch(FeatureGroupDefinitionRecord otherRecord)
    {
        if (!string.Equals(Name, otherRecord.Name, StringComparison.Ordinal))
        {
            Name = otherRecord.Name;
        }

        if (!string.Equals(DisplayName, otherRecord.DisplayName, StringComparison.Ordinal))
        {
            DisplayName = otherRecord.DisplayName;
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
