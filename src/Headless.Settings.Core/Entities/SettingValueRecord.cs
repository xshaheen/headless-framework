// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;

namespace Headless.Settings.Entities;

/// <summary>Persistence entity that stores a setting value for a specific provider and optional provider key.</summary>
public sealed class SettingValueRecord : Entity<Guid>, IAggregateRoot<Guid>, ICreateAudit, IUpdateAudit
{
    /// <summary>Parameterless constructor for ORM/serializer use only.</summary>
    [SetsRequiredMembers]
    [UsedImplicitly]
    private SettingValueRecord()
    {
        Name = null!;
        Value = null!;
        ProviderName = null!;
    }

    /// <summary>Initializes a new <see cref="SettingValueRecord"/>.</summary>
    /// <param name="id">Unique identifier for the record.</param>
    /// <param name="name">Setting name. Must not be <see langword="null"/>.</param>
    /// <param name="value">Setting value. Must not be <see langword="null"/>.</param>
    /// <param name="providerName">Name of the value provider. Must not be <see langword="null"/>.</param>
    /// <param name="providerKey">Optional key scoping the value within the provider (e.g. tenant id).</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/>, <paramref name="value"/>, or <paramref name="providerName"/> is <see langword="null"/>.
    /// </exception>
    [SetsRequiredMembers]
    public SettingValueRecord(Guid id, string name, string value, string providerName, string? providerKey = null)
    {
        Argument.IsNotNull(name);
        Argument.IsNotNull(value);
        Argument.IsNotNull(providerName);

        Id = id;
        Name = name;
        Value = value;
        ProviderName = providerName;
        ProviderKey = providerKey;
    }

    /// <summary>
    /// Creates a <see cref="SettingValueRecord"/> from raw storage data, including audit timestamps.
    /// </summary>
    /// <param name="id">Unique identifier for the record.</param>
    /// <param name="name">Setting name.</param>
    /// <param name="value">Setting value.</param>
    /// <param name="providerName">Name of the value provider.</param>
    /// <param name="providerKey">Optional key scoping the value within the provider.</param>
    /// <param name="dateCreated">UTC timestamp when the record was first created.</param>
    /// <param name="dateUpdated">UTC timestamp of the last update, or <see langword="null"/> if never updated.</param>
    /// <returns>A fully-hydrated <see cref="SettingValueRecord"/> with audit fields populated.</returns>
    public static SettingValueRecord FromStorage(
        Guid id,
        string name,
        string value,
        string providerName,
        string? providerKey,
        DateTimeOffset dateCreated,
        DateTimeOffset? dateUpdated
    )
    {
        return new SettingValueRecord(id, name, value, providerName, providerKey)
        {
            DateCreated = dateCreated,
            DateUpdated = dateUpdated,
        };
    }

    /// <summary>Gets the unique name of the setting.</summary>
    public string Name { get; private init; }

    /// <summary>Gets or sets the current value of the setting.</summary>
    public string Value { get; internal set; }

    /// <summary>Gets the name of the value provider that owns this record.</summary>
    public string ProviderName { get; private init; }

    /// <summary>Gets the optional key that scopes this value within the provider (e.g. a tenant identifier).</summary>
    public string? ProviderKey { get; private init; }

    /// <summary>Gets the UTC timestamp when this record was first created.</summary>
    public DateTimeOffset DateCreated { get; private set; }

    /// <summary>Gets the UTC timestamp of the last update, or <see langword="null"/> if the record has never been updated.</summary>
    public DateTimeOffset? DateUpdated { get; private set; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{base.ToString()}, Name = {Name}, Value = {Value}, ProviderName = {ProviderName}, ProviderKey = {ProviderKey}";
    }
}
