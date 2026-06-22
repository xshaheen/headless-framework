// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.Primitives;

namespace Headless.Settings.Entities;

/// <summary>Persistence entity that represents a single setting definition stored in an external data source.</summary>
public sealed class SettingDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    /// <summary>Parameterless constructor for ORM/serializer use only.</summary>
    [UsedImplicitly]
    private SettingDefinitionRecord()
    {
        Name = null!;
        DisplayName = null!;
        Providers = null!;
        ExtraProperties = [];
    }

    /// <summary>Initializes a new <see cref="SettingDefinitionRecord"/> with all required fields validated.</summary>
    /// <param name="id">Unique identifier for the record.</param>
    /// <param name="name">Unique name of the setting. Must not be null, empty, or whitespace and must not exceed <see cref="SettingDefinitionRecordConstants.NameMaxLength"/> characters.</param>
    /// <param name="displayName">Human-readable display name. Must not be null, empty, or whitespace and must not exceed <see cref="SettingDefinitionRecordConstants.DisplayNameMaxLength"/> characters.</param>
    /// <param name="description">Optional description. When non-<see langword="null"/>, must not exceed <see cref="SettingDefinitionRecordConstants.DescriptionMaxLength"/> characters.</param>
    /// <param name="defaultValue">Optional default value. When non-<see langword="null"/>, must not exceed <see cref="SettingDefinitionRecordConstants.DefaultValueMaxLength"/> characters.</param>
    /// <param name="providers">Optional comma-separated list of provider names. When non-<see langword="null"/>, must not exceed <see cref="SettingDefinitionRecordConstants.ProvidersMaxLength"/> characters.</param>
    /// <param name="isVisibleToClients">Whether clients may read this setting and its value.</param>
    /// <param name="isInherited">Whether the value falls back to the next provider when not set for the requested provider.</param>
    /// <param name="isEncrypted">Whether the value is stored encrypted in the data source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="displayName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="displayName"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="name"/> exceeds <see cref="SettingDefinitionRecordConstants.NameMaxLength"/>,
    /// <paramref name="displayName"/> exceeds <see cref="SettingDefinitionRecordConstants.DisplayNameMaxLength"/>,
    /// <paramref name="description"/> exceeds <see cref="SettingDefinitionRecordConstants.DescriptionMaxLength"/>,
    /// <paramref name="defaultValue"/> exceeds <see cref="SettingDefinitionRecordConstants.DefaultValueMaxLength"/>,
    /// or <paramref name="providers"/> exceeds <see cref="SettingDefinitionRecordConstants.ProvidersMaxLength"/>.
    /// </exception>
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

    /// <summary>Returns <see langword="true"/> if <paramref name="other"/> carries identical data to this record (excluding <see cref="AggregateRoot{TKey}.Id"/>).</summary>
    /// <param name="other">The record to compare against.</param>
    /// <returns><see langword="true"/> when all data fields and extra properties are equal; otherwise <see langword="false"/>.</returns>
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

    /// <summary>Applies all data fields from <paramref name="other"/> onto this record in place.</summary>
    /// <param name="other">The source record whose values will overwrite this record's fields.</param>
    public void Patch(SettingDefinitionRecord other)
    {
        Name = other.Name;
        DisplayName = other.DisplayName;
        Description = other.Description;
        DefaultValue = other.DefaultValue;
        IsVisibleToClients = other.IsVisibleToClients;
        Providers = other.Providers;
        IsInherited = other.IsInherited;
        IsEncrypted = other.IsEncrypted;

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
