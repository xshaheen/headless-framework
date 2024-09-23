// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Blobs.FileSystem.Internals;

internal static class Helpers
{
    internal static async ValueTask<Stream?> CopyToMemoryStreamAndFlushAsync(
        this Stream? stream,
        CancellationToken token = default
    )
    {
        if (stream is null)
        {
            return null;
        }

        var memoryStream = new MemoryStream();

        await stream.CopyToAsync(memoryStream, token);
        memoryStream.Seek(0, SeekOrigin.Begin);

        await stream.FlushAsync(token);

        return memoryStream;
    }
}
