// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization.Metadata;

namespace Headless.Tus.Internal;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(TusS3UploadState))]
[JsonSerializable(typeof(TusS3CompletedRecord))]
internal sealed partial class TusS3JsonContext : JsonSerializerContext
{
    /// <summary>Reads a stored object, reporting JSON this build cannot parse as invalid data.</summary>
    /// <exception cref="InvalidDataException">The JSON is malformed or holds a value this store cannot read.</exception>
    internal static T? Read<T>(ReadOnlySpan<byte> json, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException e)
        {
            // Callers skip an unreadable object by catching InvalidDataException; a corrupt object or an enum value a
            // newer build added must take that path too, or one bad object stops every sweep pass.
            throw new InvalidDataException("The tus upload object is not JSON this store can read.", e);
        }
    }

    /// <summary>Refuses an object written by a newer schema than this build reads.</summary>
    /// <exception cref="InvalidDataException">The object was written by a newer schema.</exception>
    internal static void EnsureReadableSchema(int schemaVersion, int currentSchemaVersion)
    {
        // A newer writer may have added fields this build would silently drop on its next conditional write.
        if (schemaVersion > currentSchemaVersion)
        {
            throw new InvalidDataException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The tus upload object has schema version {schemaVersion}, newer than the {currentSchemaVersion} this store reads."
                )
            );
        }
    }
}
