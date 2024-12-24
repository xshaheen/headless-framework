// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Primitives;

/// <summary>Can be used to store Name/Value (or Key/Value) pairs.</summary>
[PublicAPI]
public class NameValue<T>
{
    public NameValue() { }

    [SetsRequiredMembers]
    public NameValue(string name, T value)
    {
        Name = name;
        Value = value;
    }

    public required string Name { get; set; }

    public required T Value { get; set; }
}
