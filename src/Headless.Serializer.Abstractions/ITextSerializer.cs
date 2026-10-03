// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;

namespace Headless.Serializer;

/// <summary>
/// Marker interface for serializers that produce UTF-8 text output, such as JSON or XML.
/// </summary>
/// <remarks>
/// <see cref="SerializerExtensions.SerializeToString{T}"/> returns the raw UTF-8 string for text serializers,
/// and <see cref="SerializerExtensions.Deserialize{T}(ISerializer, string)"/> decodes the input as UTF-8 bytes
/// rather than Base64.
/// </remarks>
public interface ITextSerializer : ISerializer;
