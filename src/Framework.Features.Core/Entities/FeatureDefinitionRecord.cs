// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
using Framework.Domain;
using Framework.Primitives;

namespace Framework.Features.Entities;

public sealed class FeatureDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    public FeatureDefinitionRecord()
    {
        GroupName = null!;
        Name = null!;
        DisplayName = null!;
        ExtraProperties = [];
    }

    [SetsRequiredMembers]
    public FeatureDefinitionRecord(
        Guid id,
        string groupName,
        string name,
        string? parentName,
        string? displayName = null,
        string? description = null,
        string? defaultValue = null,
        bool isVisibleToClients = true,
        bool isAvailableToHost = true,
        string? providers = null
    )
    {
        Argument.IsNotNullOrWhiteSpace(groupName);
        Argument.IsLessThanOrEqualTo(groupName.Length, FeatureDefinitionRecordConstants.NameMaxLength);

        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, FeatureDefinitionRecordConstants.NameMaxLength);

        Argument.IsNotNullOrWhiteSpace(displayName);
        Argument.IsLessThanOrEqualTo(displayName.Length, FeatureDefinitionRecordConstants.DisplayNameMaxLength);

        if (parentName is not null)
        {
            Argument.IsLessThanOrEqualTo(parentName.Length, FeatureDefinitionRecordConstants.NameMaxLength);
        }

        if (description is not null)
        {
            Argument.IsLessThanOrEqualTo(description.Length, FeatureDefinitionRecordConstants.DescriptionMaxLength);
        }

        if (defaultValue is not null)
        {
            Argument.IsLessThanOrEqualTo(defaultValue.Length, FeatureDefinitionRecordConstants.DefaultValueMaxLength);
        }

        if (providers is not null)
        {
            Argument.IsLessThanOrEqualTo(providers.Length, FeatureDefinitionRecordConstants.ProvidersMaxLength);
        }

        Id = id;
        GroupName = groupName;
        Name = name;
        ParentName = parentName;
        DisplayName = displayName;
        Description = description;
        DefaultValue = defaultValue;
        IsVisibleToClients = isVisibleToClients;
        IsAvailableToHost = isAvailableToHost;
        Providers = providers;
        ExtraProperties = [];
    }

    public string GroupName { get; set; }

    public string Name { get; set; }

    public string DisplayName { get; set; }

    public string? ParentName { get; set; }

    public string? Description { get; set; }

    public string? DefaultValue { get; set; }

    public bool IsVisibleToClients { get; set; } = true;

    public bool IsAvailableToHost { get; set; } = true;

    /// <summary>Comma separated the list of provider names.</summary>
    public string? Providers { get; set; }

    public ExtraProperties ExtraProperties { get; private init; }

    public bool HasSameData(FeatureDefinitionRecord otherRecord)
    {
        if (!string.Equals(Name, otherRecord.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(GroupName, otherRecord.GroupName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(ParentName, otherRecord.ParentName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(DisplayName, otherRecord.DisplayName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(Description, otherRecord.Description, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(DefaultValue, otherRecord.DefaultValue, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsVisibleToClients != otherRecord.IsVisibleToClients)
        {
            return false;
        }

        if (IsAvailableToHost != otherRecord.IsAvailableToHost)
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

    public void Patch(FeatureDefinitionRecord otherRecord)
    {
        Name = otherRecord.Name;
        GroupName = otherRecord.GroupName;
        ParentName = otherRecord.ParentName;
        DisplayName = otherRecord.DisplayName;
        Description = otherRecord.Description;
        DefaultValue = otherRecord.DefaultValue;
        IsVisibleToClients = otherRecord.IsVisibleToClients;
        IsAvailableToHost = otherRecord.IsAvailableToHost;
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
