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
