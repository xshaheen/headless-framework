// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Text.Json;

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
}
