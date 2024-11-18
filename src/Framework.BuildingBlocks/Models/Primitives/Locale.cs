// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

[PublicAPI]
public sealed class Locale : Dictionary<string, Dictionary<string, string>>
{
    public Locale() { }

    public Locale(IDictionary<string, Dictionary<string, string>> d)
        : base(d) { }
}
