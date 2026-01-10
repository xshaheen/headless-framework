// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace Framework.Caching;

/// <summary>
/// Adapts the framework's <see cref="ISerializer"/> to FusionCache's <see cref="IFusionCacheSerializer"/>.
/// </summary>
public sealed class FusionCacheSerializerAdapter(ISerializer serializer) : IFusionCacheSerializer
{
    public byte[] Serialize<T>(T? obj)
    {
        using var stream = new MemoryStream();
        serializer.Serialize(obj, stream);
        return stream.ToArray();
    }

    public T? Deserialize<T>(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return serializer.Deserialize<T>(stream);
    }

    public ValueTask<byte[]> SerializeAsync<T>(T? obj, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<byte[]>(Serialize(obj));
    }

    public ValueTask<T?> DeserializeAsync<T>(byte[] data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<T?>(Deserialize<T>(data));
    }
}
