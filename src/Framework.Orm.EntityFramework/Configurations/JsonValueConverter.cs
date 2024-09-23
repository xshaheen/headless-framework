// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.Json;
using Framework.Kernel.BuildingBlocks.Constants;
using Framework.Serializer.Json.Converters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

public sealed class JsonValueConverter<TPropertyType> : ValueConverter<TPropertyType, string>
{
    private static readonly JsonSerializerOptions _Options = _CreateJsonOptions();

    public JsonValueConverter()
        : base(d => _SerializeObject(d), s => _DeserializeObject(s)) { }

    private static string _SerializeObject(TPropertyType d)
    {
        return JsonSerializer.Serialize(d, _Options);
    }

    private static TPropertyType _DeserializeObject(string s)
    {
        return JsonSerializer.Deserialize<TPropertyType>(s, _Options)!;
    }

    private static JsonSerializerOptions _CreateJsonOptions()
    {
        var option = new JsonSerializerOptions();

        PlatformJsonConstants.ConfigureInternalJsonOptions(option);
        option.Converters.Add(new ObjectToInferredTypesConverter());

        return option;
    }
}
