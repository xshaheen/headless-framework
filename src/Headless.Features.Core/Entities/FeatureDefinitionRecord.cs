// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.Primitives;

namespace Headless.Features.Entities;

/// <summary>Persistence record for a single feature definition stored in the dynamic feature definition store.</summary>
public sealed class FeatureDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    /// <summary>Parameterless constructor required by EF Core and other ORM/serializer conventions.</summary>
    public FeatureDefinitionRecord()
    {
        GroupName = null!;
        Name = null!;
        DisplayName = null!;
        ExtraProperties = [];
    }

    /// <summary>Initializes a new <see cref="FeatureDefinitionRecord"/> with all required and optional fields.</summary>
    /// <param name="id">The unique identifier for this record.</param>
    /// <param name="groupName">The name of the feature group that owns this feature.</param>
    /// <param name="name">The unique name of the feature.</param>
    /// <param name="parentName">The name of the parent feature, or <see langword="null"/> for top-level features.</param>
    /// <param name="displayName">The human-readable display name of the feature.</param>
    /// <param name="description">An optional description of the feature.</param>
    /// <param name="defaultValue">The default value of the feature when no provider supplies one.</param>
    /// <param name="isVisibleToClients">Whether this feature is exposed to client-side consumers.</param>
    /// <param name="isAvailableToHost">Whether this feature is available to the host application.</param>
    /// <param name="providers">Comma-separated list of provider names allowed to manage this feature's value, or <see langword="null"/> for all providers.</param>
    /// <exception cref="ArgumentNullException"><paramref name="groupName"/>, <paramref name="name"/>, or <paramref name="displayName"/> is <see langword="null"/> or white space.</exception>
    /// <exception cref="ArgumentException"><paramref name="groupName"/>, <paramref name="name"/>, or <paramref name="displayName"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="groupName"/> or <paramref name="name"/> exceeds <see cref="FeatureDefinitionRecordConstants.NameMaxLength"/> characters,
    /// <paramref name="displayName"/> exceeds <see cref="FeatureDefinitionRecordConstants.DisplayNameMaxLength"/> characters,
    /// <paramref name="parentName"/> exceeds <see cref="FeatureDefinitionRecordConstants.NameMaxLength"/> characters,
    /// <paramref name="description"/> exceeds <see cref="FeatureDefinitionRecordConstants.DescriptionMaxLength"/> characters,
    /// <paramref name="defaultValue"/> exceeds <see cref="FeatureDefinitionRecordConstants.DefaultValueMaxLength"/> characters,
    /// or <paramref name="providers"/> exceeds <see cref="FeatureDefinitionRecordConstants.ProvidersMaxLength"/> characters.
    /// </exception>
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

    /// <summary>The name of the feature group that contains this feature.</summary>
    public string GroupName { get; set; }

    /// <summary>The unique name of the feature.</summary>
    public string Name { get; set; }

    /// <summary>The human-readable display name of the feature.</summary>
    public string DisplayName { get; set; }

    /// <summary>The name of the parent feature, or <see langword="null"/> for top-level features.</summary>
    public string? ParentName { get; set; }

    /// <summary>An optional description of the feature.</summary>
    public string? Description { get; set; }

    /// <summary>The default value used when no provider supplies a value for this feature.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Whether this feature is exposed to client-side consumers. Default: <see langword="true"/>.</summary>
    public bool IsVisibleToClients { get; set; } = true;

    /// <summary>Whether this feature is available to the host application. Default: <see langword="true"/>.</summary>
    public bool IsAvailableToHost { get; set; } = true;

    /// <summary>Comma-separated list of provider names that are allowed to manage this feature's value.</summary>
    public string? Providers { get; set; }

    /// <summary>Arbitrary extra properties stored alongside the record.</summary>
    public ExtraProperties ExtraProperties { get; private init; }

    /// <summary>Returns <see langword="true"/> when all persisted fields of this record equal those of <paramref name="otherRecord"/>.</summary>
    /// <param name="otherRecord">The record to compare against.</param>
    /// <returns><see langword="true"/> if all data fields are equal; otherwise <see langword="false"/>.</returns>
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

    /// <summary>Copies all mutable fields from <paramref name="otherRecord"/> into this instance, replacing current values.</summary>
    /// <param name="otherRecord">The source record whose values will be applied.</param>
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
