// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging.Internal;

/// <summary>Buffers a stream that cannot seek, so every contributor in a chain can read it from the start.</summary>
internal static class SeekableBuffer
{
    // Below the 85,000-byte large-object-heap threshold, so the chunks die young in generation 0.
    private const int _ChunkSize = 80 * 1024;

    /// <summary>
    /// Reads <paramref name="stream" /> to its end into one exact-size, read-only <see cref="MemoryStream" />.
    /// </summary>
    /// <remarks>
    /// A plain <c>CopyToAsync</c> into a growing <see cref="MemoryStream" /> doubles its array as it goes, allocating
    /// about twice the input on the large-object heap. Reading into small chunks and copying them once into an
    /// exact-size array allocates the input once there. The result is a <see cref="MemoryStream" /> on purpose: a
    /// provider can recognise it as safe for synchronous reads and read it without another copy.
    /// </remarks>
    public static async Task<MemoryStream> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var chunks = new List<byte[]>();
        var lastLength = 0;
        long total = 0;

        while (true)
        {
            var chunk = new byte[_ChunkSize];
            var filled = 0;

            while (filled < chunk.Length)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(filled), cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                filled += read;
            }

            if (filled > 0)
            {
                chunks.Add(chunk);
                lastLength = filled;
                total += filled;
            }

            if (filled < chunk.Length)
            {
                break;
            }
        }

        // A small input fits one chunk, which becomes the buffer itself.
        if (chunks.Count == 1)
        {
            return new MemoryStream(chunks[0], 0, lastLength, writable: false, publiclyVisible: true);
        }

        var buffer = new byte[total];
        var offset = 0;

        for (var i = 0; i < chunks.Count; i++)
        {
            var length = i == chunks.Count - 1 ? lastLength : _ChunkSize;
            chunks[i].AsSpan(0, length).CopyTo(buffer.AsSpan(offset));
            offset += length;
        }

        return new MemoryStream(buffer, writable: false);
    }
}
