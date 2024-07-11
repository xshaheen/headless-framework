#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.IO;

/// <summary>Provides a set of extension methods for operations on <see cref="Stream"/>.</summary>
[PublicAPI]
public static class StreamExtensions
{
    [MustUseReturnValue]
    public static byte[] GetAllBytes(this Stream stream)
    {
        using var memoryStream = new MemoryStream();
        stream.Position = 0;
        stream.CopyTo(memoryStream);

        return memoryStream.ToArray();
    }

    [MustUseReturnValue]
    public static async Task<byte[]> GetAllBytesAsync(this Stream stream, CancellationToken token = default)
    {
        await using var memoryStream = new MemoryStream();
        stream.Position = 0;
        await stream.CopyToAsync(memoryStream, token);

        return memoryStream.ToArray();
    }
}
