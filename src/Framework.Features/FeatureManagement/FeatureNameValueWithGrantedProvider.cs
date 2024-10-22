// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using Framework.Features.Values;
using Framework.Kernel.Primitives;

namespace Framework.Features.FeatureManagement;

[Serializable]
[method: SetsRequiredMembers]
public sealed class FeatureNameValueWithGrantedProvider(string name, string? value) : NameValue<string?>(name, value)
{
    public FeatureValueProviderInfo? Provider { get; set; }
}
