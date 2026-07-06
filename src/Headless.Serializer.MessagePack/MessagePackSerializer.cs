// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using MessagePack;
using MessagePack.Resolvers;

namespace Headless.Serializer;

/// <summary>
/// <see cref="IBinarySerializer"/> implementation backed by the MessagePack-CSharp library.
/// Produces compact binary output rather than JSON text.
/// </summary>
/// <remarks>
/// <para>
/// Writes go straight to the caller's <see cref="IBufferWriter{T}"/> and reads consume the payload as a
/// <see cref="ReadOnlyMemory{T}"/> / <see cref="ReadOnlySequence{T}"/> in place — the library's native
/// low-allocation surface, avoiding the buffer-copy overhead its <see cref="Stream"/> overloads incur.
/// </para>
/// <para>
/// With no <paramref name="options"/> the serializer uses <c>MessagePackSerializerOptions.Standard</c> with
/// <c>ContractlessStandardResolver</c>, which maps .NET properties by name without requiring
/// <c>[MessagePackObject]</c> attributes, with <see cref="MessagePackSecurity.UntrustedData"/> applied by default.
/// </para>
/// <para>
/// The default security level is safe for untrusted payloads such as cross-service caches or external message
/// producers. For trusted in-process payloads where the MessagePack-CSharp fast path is a deliberate choice, pass
/// <c>untrustedData: false</c> or supply custom <see cref="MessagePackSerializerOptions"/>. The
/// <c>untrustedData</c> switch configures only the default (no-<paramref name="options"/>) path: when you supply
/// your own options the serializer uses them verbatim — you own the security level there (set <c>Security</c> on
/// the options) and the switch is not applied on top.
/// </para>
/// <para>
/// For compressed output wrap the resolver with <c>LZ4MessagePackSerializer</c> options at the call
/// site rather than changing this class.
/// </para>
/// </remarks>
/// <param name="options">
/// The MessagePack options to use, or <see langword="null"/> for the contractless standard defaults. When supplied,
/// they are used verbatim and own the security level (see <paramref name="untrustedData"/>).
/// </param>
/// <param name="untrustedData">
/// When <see langword="true"/> and no <paramref name="options"/> are supplied, applies
/// <see cref="MessagePackSecurity.UntrustedData"/> (recursion-depth limit + collision-resistant hashing) to the
/// default options for safe deserialization of untrusted input. Pass <see langword="false"/> only for trusted
/// payloads where the MessagePack-CSharp fast path is intentional. Ignored when <paramref name="options"/> are
/// supplied. Defaults to <see langword="true"/>.
/// </param>
public sealed class MessagePackSerializer(MessagePackSerializerOptions? options = null, bool untrustedData = true)
    : IBinarySerializer
{
    private readonly MessagePackSerializerOptions _options = _ResolveOptions(options, untrustedData);

    private static MessagePackSerializerOptions _ResolveOptions(
        MessagePackSerializerOptions? options,
        bool untrustedData
    )
    {
        if (options is not null)
        {
            // Supplied options own the security level; the untrustedData switch configures only the default path so
            // it can never override (and thereby relax) a Security the caller set explicitly.
            return options;
        }

        var resolved = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

        return untrustedData ? resolved.WithSecurity(MessagePackSecurity.UntrustedData) : resolved;
    }

    public void Serialize<T>(T value, IBufferWriter<byte> output)
    {
        MessagePack.MessagePackSerializer.Serialize(output, value, _options);
    }

    public void Serialize(object? value, IBufferWriter<byte> output)
    {
        MessagePack.MessagePackSerializer.Serialize(value?.GetType() ?? typeof(object), output, value, _options);
    }

    public T? Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        return MessagePack.MessagePackSerializer.Deserialize<T?>(data, _options);
    }

    public T? Deserialize<T>(in ReadOnlySequence<byte> data)
    {
        return MessagePack.MessagePackSerializer.Deserialize<T?>(data, _options);
    }

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        return MessagePack.MessagePackSerializer.Deserialize(type, data, _options);
    }

    public object? Deserialize(in ReadOnlySequence<byte> data, Type type)
    {
        return MessagePack.MessagePackSerializer.Deserialize(type, data, _options);
    }
}
