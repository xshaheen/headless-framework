// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

[PublicAPI]
public sealed class ExtraProperties : Dictionary<string, object?>
{
    public ExtraProperties()
        : base(StringComparer.InvariantCulture) { }

    public ExtraProperties(IDictionary<string, object?> dictionary)
        : base(dictionary, StringComparer.InvariantCulture) { }
}
