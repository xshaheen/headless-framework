// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

/// <summary>Can be used to store Name/Value (or Key/Value) pairs.</summary>
public class NameValue<T>
{
    public required string Name { get; set; }

    public required T Value { get; set; }
}

/// <summary>Can be used to store Name/Value (or Key/Value) pairs.</summary>
public sealed class NameValue : NameValue<string>;
