// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace Framework.Serializer.Modifiers;

public sealed class JsonSerializerModifiersOptions
{
    /// <summary>Gets a list of user-defined callbacks that can be used to modify the initial contract.</summary>
    public List<Action<JsonTypeInfo>> Modifiers { get; } = [];
}

[RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed.")]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class SystemJsonTypeInfoResolver : DefaultJsonTypeInfoResolver
{
    public SystemJsonTypeInfoResolver(JsonSerializerModifiersOptions options)
    {
        foreach (var modifier in options.Modifiers)
        {
            Modifiers.Add(modifier);
        }
    }
}
