// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer;

/// <summary>
/// Supplies the <see cref="JsonSerializerOptions"/> used by <see cref="SystemJsonSerializer"/> for
/// serialization and deserialization, allowing callers to customize behavior without subclassing the serializer.
/// </summary>
public interface IJsonOptionsProvider
{
    /// <summary>Returns the <see cref="JsonSerializerOptions"/> to use when serializing objects to JSON.</summary>
    JsonSerializerOptions GetSerializeOptions();

    /// <summary>Returns the <see cref="JsonSerializerOptions"/> to use when deserializing JSON into objects.</summary>
    JsonSerializerOptions GetDeserializeOptions();
}

/// <summary>
/// Default <see cref="IJsonOptionsProvider"/> that returns <see cref="JsonConstants.DefaultWebJsonOptions"/>
/// for both serialization and deserialization.
/// </summary>
public sealed class DefaultJsonOptionsProvider : IJsonOptionsProvider
{
    /// <inheritdoc/>
    public JsonSerializerOptions GetSerializeOptions()
    {
        return JsonConstants.DefaultWebJsonOptions;
    }

    /// <inheritdoc/>
    public JsonSerializerOptions GetDeserializeOptions()
    {
        return JsonConstants.DefaultWebJsonOptions;
    }
}
