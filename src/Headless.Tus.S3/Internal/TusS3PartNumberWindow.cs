// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Internal;

/// <summary>
/// A run of part numbers one request reserved with a single state write, handed out in order as its parts fill.
/// </summary>
/// <remarks>
/// Reserving numbers before any part is uploaded is what keeps a stale writer from re-uploading a part number another
/// request already used: its reservation fails on the state's ETag first. Reserving a run instead of one number per part
/// keeps the state writes per request to one for every window's worth of parts.
/// </remarks>
internal sealed class TusS3PartNumberWindow
{
    private int _next;

    private TusS3PartNumberWindow(int first, int end)
    {
        First = first;
        End = end;
        _next = first;
    }

    /// <summary>The first number of the run.</summary>
    public int First { get; }

    /// <summary>The number just past the run, which the state's counter records once the run is reserved.</summary>
    public int End { get; }

    /// <summary>
    /// Reserves up to <paramref name="windowSize"/> numbers starting at <paramref name="nextPartNumber"/>, never past
    /// the provider's last part number.
    /// </summary>
    /// <returns><see langword="false"/> when every part number of the multipart upload is already used</returns>
    public static bool TryReserve(
        int nextPartNumber,
        int windowSize,
        [NotNullWhen(true)] out TusS3PartNumberWindow? window
    )
    {
        if (nextPartNumber is < 1 or > TusS3PartSize.MaxPartCount)
        {
            window = null;

            return false;
        }

        var end = (int)Math.Min((long)nextPartNumber + windowSize, TusS3PartSize.MaxPartCount + 1L);
        window = new TusS3PartNumberWindow(nextPartNumber, end);

        return true;
    }

    /// <summary>Hands out the next unused number of the run.</summary>
    /// <returns><see langword="false"/> when the run is used up and the next part needs a new reservation</returns>
    public bool TryTake(out int partNumber)
    {
        if (_next >= End)
        {
            partNumber = 0;

            return false;
        }

        partNumber = _next++;

        return true;
    }
}
