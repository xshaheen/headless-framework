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
    /// Re-encoding for resize or compress yields output of the source's order of magnitude at most, so seeding the
    /// capacity skips MemoryStream's doubling regrow-and-copy chain up from zero. This only sets the starting
    /// capacity — an output that does exceed the estimate still grows normally.
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
