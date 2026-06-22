// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;

namespace Headless.Features.Entities;

/// <summary>Persistence record for a stored feature value, scoped to a specific provider and optional provider key.</summary>
public sealed class FeatureValueRecord : AggregateRoot<Guid>
{
    /// <summary>The name of the feature whose value is stored.</summary>
    public string Name { get; private init; }

    /// <summary>The stored value for the feature.</summary>
    public string Value { get; internal set; }

    /// <summary>The name of the value provider that owns this record (e.g. <c>Tenant</c>, <c>User</c>).</summary>
    public string ProviderName { get; private init; }

    /// <summary>The provider-specific key that scopes this value (e.g. a tenant ID), or <see langword="null"/> for provider-global values.</summary>
    public string? ProviderKey { get; private init; }

    /// <summary>Initializes a new <see cref="FeatureValueRecord"/>.</summary>
    /// <param name="id">The unique identifier for this record.</param>
    /// <param name="name">The name of the feature.</param>
    /// <param name="value">The stored feature value.</param>
    /// <param name="providerName">The name of the value provider that owns this record.</param>
    /// <param name="providerKey">The provider-specific scoping key, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="value"/>, or <paramref name="providerName"/> is <see langword="null"/> or white space.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/>, <paramref name="value"/>, or <paramref name="providerName"/> is empty or white space.</exception>
    [SetsRequiredMembers]
    public FeatureValueRecord(Guid id, string name, string value, string providerName, string? providerKey)
    {
        Id = id;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        Value = Argument.IsNotNullOrWhiteSpace(value);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        ProviderKey = providerKey;
    }
}
