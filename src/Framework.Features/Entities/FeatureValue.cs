// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;
using Framework.Kernel.Domains;

namespace Framework.Features.FeatureManagement;

public sealed class FeatureValue : AggregateRoot<Guid>
{
    public string Name { get; private set; }

    public string Value { get; internal set; }

    public string ProviderName { get; private set; }

    public string? ProviderKey { get; private set; }

    public FeatureValue(Guid id, string name, string value, string providerName, string? providerKey)
    {
        Argument.IsNotNull(name);

        Id = id;
        Name = Argument.IsNotNullOrWhiteSpace(name);
        Value = Argument.IsNotNullOrWhiteSpace(value);
        ProviderName = Argument.IsNotNullOrWhiteSpace(providerName);
        ProviderKey = providerKey;
    }
}
