// Copyright (c) Mahmoud Shaheen. All rights reserved.

using tusdotnet.Models;

namespace Headless.Tus.Internal;

/// <summary>Chooses an upload's part size and the length limits that follow from it.</summary>
/// <remarks>
/// S3 and R2 allow at most 10,000 parts per multipart upload, and R2 requires every part except the last to have the
/// same size, so the part size is fixed when the upload is created. Part numbers are claimed in windows before parts
/// are uploaded, and a crash or a lost write fence wastes the rest of a window, so four windows stay in reserve and
/// are never counted toward the upload's length.
/// </remarks>
internal static class TusS3PartSize
{
    /// <summary>The most parts one multipart upload can hold on S3 and R2.</summary>
    public const int MaxPartCount = 10_000;

    /// <summary>How many claim windows of part numbers stay in reserve for crash recovery.</summary>
    public const int ReservedClaimWindows = 4;

    private const long _MiB = 1024 * 1024;

    /// <summary>The number of parts an upload's length may span.</summary>
    public static int UsablePartCount(int partClaimWindow)
    {
        return MaxPartCount - (ReservedClaimWindows * partClaimWindow);
    }

    /// <summary>
    /// Returns the part size for a new upload: <see cref="TusS3StoreOptions.PartSize"/> for a deferred length or one
    /// that fits the part budget at that size, otherwise the length divided by the budget, rounded up to a whole MiB.
    /// </summary>
    /// <exception cref="TusStoreException">The length needs parts larger than <see cref="TusS3StoreOptions.MaxPartSize"/>.</exception>
    public static int ForUpload(long? uploadLength, TusS3StoreOptions options)
    {
        if (uploadLength is not { } length)
        {
            return options.PartSize;
        }

        var budget = UsablePartCount(options.PartClaimWindow);
        // Written without "length + budget - 1" so a length near long.MaxValue cannot overflow.
        var needed = (length / budget) + (length % budget == 0 ? 0 : 1);
        var rounded = (needed / _MiB * _MiB) + (needed % _MiB == 0 ? 0 : _MiB);
        var partSize = Math.Max(options.PartSize, rounded);

        if (partSize > options.MaxPartSize)
        {
            throw new TusStoreException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Upload length {length} is too large: the store supports at most {MaxKnownLength(options)} bytes ({budget} parts of at most {options.MaxPartSize} bytes)."
                )
            );
        }

        return (int)partSize;
    }

    /// <summary>The largest upload length accepted at creation.</summary>
    public static long MaxKnownLength(TusS3StoreOptions options)
    {
        return (long)UsablePartCount(options.PartClaimWindow) * options.MaxPartSize;
    }

    /// <summary>The largest length an upload created without one can declare, given its part size.</summary>
    public static long DeferredLengthCap(int partSize, int partClaimWindow)
    {
        return (long)UsablePartCount(partClaimWindow) * partSize;
    }
}
