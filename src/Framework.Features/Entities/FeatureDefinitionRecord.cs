// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Features.FeatureManagement;
using Framework.Kernel.Checks;
using Framework.Kernel.Domains;
using Framework.Kernel.Primitives;

namespace Framework.Features.Entities;

public sealed class FeatureDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    public string GroupName { get; set; }

    public string Name { get; set; }

    public string DisplayName { get; set; }

    public string? ParentName { get; set; }

    public string? Description { get; set; }

    public string DefaultValue { get; set; }

    public bool IsVisibleToClients { get; set; } = true;

    public bool IsAvailableToHost { get; set; } = true;

    /// <summary>Comma separated list of provider names.</summary>
    public string AllowedProviders { get; set; }

    /// <summary>Serialized string to store info about the ValueType.</summary>
    public string ValueType { get; set; } // ToggleStringValueType

    public ExtraProperties ExtraProperties { get; protected set; }

    public FeatureDefinitionRecord()
    {
        IsVisibleToClients = true;
        IsAvailableToHost = true;
        ExtraProperties = [];
    }

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
        string? allowedProviders = null,
        string? valueType = null
    )
    {
        Argument.IsNotNullOrWhiteSpace(groupName);
        Argument.IsLessThanOrEqualTo(groupName.Length, FeatureDefinitionRecordConsts.MaxNameLength);

        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, FeatureDefinitionRecordConsts.MaxNameLength);

        Id = id;
        GroupName = groupName;
        Name = name;
        ParentName = Argument.IsLength(parentName, FeatureDefinitionRecordConsts.MaxNameLength);
        DisplayName = Argument.IsNotNullOrWhiteSpace(displayName, FeatureDefinitionRecordConsts.MaxDisplayNameLength);

        Description = Argument.IsLength(description, FeatureDefinitionRecordConsts.MaxDescriptionLength);
        DefaultValue = Argument.IsLength(defaultValue, FeatureDefinitionRecordConsts.MaxDefaultValueLength);

        IsVisibleToClients = isVisibleToClients;
        IsAvailableToHost = isAvailableToHost;

        AllowedProviders = Argument.IsLength(allowedProviders, FeatureDefinitionRecordConsts.MaxAllowedProvidersLength);
        ValueType = Argument.IsNotNullOrWhiteSpace(valueType, FeatureDefinitionRecordConsts.MaxValueTypeLength);

        ExtraProperties = [];
    }

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
        if (!string.Equals(AllowedProviders, otherRecord.AllowedProviders, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(ValueType, otherRecord.ValueType, StringComparison.Ordinal))
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
        if (!string.Equals(Name, otherRecord.Name, StringComparison.Ordinal))
        {
            Name = otherRecord.Name;
        }

        if (!string.Equals(GroupName, otherRecord.GroupName, StringComparison.Ordinal))
        {
            GroupName = otherRecord.GroupName;
        }

        if (!string.Equals(ParentName, otherRecord.ParentName, StringComparison.Ordinal))
        {
            ParentName = otherRecord.ParentName;
        }

        if (!string.Equals(DisplayName, otherRecord.DisplayName, StringComparison.Ordinal))
        {
            DisplayName = otherRecord.DisplayName;
        }

        if (!string.Equals(Description, otherRecord.Description, StringComparison.Ordinal))
        {
            Description = otherRecord.Description;
        }

        if (!string.Equals(DefaultValue, otherRecord.DefaultValue, StringComparison.Ordinal))
        {
            DefaultValue = otherRecord.DefaultValue;
        }

        if (IsVisibleToClients != otherRecord.IsVisibleToClients)
        {
            IsVisibleToClients = otherRecord.IsVisibleToClients;
        }

        if (IsAvailableToHost != otherRecord.IsAvailableToHost)
        {
            IsAvailableToHost = otherRecord.IsAvailableToHost;
        }

        if (!string.Equals(AllowedProviders, otherRecord.AllowedProviders, StringComparison.Ordinal))
        {
            AllowedProviders = otherRecord.AllowedProviders;
        }

        if (!string.Equals(ValueType, otherRecord.ValueType, StringComparison.Ordinal))
        {
            ValueType = otherRecord.ValueType;
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
