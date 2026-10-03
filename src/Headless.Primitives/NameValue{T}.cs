// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>Can be used to store Name/Value (or Key/Value) pairs.</summary>
/// <typeparam name="T">The type of the stored value.</typeparam>
[PublicAPI]
public class NameValue<T>
{
    /// <summary>Initializes a new pair with both members unset; <see cref="Name"/> and <see cref="Value"/> must be set via initializers.</summary>
    public NameValue() { }

    /// <summary>Initializes a new pair with the given name and value.</summary>
    /// <param name="name">The name (or key) of the pair.</param>
    /// <param name="value">The value of the pair.</param>
    [SetsRequiredMembers]
    public NameValue(string name, T value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>The name (or key) of the pair.</summary>
    public required string Name { get; set; }

    /// <summary>The value of the pair.</summary>
    public required T Value { get; set; }
}
