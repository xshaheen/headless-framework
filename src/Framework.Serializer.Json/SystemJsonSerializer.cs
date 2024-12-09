// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Serializer;

public sealed class SystemJsonSerializer(JsonSerializerOptions options) : IJsonSerializer
{
    public T? Deserialize<T>(Stream data)
    {
        return JsonSerializer.Deserialize<T>(data, options);
    }

    public void Serialize<T>(T value, Stream output)
    {
        using var writer = new Utf8JsonWriter(output);
        JsonSerializer.Serialize(writer, value, options);
    }

    public object? Deserialize(Stream data, Type objectType)
    {
        return JsonSerializer.Deserialize(data, objectType, options);
    }

    public void Serialize(object? value, Stream output)
    {
        JsonSerializer.Serialize(value, options);
    }
}
