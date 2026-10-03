// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Serializer;

/// <summary>
/// Default <see cref="IJsonOptionsProvider"/> that returns <see cref="JsonConstants.DefaultWebJsonOptions"/>
/// for both serialization and deserialization.
/// </summary>
[PublicAPI]
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
