// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using NetTopologySuite.IO.Converters;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Serializer;

public static class JsonConstants
{
    public static readonly JsonSerializerOptions DefaultWebJsonOptions = CreateWebJsonOptions();
    public static readonly JsonSerializerOptions DefaultInternalJsonOptions = CreateInternalJsonOptions();
    public static readonly JsonSerializerOptions DefaultPrettyJsonOptions = new() { WriteIndented = true };

    public static JsonSerializerOptions CreateWebJsonOptions()
    {
        return ConfigureWebJsonOptions(new JsonSerializerOptions());
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
        options.PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate;
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

    private static void _AddDefaultConverters(JsonSerializerOptions options)
    {
        var enumConverter = options.Converters.FirstOrDefault(x => x is JsonStringEnumConverter);

        if (enumConverter is not null)
        {
            options.Converters.Remove(enumConverter);
        }

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.Converters.Add(new GeoJsonConverterFactory());
    }
}
