// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Imaging.ImageSharp.Internals;

internal static class EncodeBufferHelpers
{
    /// <summary>
    /// Ceiling on the pre-sized capacity, so a pathologically large source cannot turn into one outsized
    /// large-object-heap allocation before a single byte has been encoded.
    /// </summary>
    private const int _MaxPreSizedCapacity = 32 * 1024 * 1024;

    /// <summary>
    /// Creates the buffer an encode writes into, pre-sized from <paramref name="source"/> when its length is known.
    /// Intended for compression, where an output worth keeping is strictly smaller than the source, making the
    /// source length a tight upper bound that skips MemoryStream's doubling regrow-and-copy chain up from zero.
    /// Do not use it where the output is typically far smaller than the source (resize): the buffer keeps its full
    /// capacity for the returned stream's lifetime, so an oversized seed is a retained allocation, not a shortcut.
    /// This only sets the starting capacity — an output that does exceed the estimate still grows normally.
    /// </summary>
    /// <param name="source">The encoded source stream; its length is used only as an estimate.</param>
    public static MemoryStream CreateEncodeBuffer(Stream source)
    {
        if (!source.CanSeek)
        {
            return new MemoryStream();
        }

        var length = source.Length;

        return length is > 0 and <= _MaxPreSizedCapacity ? new MemoryStream((int)length) : new MemoryStream();
    }
}
