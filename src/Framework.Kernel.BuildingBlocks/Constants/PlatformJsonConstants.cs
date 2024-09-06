using System.Text.Json;
using System.Text.Json.Serialization;
using Framework.Kernel.Primitives;
using Framework.Serializer;
using NetTopologySuite.IO.Converters;

namespace Framework.Kernel.BuildingBlocks.Constants;

public static class PlatformJsonConstants
{
    private static readonly List<JsonConverter> _DefaultConverters =
    [
        new GeoJsonConverterFactory(),
        new IpAddressJsonConverter(),
        new IpAddressRangeJsonConverter(),
    ];

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

        foreach (var converter in _DefaultConverters)
        {
            options.Converters.Add(converter);
        }

        return options;
    }

    public static JsonSerializerOptions ConfigureInternalJsonOptions(JsonSerializerOptions options)
    {
        SerializerJsonConstants.ConfigureInternalJsonOptions(options);

        foreach (var converter in _DefaultConverters)
        {
            options.Converters.Add(converter);
        }

        return options;
    }
}
