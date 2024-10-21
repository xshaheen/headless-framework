// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;
using Volo.Abp.FeatureManagement;

namespace Framework.Features.FeatureManagement;

public class FeatureGroupDefinitionRecord : BasicAggregateRoot<Guid>, IHasExtraProperties
{
    public string Name { get; set; }

    public string DisplayName { get; set; }

    public ExtraProperties ExtraProperties { get; protected set; }

    public FeatureGroupDefinitionRecord()
    {
        ExtraProperties = new ExtraPropertyDictionary();
        this.SetDefaultsForExtraProperties();
    }

    public FeatureGroupDefinitionRecord(Guid id, string name, string displayName)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), FeatureGroupDefinitionRecordConsts.MaxNameLength);
        DisplayName = Check.NotNullOrWhiteSpace(
            displayName,
            nameof(displayName),
            FeatureGroupDefinitionRecordConsts.MaxDisplayNameLength
        );
        ;

        ExtraProperties = new ExtraProperties();
        this.SetDefaultsForExtraProperties();
    }

    public bool HasSameData(FeatureGroupDefinitionRecord otherRecord)
    {
        if (Name != otherRecord.Name)
        {
            return false;
        }

        if (DisplayName != otherRecord.DisplayName)
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
        if (Name != otherRecord.Name)
        {
            Name = otherRecord.Name;
        }

        if (DisplayName != otherRecord.DisplayName)
        {
            DisplayName = otherRecord.DisplayName;
        }

        if (!this.HasSameExtraProperties(otherRecord))
        {
            this.ExtraProperties.Clear();

            foreach (var property in otherRecord.ExtraProperties)
            {
                this.ExtraProperties.Add(property.Key, property.Value);
            }
        }
    }
}
