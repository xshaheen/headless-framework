// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer;

/// <summary>
/// Supplies the <see cref="JsonSerializerOptions"/> used by <see cref="SystemJsonSerializer"/> for
/// serialization and deserialization, allowing callers to customize behavior without subclassing the serializer.
/// </summary>
[PublicAPI]
public interface IJsonOptionsProvider
{
    /// <summary>Returns the <see cref="JsonSerializerOptions"/> to use when serializing objects to JSON.</summary>
    JsonSerializerOptions GetSerializeOptions();

    /// <summary>Returns the <see cref="JsonSerializerOptions"/> to use when deserializing JSON into objects.</summary>
    JsonSerializerOptions GetDeserializeOptions();
}

/// <summary>
/// Default <see cref="IJsonOptionsProvider"/>: returns <see cref="JsonConstants.DefaultStorageJsonOptions"/> for both
/// serialization and deserialization, so a value the framework stores for the app (a cache entry, blob metadata,
/// coordination metadata) reads back with the keys and values it was written with, on every node.
/// </summary>
/// <remarks>
/// HTTP JSON uses the web preset instead. That preset camelCases property names and reads leniently, which suits a
/// client but not a round trip. A host that registers its own provider changes how its stored values are written; if
/// that provider returns its HTTP options, stored property names follow them too.
/// </remarks>
[PublicAPI]
public sealed class DefaultJsonOptionsProvider : IJsonOptionsProvider
{
    /// <inheritdoc/>
    public JsonSerializerOptions GetSerializeOptions()
    {
        return JsonConstants.DefaultStorageJsonOptions;
    }

    /// <inheritdoc/>
    public JsonSerializerOptions GetDeserializeOptions()
    {
        return JsonConstants.DefaultStorageJsonOptions;
    }
}
