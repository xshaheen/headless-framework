// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Headless.Serializer.Converters;

namespace Headless.Serializer;

/// <summary>
/// Pre-built <see cref="JsonSerializerOptions"/> presets and factory helpers for common serialization scenarios.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DefaultWebJsonOptions"/> — camelCase naming, case-insensitive reads, numbers from strings,
/// cycle-safe reference handling, <c>RespectNullableAnnotations</c>/<c>RespectRequiredConstructorParameters</c>,
/// and the <see cref="IpAddressJsonConverter"/> included. Intended for public-facing HTTP APIs. Used by
/// <see cref="DefaultJsonOptionsProvider"/>.
/// </para>
/// <para>
/// <see cref="DefaultInternalJsonOptions"/> — strict number handling, no naming policy (preserves property
/// casing), unknown members disallowed, null values omitted. Intended for internal persistence or
/// inter-service payloads where schema drift must surface immediately.
/// </para>
/// <para>
/// <see cref="DefaultPrettyJsonOptions"/> — identical to <see cref="DefaultWebJsonOptions"/> with
/// <see cref="JsonSerializerOptions.WriteIndented"/> set to <see langword="true"/>. Useful for logging and
/// diagnostics.
/// </para>
/// <para>
/// All three presets include <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> (camelCase,
/// integer values disallowed) and <see cref="Converters.IpAddressJsonConverter"/> via the shared
/// <see cref="ConfigureWebJsonOptions"/> / <see cref="ConfigureInternalJsonOptions"/> helpers.
/// </para>
/// </remarks>
public static class JsonConstants
{
    /// <summary>
    /// Shared options for public-facing HTTP API serialization. See the <see cref="JsonConstants"/> remarks for
    /// the full configuration applied.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultWebJsonOptions = CreateWebJsonOptions();

    /// <summary>
    /// Shared options for internal persistence or inter-service serialization. See the <see cref="JsonConstants"/>
    /// remarks for the full configuration applied.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultInternalJsonOptions = CreateInternalJsonOptions();

    /// <summary>
    /// Shared options for human-readable (indented) output. Identical to <see cref="DefaultWebJsonOptions"/>
    /// with <see cref="JsonSerializerOptions.WriteIndented"/> set to <see langword="true"/>.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultPrettyJsonOptions = CreatePrettyJsonOptions();

    /// <summary>Creates a new <see cref="JsonSerializerOptions"/> instance configured for public-facing HTTP APIs.</summary>
    /// <returns>A new, mutable options instance with the web preset applied.</returns>
    public static JsonSerializerOptions CreateWebJsonOptions()
    {
        return ConfigureWebJsonOptions(new JsonSerializerOptions());
    }

    /// <summary>
    /// Creates a new <see cref="JsonSerializerOptions"/> instance configured for human-readable (indented) output.
    /// </summary>
    /// <returns>A new, mutable options instance based on the web preset with <see cref="JsonSerializerOptions.WriteIndented"/> enabled.</returns>
    public static JsonSerializerOptions CreatePrettyJsonOptions()
    {
        var webJsonOptions = CreateWebJsonOptions();
        webJsonOptions.WriteIndented = true;

        return webJsonOptions;
    }

    /// <summary>Creates a new <see cref="JsonSerializerOptions"/> instance configured for internal serialization.</summary>
    /// <returns>A new, mutable options instance with the internal preset applied.</returns>
    public static JsonSerializerOptions CreateInternalJsonOptions()
    {
        return ConfigureInternalJsonOptions(new JsonSerializerOptions());
    }

    /// <summary>
    /// Applies the web-API preset to an existing <paramref name="options"/> instance.
    /// </summary>
    /// <param name="options">The options instance to configure. Must not be read-only.</param>
    /// <returns>The same <paramref name="options"/> instance, to support a fluent call style.</returns>
    public static JsonSerializerOptions ConfigureWebJsonOptions(JsonSerializerOptions options)
    {
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.PropertyNameCaseInsensitive = true;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.ReadCommentHandling = JsonCommentHandling.Disallow;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        // Make it populate when this get fixed: https://github.com/dotnet/runtime/issues/92877
        options.PreferredObjectCreationHandling = JsonObjectCreationHandling.Replace;
        options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        options.IgnoreReadOnlyProperties = false;
        options.WriteIndented = false;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        options.AllowTrailingCommas = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        _AddDefaultConverters(options);

        return options;
    }

    /// <summary>
    /// Applies the internal-serialization preset to an existing <paramref name="options"/> instance.
    /// </summary>
    /// <param name="options">The options instance to configure. Must not be read-only.</param>
    /// <returns>The same <paramref name="options"/> instance, to support a fluent call style.</returns>
    public static JsonSerializerOptions ConfigureInternalJsonOptions(JsonSerializerOptions options)
    {
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.ReadCommentHandling = JsonCommentHandling.Disallow;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // Make it populate when this get fixed: https://github.com/dotnet/runtime/issues/92877
        options.PreferredObjectCreationHandling = JsonObjectCreationHandling.Replace;
        options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        options.PropertyNamingPolicy = null;
        options.DictionaryKeyPolicy = null;
        options.PropertyNameCaseInsensitive = false;
        options.IgnoreReadOnlyProperties = false;
        options.IncludeFields = false;
        options.IgnoreReadOnlyFields = false;
        options.WriteIndented = false;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        options.AllowTrailingCommas = false;
        options.ReferenceHandler = null;
        _AddDefaultConverters(options);

        return options;
    }

    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "JsonStringEnumConverter is used for serialization options and consumers should use source generation for AOT scenarios."
    )]
    private static void _AddDefaultConverters(JsonSerializerOptions options)
    {
        var enumConverter = options.Converters.FirstOrDefault(x => x is JsonStringEnumConverter);

        if (enumConverter is not null)
        {
            options.Converters.Remove(enumConverter);
        }

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new IpAddressJsonConverter());
    }
}
