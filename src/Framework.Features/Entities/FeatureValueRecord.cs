// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Framework.Kernel.Domains;

namespace Framework.Features.Entities;

public sealed class FeatureValueRecord : AggregateRoot<Guid>
{
    public string Name { get; private set; }

    public string Value { get; internal set; }

    public string ProviderName { get; private set; }

    public string? ProviderKey { get; private set; }

    public FeatureValueRecord(Guid id, string name, string value, string providerName, string? providerKey)
    {
        Id = id;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        Value = Argument.IsNotNullOrWhiteSpace(value);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        ProviderKey = providerKey;
    }
}
