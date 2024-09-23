// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
public sealed class ExtraProperties : Dictionary<string, object?>
{
    public ExtraProperties() { }

    public ExtraProperties(IDictionary<string, object?> dictionary)
        : base(dictionary) { }
}
