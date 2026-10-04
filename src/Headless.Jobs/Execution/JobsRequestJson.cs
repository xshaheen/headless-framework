// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

namespace Headless.Jobs;

/// <summary>
/// Resolves job payload metadata through the configured <see cref="JsonSerializerOptions"/> instead of the
/// reflection-only serializer overloads, so Core stays trim- and AOT-safe and the application decides where metadata
/// comes from: reflection by default, or a <c>JsonSerializerContext</c> in trimmed and native AOT apps.
/// </summary>
internal static class JobsRequestJson
{
    /// <summary>
    /// General-default options whose metadata comes from reflection when the app allows reflection-based
    /// serialization, and from nowhere when it does not; such an app registers a <c>JsonSerializerContext</c> through
    /// <c>ConfigureRequestJsonOptions</c>.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = _CreateDefaultOptions();

    /// <summary>
    /// Makes <paramref name="options"/> usable through <see cref="JsonSerializerOptions.GetTypeInfo"/>, which throws
    /// for options without a resolver even when reflection is enabled: a missing resolver is filled the way
    /// <see cref="DefaultOptions"/> fills it, and the options are locked. Read-only options already have a resolver.
    /// </summary>
    public static JsonSerializerOptions Prepare(JsonSerializerOptions options)
    {
        if (!options.IsReadOnly)
        {
            options.TypeInfoResolver ??= _CreateDefaultResolver();
            options.MakeReadOnly();
        }

        return options;
    }

    public static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions options) =>
        (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));

    private static JsonSerializerOptions _CreateDefaultOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            TypeInfoResolver = _CreateDefaultResolver(),
        };
        options.MakeReadOnly();
        return options;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "The reflection resolver is created only when JsonSerializer.IsReflectionEnabledByDefault is true. Trimmed and native AOT publishing turn that feature switch off, and the trimmer removes this branch."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
        Justification = "The reflection resolver is created only when JsonSerializer.IsReflectionEnabledByDefault is true. Native AOT publishing turns that feature switch off, and the trimmer removes this branch."
    )]
    private static IJsonTypeInfoResolver _CreateDefaultResolver() =>
        JsonSerializer.IsReflectionEnabledByDefault
            ? new DefaultJsonTypeInfoResolver()
            : JsonTypeInfoResolver.Combine();
}
