// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Checks;

namespace Framework.Features.Helpers;

[Serializable]
public class FeatureValueProviderInfo
{
    public string Name { get; }

    public string Key { get; }

    public FeatureValueProviderInfo(string name, string key)
    {
        Argument.IsNotNull(name);

        Name = name;
        Key = key;
    }
}
