// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetTopologySuite.IO.Converters;

namespace Framework.Api.MinimalApi.Options;

public sealed class ConfigureMinimalApiJsonOptions(IWebHostEnvironment webHostEnvironment)
    : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options)
    {
        var jsonOptions = options.SerializerOptions;

        jsonOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        jsonOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        jsonOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        jsonOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        jsonOptions.PreferredObjectCreationHandling = JsonObjectCreationHandling.Replace;
        jsonOptions.UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode;
        jsonOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
        jsonOptions.IgnoreReadOnlyProperties = false;
        jsonOptions.PropertyNameCaseInsensitive = false;
        // Pretty print the JSON in development for easier debugging.
        jsonOptions.WriteIndented = webHostEnvironment.IsDevelopmentOrTest();

        jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        jsonOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;

        jsonOptions.Converters.Add(new GeoJsonConverterFactory());
        jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }
}
