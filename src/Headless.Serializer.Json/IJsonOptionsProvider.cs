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
/// Default <see cref="IJsonOptionsProvider"/>: the web preset (<see cref="JsonConstants.CreateWebJsonOptions"/>) with
/// two changes that make a written value read back unchanged. String dictionary keys are written as they are, not
/// camelCased, and an <see langword="object"/> value reads back as a <see cref="JsonElement"/> rather than a
/// <c>JsonNode</c>.
/// </summary>
/// <remarks>
/// The web preset camelCases dictionary keys on write only, which suits an HTTP response but makes a round trip
/// lossy: <c>{"Admin":1}</c> comes back as <c>{"admin":1}</c>, so a cache entry or blob metadata read on another node
/// no longer has the key it was written with. This provider backs every <see cref="SystemJsonSerializer"/> built
/// without one, including the framework's default <see cref="IJsonSerializer"/> registrations.
/// </remarks>
[PublicAPI]
public sealed class DefaultJsonOptionsProvider : IJsonOptionsProvider
{
    private static readonly JsonSerializerOptions _Options = _CreateOptions();

    /// <inheritdoc/>
    public JsonSerializerOptions GetSerializeOptions()
    {
        return _Options;
    }

    /// <inheritdoc/>
    public JsonSerializerOptions GetDeserializeOptions()
    {
        return _Options;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "The default options use the reflection-based serializer; AOT consumers supply their own provider."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "The default options use the reflection-based serializer; AOT consumers supply their own provider."
    )]
    private static JsonSerializerOptions _CreateOptions()
    {
        var options = JsonConstants.CreateWebJsonOptions();
        options.DictionaryKeyPolicy = null;
        options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement;
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }
}
