// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NetTopologySuite.IO.Converters;

namespace Framework.Api.MinimalApi.Options;

[PublicAPI]
public sealed class ConfigureMinimalApiJsonOptions(IWebHostEnvironment environment) : IConfigureOptions<JsonOptions>
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
        jsonOptions.WriteIndented = environment.IsDevelopmentOrTest();

        jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        jsonOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;

        jsonOptions.Converters.Add(new GeoJsonConverterFactory());
        jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    }
}
