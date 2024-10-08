using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.Checks;
using Framework.Kernel.Domains;
using Framework.Kernel.Primitives;

namespace Framework.Settings.Entities;

public class SettingDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    public SettingDefinitionRecord()
    {
        Name = default!;
        DisplayName = default!;
        Providers = default!;
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
        Argument.IsLessThanOrEqualTo(name.Length, SettingDefinitionRecordConstants.MaxNameLength);
        Argument.IsNotNullOrWhiteSpace(displayName);
        Argument.IsLessThanOrEqualTo(displayName.Length, SettingDefinitionRecordConstants.MaxDisplayNameLength);

        if (description is not null)
        {
            Argument.IsLessThanOrEqualTo(description.Length, SettingDefinitionRecordConstants.MaxDescriptionLength);
        }

        if (defaultValue is not null)
        {
            Argument.IsLessThanOrEqualTo(defaultValue.Length, SettingDefinitionRecordConstants.MaxDefaultValueLength);
        }

        if (providers is not null)
        {
            Argument.IsLessThanOrEqualTo(providers.Length, SettingDefinitionRecordConstants.MaxProvidersLength);
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

    public string Name { get; set; }

    public string DisplayName { get; set; }

    public string? Description { get; set; }

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

    /// <summary>Comma separated list of provider names.</summary>
    public string? Providers { get; set; }

    public ExtraProperties ExtraProperties { get; protected set; }

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
