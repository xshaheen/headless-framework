// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

// ReSharper disable once CheckNamespace
namespace Framework.Serializer;

public static class SerializerJsonConstants
{
    public static JsonSerializerOptions ConfigureWebJsonOptions(JsonSerializerOptions options)
    {
        options.Encoder = JavaScriptEncoder.Default;
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
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;

        options.Converters.Clear();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

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
        options.AllowTrailingCommas = false;
        options.ReferenceHandler = null;

        options.Converters.Clear();
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

        return options;
    }
}
