#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

/// <summary>Can be used to store Name/Value (or Key/Value) pairs.</summary>
public class NameValue<T>
{
    public required string Name { get; set; }

    public required T Value { get; set; }
}

/// <summary>Can be used to store Name/Value (or Key/Value) pairs.</summary>
public sealed class NameValue : NameValue<string>;
