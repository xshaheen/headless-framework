// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Serializer;

public sealed class SystemJsonSerializer(JsonSerializerOptions? options = null) : IJsonSerializer
{
    private readonly JsonSerializerOptions _options = options ?? JsonConstants.DefaultWebJsonOptions;

    public T? Deserialize<T>(Stream data)
    {
        return JsonSerializer.Deserialize<T>(data, _options);
    }

    public object? Deserialize(Stream data, Type objectType)
    {
        return JsonSerializer.Deserialize(data, objectType, _options);
    }

    public void Serialize<T>(T? value, Stream output)
    {
        JsonSerializer.Serialize(output, value, _options);
    }

    public void Serialize(object? value, Stream output)
    {
        JsonSerializer.Serialize(output, value, value is null ? typeof(object) : value.GetType(), _options);
    }
}
