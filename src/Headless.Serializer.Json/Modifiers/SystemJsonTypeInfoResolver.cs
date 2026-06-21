// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;

namespace Headless.Serializer.Modifiers;

/// <summary>
/// Holds a collection of type-info modifier delegates to be applied at JSON contract build time.
/// Intended to be registered in the DI container so that feature modules can contribute modifiers
/// without tightly coupling to a single resolver instance.
/// </summary>
public sealed class JsonSerializerModifiersOptions
{
    /// <summary>Gets a list of user-defined callbacks that can be used to modify the initial contract.</summary>
    public List<Action<JsonTypeInfo>> Modifiers { get; } = [];
}

/// <summary>
/// A <see cref="DefaultJsonTypeInfoResolver"/> that applies the modifiers collected in a
/// <see cref="JsonSerializerModifiersOptions"/> instance, enabling contract customization (property
/// inclusion/exclusion, non-public setter injection, etc.) to be registered through DI without
/// subclassing the serializer.
/// </summary>
[RequiresUnreferencedCode(
    "JSON serialization and deserialization might require types that cannot be statically analyzed."
)]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class SystemJsonTypeInfoResolver : DefaultJsonTypeInfoResolver
{
    /// <summary>
    /// Initializes a new instance and registers all modifiers from <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The options whose <see cref="JsonSerializerModifiersOptions.Modifiers"/> are applied.</param>
    public SystemJsonTypeInfoResolver(JsonSerializerModifiersOptions options)
    {
        foreach (var modifier in options.Modifiers)
        {
            Modifiers.Add(modifier);
        }
    }
}
