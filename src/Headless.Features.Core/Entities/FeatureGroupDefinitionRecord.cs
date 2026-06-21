// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.Primitives;

namespace Headless.Features.Entities;

/// <summary>Persistence record for a single feature group definition stored in the dynamic feature definition store.</summary>
public sealed class FeatureGroupDefinitionRecord : AggregateRoot<Guid>, IHasExtraProperties
{
    /// <summary>The unique name of the feature group.</summary>
    public string Name { get; set; }

    /// <summary>The human-readable display name of the feature group.</summary>
    public string DisplayName { get; set; }

    /// <summary>Arbitrary extra properties stored alongside the record.</summary>
    public ExtraProperties ExtraProperties { get; init; }

    /// <summary>Initializes a new <see cref="FeatureGroupDefinitionRecord"/>.</summary>
    /// <param name="id">The unique identifier for this record.</param>
    /// <param name="name">The unique name of the feature group.</param>
    /// <param name="displayName">The human-readable display name of the feature group.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="displayName"/> is <see langword="null"/> or white space.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="displayName"/> is empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="name"/> exceeds <see cref="FeatureGroupDefinitionRecordConstants.NameMaxLength"/> characters,
    /// or <paramref name="displayName"/> exceeds <see cref="FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength"/> characters.
    /// </exception>
    [SetsRequiredMembers]
    public FeatureGroupDefinitionRecord(Guid id, string name, string displayName)
    {
        Argument.IsNotNullOrWhiteSpace(name);
        Argument.IsLessThanOrEqualTo(name.Length, FeatureGroupDefinitionRecordConstants.NameMaxLength);
        Argument.IsNotNullOrWhiteSpace(displayName);
        Argument.IsLessThanOrEqualTo(displayName.Length, FeatureGroupDefinitionRecordConstants.DisplayNameMaxLength);

        Id = id;
        Name = name;
        DisplayName = displayName;
        ExtraProperties = [];
    }

    /// <summary>Returns <see langword="true"/> when all persisted fields of this record equal those of <paramref name="otherRecord"/>.</summary>
    /// <param name="otherRecord">The record to compare against.</param>
    /// <returns><see langword="true"/> if all data fields are equal; otherwise <see langword="false"/>.</returns>
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

    /// <summary>Copies all mutable fields from <paramref name="otherRecord"/> into this instance, replacing current values.</summary>
    /// <param name="otherRecord">The source record whose values will be applied.</param>
    public void Patch(FeatureGroupDefinitionRecord otherRecord)
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
