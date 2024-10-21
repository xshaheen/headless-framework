// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;

namespace Framework.Features.FeatureManagement;

[Serializable]
public class FeatureNameValue : NameValue<string>
{
    public FeatureNameValue() { }

    public FeatureNameValue(string name, string value)
    {
        Name = name;
        Value = value;
    }
}
