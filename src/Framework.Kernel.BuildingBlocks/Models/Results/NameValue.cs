// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

/// <summary>Can be used to store Name/Value (or Key/Value) pairs.</summary>
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

/// <summary>Can be used to store Name/Value (or Key/Value) pairs.</summary>
public sealed class NameValue : NameValue<string>;
