// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Framework.Serializer.Json.Converters;
using NetTopologySuite.IO.Converters;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.BuildingBlocks;

public static class FrameworkJsonConstants
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
        SerializerJsonConstants.ConfigureWebJsonOptions(options);
        _AddDefaultConverters(options);

        return options;
    }

    public static JsonSerializerOptions ConfigureInternalJsonOptions(JsonSerializerOptions options)
    {
        SerializerJsonConstants.ConfigureInternalJsonOptions(options);
        _AddDefaultConverters(options);

        return options;
    }

    private static void _AddDefaultConverters(JsonSerializerOptions options)
    {
        options.Converters.Add(new GeoJsonConverterFactory());
        options.Converters.Add(new IpAddressJsonConverter());
    }
}
