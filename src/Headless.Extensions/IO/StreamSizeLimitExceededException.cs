// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.IO;

/// <summary>Thrown when a stream decorated with <see cref="SizeLimitedReadStream"/> reads beyond its configured limit.</summary>
/// <remarks>Initializes a new instance of the <see cref="StreamSizeLimitExceededException"/> class.</remarks>
/// <param name="maximumBytes">The maximum number of bytes the stream permits.</param>
/// <param name="bytesRead">The number of bytes observed when the limit was exceeded.</param>
[PublicAPI]
public sealed class StreamSizeLimitExceededException(long maximumBytes, long bytesRead)
    : IOException(_BuildMessage(maximumBytes, bytesRead))
{
    /// <summary>Gets the maximum number of bytes the stream permits.</summary>
    public long MaximumBytes { get; } = maximumBytes;

    /// <summary>Gets the number of bytes observed when the limit was exceeded.</summary>
    public long BytesRead { get; } = bytesRead;

    private static string _BuildMessage(long maximumBytes, long bytesRead)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"The stream exceeded its {maximumBytes:N0}-byte read limit after reading {bytesRead:N0} bytes."
        );
    }
}
