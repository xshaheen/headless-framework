// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Framework.Serializer;

[RequiresUnreferencedCode("JSON serialization and deserialization might require types that cannot be statically analyzed.")]
[RequiresDynamicCode("JSON serialization and deserialization might require runtime code generation.")]
public sealed class SystemJsonSerializer(IJsonOptionsProvider? optionsProvider = null) : IJsonSerializer
{
    private readonly IJsonOptionsProvider _optionsProvider = optionsProvider ?? new DefaultJsonOptionsProvider();

    public T? Deserialize<T>(Stream data)
    {
        return JsonSerializer.Deserialize<T>(data, _optionsProvider.GetDeserializeOptions());
    }

    public object? Deserialize(Stream data, Type objectType)
    {
        return JsonSerializer.Deserialize(data, objectType, _optionsProvider.GetDeserializeOptions());
    }

    public void Serialize<T>(T? value, Stream output)
    {
        JsonSerializer.Serialize(output, value, _optionsProvider.GetSerializeOptions());
    }

    public void Serialize(object? value, Stream output)
    {
        JsonSerializer.Serialize(
            output,
            value,
            value is null ? typeof(object) : value.GetType(),
            _optionsProvider.GetSerializeOptions()
        );
    }
}
