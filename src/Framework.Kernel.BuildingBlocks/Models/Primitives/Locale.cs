// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
public sealed class Locale : Dictionary<string, Dictionary<string, string>>
{
    public Locale() { }

    public Locale(IDictionary<string, Dictionary<string, string>> d)
        : base(d) { }
}
