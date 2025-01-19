// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Serializer;

public sealed class SystemJsonSerializer(JsonSerializerOptions? options = null) : IJsonSerializer
{
    private readonly JsonSerializerOptions _options = options ?? JsonConstants.DefaultWebJsonOptions;

    public T? Deserialize<T>(Stream data)
    {
        return JsonSerializer.Deserialize<T>(data, _options);
    }

    public void Serialize<T>(T value, Stream output)
    {
        using var writer = new Utf8JsonWriter(output);
        JsonSerializer.Serialize(writer, value, _options);
    }

    public object? Deserialize(Stream data, Type objectType)
    {
        return JsonSerializer.Deserialize(data, objectType, _options);
    }

    public void Serialize(object? value, Stream output)
    {
        JsonSerializer.Serialize(value, _options);
    }
}
