// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Checks;

namespace Headless.Tus.Internal;

/// <summary>
/// Splits a final concatenated upload into the parts of its multipart upload, and says where each part's bytes come
/// from in the partial uploads.
/// </summary>
/// <remarks>
/// Every part except the last is exactly the part size, because R2 rejects a multipart upload whose non-final parts
/// differ in size; the partials' own boundaries therefore cannot decide where a part ends. A part whose range lies
/// inside one partial can be copied by the provider, while a part that spans partials has to be assembled by the
/// store.
/// </remarks>
internal static class TusS3ConcatPlan
{
    /// <summary>Plans the parts of a final upload built from partials of <paramref name="partialLengths"/>, in order.</summary>
    /// <param name="partialLengths">each partial upload's length, in the order the final upload lists them</param>
    /// <param name="partSize">the final upload's part size</param>
    /// <returns>the parts in ascending part-number order; empty when every partial is empty</returns>
    public static IReadOnlyList<TusS3ConcatPart> Create(IReadOnlyList<long> partialLengths, long partSize)
    {
        Argument.IsNotNull(partialLengths);
        Argument.IsPositive(partSize);

        var parts = new List<TusS3ConcatPart>();
        var sources = new List<TusS3ConcatSource>();
        var filled = 0L;

        for (var index = 0; index < partialLengths.Count; index++)
        {
            var length = partialLengths[index];
            Argument.IsPositiveOrZero(length);
            var offset = 0L;

            while (offset < length)
            {
                var take = Math.Min(partSize - filled, length - offset);
                sources.Add(new TusS3ConcatSource(index, offset, take));
                offset += take;
                filled += take;

                if (filled == partSize)
                {
                    parts.Add(new TusS3ConcatPart(parts.Count + 1, filled, [.. sources]));
                    sources.Clear();
                    filled = 0;
                }
            }
        }

        if (filled > 0)
        {
            parts.Add(new TusS3ConcatPart(parts.Count + 1, filled, [.. sources]));
        }

        return parts;
    }
}

/// <summary>One part of a final upload's multipart upload.</summary>
/// <param name="Number">the part number, from 1</param>
/// <param name="Length">the part's size in bytes</param>
/// <param name="Sources">the partial ranges whose bytes form the part, in order</param>
internal sealed record TusS3ConcatPart(int Number, long Length, IReadOnlyList<TusS3ConcatSource> Sources)
{
    /// <summary>Whether the whole part lies inside one partial, so the provider can copy it without the store reading it.</summary>
    public bool IsCopy => Sources.Count == 1;
}

/// <summary>A byte range of one partial upload's finished object.</summary>
/// <param name="PartialIndex">the partial's position in the final upload's list</param>
/// <param name="Offset">the first byte of the range within the partial</param>
/// <param name="Length">the range's length in bytes</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TusS3ConcatSource(int PartialIndex, long Offset, long Length);
