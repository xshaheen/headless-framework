// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.Serializer.Converters;

namespace Headless.EntityFramework.Configurations;

/// <summary>
/// Shared, immutable <see cref="JsonSerializerOptions"/> used by the EF Core value converters in this
/// package to serialize/deserialize JSON-backed columns. Exposed as a single cached instance because
/// the configuration is identical and the options are reused (and frozen after first use).
/// </summary>
internal static class EfCoreJsonOptions
{
    public static JsonSerializerOptions Instance { get; } = _Create();

    private static JsonSerializerOptions _Create()
    {
        var options = new JsonSerializerOptions();

        JsonConstants.ConfigureInternalJsonOptions(options);
        options.Converters.Add(new ObjectToInferredTypesJsonConverter());

        return options;
    }
}
