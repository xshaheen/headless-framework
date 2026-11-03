// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Headless.Serializer.Converters;

namespace Headless.Serializer;

/// <summary>
/// Pre-built <see cref="JsonSerializerOptions"/> presets and factory helpers for common serialization scenarios.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DefaultWebJsonOptions"/> — camelCase property names, dictionary keys as written, case-insensitive reads,
/// numbers from strings, unknown members skipped, duplicate properties refused, a polymorphic type discriminator
/// accepted in any position, <see langword="object"/> values read as <see cref="JsonElement"/>, a reference cycle
/// refused rather than written as <see langword="null"/>, <c>RespectNullableAnnotations</c>/
/// <c>RespectRequiredConstructorParameters</c>, and the <see cref="IpAddressJsonConverter"/> included. Intended for
/// public-facing HTTP APIs.
/// </para>
/// <para>
/// <see cref="DefaultInternalJsonOptions"/> — built on <see cref="JsonSerializerDefaults.Strict"/>: strict number handling, no naming policy (preserves property
/// casing), unknown members and duplicate properties disallowed, null values omitted, <see langword="object"/> values
/// read as <see cref="JsonElement"/>, and a polymorphic type discriminator accepted in any position. Intended for internal persistence or inter-service payloads where schema
/// drift must surface immediately. <see cref="CreateInternalJsonOptions"/> and
/// <see cref="ConfigureInternalJsonOptions"/> take an optional naming policy, such as
/// <see cref="JsonNamingPolicy.SnakeCaseLower"/>, for a contract shared with a non-.NET producer.
/// </para>
/// <para>
/// <see cref="DefaultStorageJsonOptions"/> — the internal preset with unknown members skipped, so an older node reads
/// what a newer one wrote, null values written, so a constructor parameter that holds null reads back, plus <see cref="ObjectToInferredTypesJsonConverter"/>, so an <see langword="object"/> value reads back as a CLR primitive (<see cref="string"/>, <see cref="bool"/>,
/// <see cref="long"/>, <see cref="decimal"/>) or a <see cref="JsonElement"/> for an object or array. Intended for JSON
/// the framework persists and reads back: EF Core JSON columns, <c>ExtraProperties</c> bags, the relational definition
/// stores, and, through <see cref="DefaultJsonOptionsProvider"/>, the app values the framework stores (cache entries,
/// blob metadata). Framework-owned records always use it, so every node and every storage provider writes the same
/// JSON for the same value; a host that registers its own <see cref="IJsonOptionsProvider"/> changes only how its own
/// stored values are written.
/// </para>
/// <para>
/// <see cref="DefaultPrettyJsonOptions"/> — identical to <see cref="DefaultWebJsonOptions"/> with
/// <see cref="JsonSerializerOptions.WriteIndented"/> set to <see langword="true"/>. Useful for logging and
/// diagnostics.
/// </para>
/// <para>
/// All four presets include <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/>, with integer values
/// disallowed, and <see cref="Converters.IpAddressJsonConverter"/>, via the shared
/// <see cref="ConfigureWebJsonOptions"/> / <see cref="ConfigureInternalJsonOptions"/> helpers. The web and pretty
/// presets write an enum under its member name as declared (<c>Active</c>, or a <c>[JsonStringEnumMemberName]</c>
/// alias); the internal preset writes it camelCase unless given a naming policy.
/// </para>
/// <para>
/// The shared presets are frozen (<see cref="JsonSerializerOptions.IsReadOnly"/> is <see langword="true"/>)
/// at initialization, so they can be shared process-wide without a caller silently mutating framework serialization.
/// To customize, start from a fresh mutable instance via <see cref="CreateWebJsonOptions"/>,
/// <see cref="CreateInternalJsonOptions"/>, <see cref="CreateStorageJsonOptions"/>, or
/// <see cref="CreatePrettyJsonOptions"/> instead of mutating a preset.
/// </para>
/// </remarks>
[PublicAPI]
public static class JsonConstants
{
    /// <summary>
    /// Shared, read-only options for public-facing HTTP API serialization. See the <see cref="JsonConstants"/>
    /// remarks for the full configuration applied. Use <see cref="CreateWebJsonOptions"/> for a mutable copy.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultWebJsonOptions = _Freeze(CreateWebJsonOptions());

    /// <summary>
    /// Shared, read-only options for internal persistence or inter-service serialization. See the
    /// <see cref="JsonConstants"/> remarks for the full configuration applied. Use
    /// <see cref="CreateInternalJsonOptions"/> for a mutable copy.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultInternalJsonOptions = _Freeze(CreateInternalJsonOptions());

    /// <summary>
    /// Shared, read-only options for JSON the framework persists and reads back itself. See the
    /// <see cref="JsonConstants"/> remarks for the full configuration applied. Use
    /// <see cref="CreateStorageJsonOptions"/> for a mutable copy.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultStorageJsonOptions = _Freeze(CreateStorageJsonOptions());

    /// <summary>
    /// Shared, read-only options for human-readable (indented) output. Identical to
    /// <see cref="DefaultWebJsonOptions"/> with <see cref="JsonSerializerOptions.WriteIndented"/> set to
    /// <see langword="true"/>. Use <see cref="CreatePrettyJsonOptions"/> for a mutable copy.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultPrettyJsonOptions = _Freeze(CreatePrettyJsonOptions());

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
    /// <param name="namingPolicy">
    /// The naming policy for property names and enum values, such as <see cref="JsonNamingPolicy.SnakeCaseLower"/>.
    /// When <see langword="null"/>, property names keep their CLR casing and enum values are camelCase.
    /// </param>
    /// <returns>A new, mutable options instance with the internal preset applied.</returns>
    public static JsonSerializerOptions CreateInternalJsonOptions(JsonNamingPolicy? namingPolicy = null)
    {
        return ConfigureInternalJsonOptions(new JsonSerializerOptions(JsonSerializerDefaults.Strict), namingPolicy);
    }

    /// <summary>
    /// Creates a new <see cref="JsonSerializerOptions"/> instance configured for JSON the framework persists and reads
    /// back itself: the internal preset with unknown members skipped and <see cref="ObjectToInferredTypesJsonConverter"/>
    /// added.
    /// </summary>
    /// <returns>A new, mutable options instance with the storage preset applied.</returns>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Presets use the reflection-based serializer by default; consumers should use source generation for AOT/trimmed scenarios."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Presets use the reflection-based serializer by default; consumers should use source generation for AOT scenarios."
    )]
    public static JsonSerializerOptions CreateStorageJsonOptions()
    {
        var options = CreateInternalJsonOptions();
        // Stored JSON outlives the code that wrote it: during a rolling deploy an older node reads what a newer node
        // wrote, so a member it does not know is skipped rather than failing the read.
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        // A round trip must give back what was written: an omitted null fails the read of a constructor parameter, and
        // RespectRequiredConstructorParameters makes every one required.
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new ObjectToInferredTypesJsonConverter());

        return options;
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
        // A cycle in a response is a modelling bug; IgnoreCycles would hide it by writing null, so the write throws.
        options.ReferenceHandler = null;
        // Make it populate when this get fixed: https://github.com/dotnet/runtime/issues/92877
        options.PreferredObjectCreationHandling = JsonObjectCreationHandling.Replace;
        options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        // A proxy or WAF that reads the first copy of a repeated key would vet a different payload than the app binds.
        options.AllowDuplicateProperties = false;
        // Clients do not all write a polymorphic type discriminator first, and the order carries no meaning.
        options.AllowOutOfOrderMetadataProperties = true;
        options.IgnoreReadOnlyProperties = false;
        options.WriteIndented = false;
        options.RespectNullableAnnotations = true;
        options.RespectRequiredConstructorParameters = true;
        options.AllowTrailingCommas = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        // String dictionary keys are data (identifiers, role names, user input), not member names, so they go out as
        // written; a response that wants camelCase keys, such as validation errors, names them itself.
        options.DictionaryKeyPolicy = null;
        // Enums go out under their member names, so a route, query, or header value a client copies from a response or
        // the OpenAPI document is the text Enum.TryParse already reads, and binding it needs no rewrite or allocation.
        _AddDefaultConverters(options, enumNamingPolicy: null);

        return options;
    }

    /// <summary>
    /// Applies the internal-serialization preset to an existing <paramref name="options"/> instance.
    /// </summary>
    /// <param name="options">The options instance to configure. Must not be read-only.</param>
    /// <param name="namingPolicy">
    /// The naming policy for property names and enum values, such as <see cref="JsonNamingPolicy.SnakeCaseLower"/>.
    /// When <see langword="null"/>, property names keep their CLR casing and enum values are camelCase. String
    /// dictionary keys are data, not member names, so they are never renamed; enum dictionary keys are enum values and
    /// follow the enum naming.
    /// </param>
    /// <returns>The same <paramref name="options"/> instance, to support a fluent call style.</returns>
    public static JsonSerializerOptions ConfigureInternalJsonOptions(
        JsonSerializerOptions options,
        JsonNamingPolicy? namingPolicy = null
    )
    {
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.NumberHandling = JsonNumberHandling.Strict;
        options.ReadCommentHandling = JsonCommentHandling.Disallow;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        // Make it populate when this get fixed: https://github.com/dotnet/runtime/issues/92877
        options.PreferredObjectCreationHandling = JsonObjectCreationHandling.Replace;
        options.UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement;
        options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
        // Parsers in other languages disagree on which duplicate wins, so a strict contract refuses the ambiguity.
        options.AllowDuplicateProperties = false;
        // PostgreSQL jsonb reorders keys, which can move a polymorphic type discriminator after ordinary properties.
        options.AllowOutOfOrderMetadataProperties = true;
        options.PropertyNamingPolicy = namingPolicy;
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
        _AddDefaultConverters(options, namingPolicy ?? JsonNamingPolicy.CamelCase);

        return options;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Presets use the reflection-based serializer by default; consumers should use source generation for AOT/trimmed scenarios."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Presets use the reflection-based serializer by default; consumers should use source generation for AOT scenarios."
    )]
    private static JsonSerializerOptions _Freeze(JsonSerializerOptions options)
    {
        // Populate the default reflection resolver eagerly (matching lazy first-use behavior) so the shared preset
        // can be locked. Once read-only, any consumer mutation throws instead of silently altering framework state.
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }

    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "JsonStringEnumConverter is used for serialization options and consumers should use source generation for AOT scenarios."
    )]
    private static void _AddDefaultConverters(JsonSerializerOptions options, JsonNamingPolicy? enumNamingPolicy)
    {
        var enumConverter = options.Converters.FirstOrDefault(x => x is JsonStringEnumConverter);

        if (enumConverter is not null)
        {
            options.Converters.Remove(enumConverter);
        }

        options.Converters.Add(new JsonStringEnumConverter(enumNamingPolicy, allowIntegerValues: false));
        options.Converters.Add(new IpAddressJsonConverter());
    }
}
