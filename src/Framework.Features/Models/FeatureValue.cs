// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Features.Models;

public record FeatureValue(string Name, string? Value);

public sealed record FeatureNameValueWithGrantedProvider(
    string Name,
    string? Value,
    FeatureValueProvider? Provider = null
) : FeatureValue(Name, Value);

public sealed record FeatureValueProvider
{
    public FeatureValueProvider(string name, string? key)
    {
        Argument.IsNotNull(name);

        Name = name;
        Key = key;
    }

    public string Name { get; }

    public string? Key { get; }
}
