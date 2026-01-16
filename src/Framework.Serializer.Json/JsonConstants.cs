// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using Framework.Serializer.Converters;

namespace Framework.Serializer;

public static class JsonConstants
{
    public static readonly JsonSerializerOptions DefaultWebJsonOptions = CreateWebJsonOptions();
    public static readonly JsonSerializerOptions DefaultInternalJsonOptions = CreateInternalJsonOptions();
    public static readonly JsonSerializerOptions DefaultPrettyJsonOptions = CreatePrettyJsonOptions();

    public static JsonSerializerOptions CreateWebJsonOptions()
    {
        return ConfigureWebJsonOptions(new JsonSerializerOptions());
    }

    public static JsonSerializerOptions CreatePrettyJsonOptions()
    {
        var webJsonOptions = CreateWebJsonOptions();
        webJsonOptions.WriteIndented = true;

        return webJsonOptions;
    }

    public static JsonSerializerOptions CreateInternalJsonOptions()
    {
        return ConfigureInternalJsonOptions(new JsonSerializerOptions());
    }

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
