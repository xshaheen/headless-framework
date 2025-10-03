// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Framework.Serializer.Converters;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Configurations;

[PublicAPI]
public sealed class JsonValueConverter<TPropertyType>()
    : ValueConverter<TPropertyType, string>(d => _SerializeObject(d), s => _DeserializeObject(s))
{
    private static readonly JsonSerializerOptions _Options = _CreateJsonOptions();

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

        JsonConstants.ConfigureInternalJsonOptions(option);
        option.Converters.Add(new ObjectToInferredTypesJsonConverter());

        return option;
    }
}
