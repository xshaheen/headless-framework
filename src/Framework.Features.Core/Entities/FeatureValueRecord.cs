// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Checks;
using Framework.Domains;

namespace Framework.Features.Entities;

public sealed class FeatureValueRecord : AggregateRoot<Guid>
{
    public string Name { get; private init; }

    public string Value { get; internal set; }

    public string ProviderName { get; private init; }

    public string? ProviderKey { get; private init; }

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
