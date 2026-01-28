// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MessagePack;
using MessagePack.Resolvers;

namespace Headless.Serializer;

public sealed class MessagePackSerializer(MessagePackSerializerOptions? options = null) : IBinarySerializer
{
    private readonly MessagePackSerializerOptions _options =
        options ?? MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    public void Serialize<T>(T value, Stream output)
    {
        MessagePack.MessagePackSerializer.Serialize(output, value, _options);
    }

    public object? Deserialize(Stream data, Type objectType)
    {
        return MessagePack.MessagePackSerializer.Deserialize(objectType, data, _options);
    }

    public void Serialize(object? value, Stream output)
    {
        MessagePack.MessagePackSerializer.Serialize(output, value, _options);
    }

    public T? Deserialize<T>(Stream data)
    {
        return MessagePack.MessagePackSerializer.Deserialize<T?>(data, _options);
    }
}
