// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
