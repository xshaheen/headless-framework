// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Serializer;

/// <summary>
/// Marker interface for serializers that produce binary (non-text) output, such as MessagePack or Protocol Buffers.
/// </summary>
/// <remarks>
/// <see cref="SerializerExtensions.SerializeToString{T}"/> encodes binary output as Base64 when the serializer
/// does not implement <see cref="ITextSerializer"/>.
/// </remarks>
public interface IBinarySerializer : ISerializer;
