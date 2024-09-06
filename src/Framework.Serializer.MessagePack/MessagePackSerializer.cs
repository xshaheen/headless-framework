using MessagePack;
using MessagePack.Resolvers;

// ReSharper disable once CheckNamespace
namespace Framework.Serializer;

public sealed class MessagePackSerializer(MessagePackSerializerOptions? options = null) : IBinarySerializer
{
    private readonly MessagePackSerializerOptions _options =
        options ?? MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    public void Serialize<T>(T value, Stream output)
    {
        MessagePack.MessagePackSerializer.Serialize(output, value, _options);
    }

    public T? Deserialize<T>(Stream data)
    {
        return MessagePack.MessagePackSerializer.Deserialize<T?>(data, _options);
    }
}
