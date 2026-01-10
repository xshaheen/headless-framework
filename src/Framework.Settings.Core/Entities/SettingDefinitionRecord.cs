// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
using Framework.Domain;
using Framework.Primitives;

namespace Framework.Settings.Entities;

public sealed class SettingDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    [UsedImplicitly]
    private SettingDefinitionRecord()
    {
        Name = null!;
        DisplayName = null!;
        Providers = null!;
        ExtraProperties = [];
    }

    [SetsRequiredMembers]
    public SettingDefinitionRecord(
        Guid id,
        string name,
        string displayName,
        string? description,
        string? defaultValue,
        string? providers,
        bool isVisibleToClients,
        bool isInherited,
        bool isEncrypted
    )
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, SettingDefinitionRecordConstants.NameMaxLength);

        Argument.IsNotNullOrWhiteSpace(displayName);
        Argument.IsLessThanOrEqualTo(displayName.Length, SettingDefinitionRecordConstants.DisplayNameMaxLength);

        if (description is not null)
        {
            Argument.IsLessThanOrEqualTo(description.Length, SettingDefinitionRecordConstants.DescriptionMaxLength);
        }

        if (defaultValue is not null)
        {
            Argument.IsLessThanOrEqualTo(defaultValue.Length, SettingDefinitionRecordConstants.DefaultValueMaxLength);
        }

        if (providers is not null)
        {
            Argument.IsLessThanOrEqualTo(providers.Length, SettingDefinitionRecordConstants.ProvidersMaxLength);
        }

        Id = id;
        Name = name;
        DisplayName = displayName;
        Description = description;
        DefaultValue = defaultValue;
        IsVisibleToClients = isVisibleToClients;
        Providers = providers;
        IsEncrypted = isEncrypted;
        IsInherited = isInherited;
        ExtraProperties = [];
    }

    /// <summary>Gets or sets the name of the setting.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the display name of the setting.</summary>
    public string DisplayName { get; set; }

    /// <summary>Gets or sets the description of the setting.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the default value of the setting.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Can clients see this setting and it's value.
    /// It may be dangerous for some settings to be visible to clients (such as an email server password).
    /// Default: false.
    /// </summary>
    public bool IsVisibleToClients { get; set; }

    /// <summary>
    /// Is the setting value is inherited from other providers or not. <see langword="true"/>
    /// means fallbacks to the next provider if the setting value was not set for the requested provider
    /// </summary>
    public bool IsInherited { get; set; }

    /// <summary>Is this setting stored as encrypted in the data source. Default: False.</summary>
    public bool IsEncrypted { get; set; }

    /// <summary>Comma separated the list of provider names.</summary>
    public string? Providers { get; set; }

    /// <summary>Gets the extra properties associated with this setting.</summary>
    public ExtraProperties ExtraProperties { get; private init; }

    public bool HasSameData(SettingDefinitionRecord other)
    {
        if (!string.Equals(Name, other.Name, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(Description, other.Description, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(DefaultValue, other.DefaultValue, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsVisibleToClients != other.IsVisibleToClients)
        {
            return false;
        }

        if (!string.Equals(Providers, other.Providers, StringComparison.Ordinal))
        {
            return false;
        }

        if (IsInherited != other.IsInherited)
        {
            return false;
        }

        if (IsEncrypted != other.IsEncrypted)
        {
            return false;
        }

        if (!this.HasSameExtraProperties(other))
        {
            return false;
        }

        return true;
    }

    public void Patch(SettingDefinitionRecord other)
    {
        if (!string.Equals(Name, other.Name, StringComparison.Ordinal))
        {
            Name = other.Name;
        }

        if (!string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal))
        {
            DisplayName = other.DisplayName;
        }

        if (!string.Equals(Description, other.Description, StringComparison.Ordinal))
        {
            Description = other.Description;
        }

        if (!string.Equals(DefaultValue, other.DefaultValue, StringComparison.Ordinal))
        {
            DefaultValue = other.DefaultValue;
        }

        if (IsVisibleToClients != other.IsVisibleToClients)
        {
            IsVisibleToClients = other.IsVisibleToClients;
        }

        if (!string.Equals(Providers, other.Providers, StringComparison.Ordinal))
        {
            Providers = other.Providers;
        }

        if (IsInherited != other.IsInherited)
        {
            IsInherited = other.IsInherited;
        }

        if (IsEncrypted != other.IsEncrypted)
        {
            IsEncrypted = other.IsEncrypted;
        }

        if (!this.HasSameExtraProperties(other))
        {
            ExtraProperties.Clear();

            foreach (var property in other.ExtraProperties)
            {
                ExtraProperties.Add(property.Key, property.Value);
            }
        }
    }
}
