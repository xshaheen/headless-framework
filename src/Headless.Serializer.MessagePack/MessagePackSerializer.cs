// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MessagePack;
using MessagePack.Resolvers;

namespace Headless.Serializer;

/// <summary>
/// <see cref="IBinarySerializer"/> implementation backed by the MessagePack-CSharp library.
/// Produces compact binary output rather than JSON text.
/// </summary>
/// <remarks>
/// <para>
/// When no <see cref="MessagePackSerializerOptions"/> are provided the serializer defaults to
/// <c>MessagePackSerializerOptions.Standard</c> with <c>ContractlessStandardResolver</c>, which maps
/// .NET properties by name without requiring <c>[MessagePackObject]</c> attributes. For production
/// scenarios where schema drift or security (untrusted input deserialization) is a concern, supply
/// explicit options with a typed resolver and enable <c>Security = MessagePackSecurity.UntrustedData</c>.
/// </para>
/// <para>
/// For compressed output wrap the resolver with <c>LZ4MessagePackSerializer</c> options at the call
/// site rather than changing this class.
/// </para>
/// </remarks>
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
