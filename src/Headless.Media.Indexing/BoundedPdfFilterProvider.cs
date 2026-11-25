// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Util;

namespace Headless.Media.Indexing;

/// <summary>
/// PdfPig's default filters, with every Flate or LZW stream refused when it would decode past a size limit.
/// </summary>
/// <remarks>
/// PdfPig decodes each Flate and LZW stream fully into memory and exposes no size limit, so a few kilobytes of a
/// crafted content stream can expand to gigabytes. PdfPig resolves every filter through
/// <see cref="UglyToad.PdfPig.ParsingOptions.FilterProvider"/>, which makes this provider the one place to bound it.
/// </remarks>
internal sealed class BoundedPdfFilterProvider(long maxDecodedBytes) : IFilterProvider
{
    private static readonly IFilterProvider _Default = DefaultFilterProvider.Instance;

    private readonly BoundedFlateFilter _flate = new(maxDecodedBytes);
    private readonly BoundedLzwFilter _lzw = new(maxDecodedBytes);

    public static BoundedPdfFilterProvider Instance { get; } = new(MediaTextLimits.MaxDecodedPdfStreamBytes);

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary) => _Bound(_Default.GetFilters(dictionary));

    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names) =>
        _Bound(_Default.GetNamedFilters(names));

    public IReadOnlyList<IFilter> GetAllFilters() => _Bound(_Default.GetAllFilters());

    private IFilter[] _Bound(IReadOnlyList<IFilter> filters)
    {
        var bounded = new IFilter[filters.Count];

        for (var i = 0; i < filters.Count; i++)
        {
            bounded[i] = filters[i] switch
            {
                FlateFilter => _flate,
                LzwFilter => _lzw,
                var filter => filter,
            };
        }

        return bounded;
    }

    /// <summary>
    /// Inflates a Flate stream once into a discarding counter, refusing it past the limit, then lets PdfPig's own
    /// <see cref="FlateFilter"/> decode it, so predictors and PdfPig's handling of corrupt data stay unchanged.
    /// </summary>
    /// <remarks>
    /// The counting pass costs a second inflation of each Flate stream, bounded by the limit. Reimplementing the
    /// decode would avoid it, but PdfPig's PNG predictor is internal, so a copy would drift from PdfPig's behavior.
    /// </remarks>
    private sealed class BoundedFlateFilter(long maxDecodedBytes) : IFilter
    {
        private static readonly FlateFilter _Inner = new();

        public bool IsSupported => true;

        public Memory<byte> Decode(
            Memory<byte> input,
            DictionaryToken streamDictionary,
            IFilterProvider filterProvider,
            int filterIndex
        )
        {
            if (_InflatedLength(input, maxDecodedBytes) > maxDecodedBytes)
            {
                throw _TooLarge("Flate", maxDecodedBytes);
            }

            return _Inner.Decode(input, streamDictionary, filterProvider, filterIndex);
        }

        /// <summary>
        /// Counts the bytes <paramref name="input"/> inflates to, stopping once the count passes
        /// <paramref name="limit"/> or the data turns corrupt.
        /// </summary>
        private static long _InflatedLength(Memory<byte> input, long limit)
        {
            // FlateFilter skips the two-byte zlib header and reads raw deflate, ignoring the Adler-32 trailer. Count the
            // same way, so a stream PdfPig accepts is measured exactly as PdfPig will inflate it.
            if (input.Length < 2)
            {
                return 0;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(81920);

            try
            {
                using var compressed = _ReadOnlyStream(input[2..]);
                using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
                long total = 0;
                int read;

                try
                {
                    while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;

                        if (total > limit)
                        {
                            return total;
                        }
                    }
                }
                catch (InvalidDataException)
                {
                    // Corrupt data ends inflation here; FlateFilter then keeps its own fallback, and what it could
                    // inflate before the corruption is no more than what was counted.
                }

                return total;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static MemoryStream _ReadOnlyStream(ReadOnlyMemory<byte> memory)
        {
            // PdfPig hands filters array-backed memory; the copy covers any other backing without changing the result.
            return MemoryMarshal.TryGetArray(memory, out var segment)
                ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
                : new MemoryStream(memory.ToArray(), writable: false);
        }
    }

    /// <summary>
    /// Counts the bytes an LZW stream decodes to, refusing it past the limit, then lets PdfPig's own
    /// <see cref="LzwFilter"/> decode it, so predictors and PdfPig's handling of corrupt data stay unchanged.
    /// </summary>
    /// <remarks>
    /// The BCL ships no LZW decoder, and PdfPig decodes the whole stream in one call, so the count comes from
    /// <see cref="LzwDecodedLength"/>, which replays PdfPig's code table without producing any output.
    /// </remarks>
    private sealed class BoundedLzwFilter(long maxDecodedBytes) : IFilter
    {
        private static readonly LzwFilter _Inner = new();

        public bool IsSupported => true;

        public Memory<byte> Decode(
            Memory<byte> input,
            DictionaryToken streamDictionary,
            IFilterProvider filterProvider,
            int filterIndex
        )
        {
            // LzwFilter reads EarlyChange the same way, so both passes agree on when the code width grows.
            var parameters = DecodeParameterResolver.GetFilterParameters(streamDictionary, filterIndex);
            var isEarlyChange = parameters.GetIntOrDefault(NameToken.EarlyChange, 1) == 1;

            if (LzwDecodedLength(input.Span, isEarlyChange, maxDecodedBytes) > maxDecodedBytes)
            {
                throw _TooLarge("LZW", maxDecodedBytes);
            }

            return _Inner.Decode(input, streamDictionary, filterProvider, filterIndex);
        }
    }

    private const int _LzwClearTable = 256;
    private const int _LzwEndOfData = 257;
    private const int _LzwFirstFreeCode = 258;
    private const int _LzwMaxCodeWidth = 12;

    /// <summary>
    /// Counts the bytes PdfPig's <see cref="LzwFilter"/> writes for <paramref name="input"/> before its predictor,
    /// stopping once the count passes <paramref name="limit"/>, at the end-of-data code, or where PdfPig would throw.
    /// </summary>
    /// <remarks>
    /// Each table entry is an earlier entry plus one byte, so only entry lengths matter. This replays PdfPig 0.1.16's
    /// decoder: codes are read most significant bit first, widths run from 9 to 12 bits and switch one code early
    /// unless <paramref name="isEarlyChange"/> is <see langword="false"/>, and the table keeps growing past 4096
    /// entries without a clear code, though a 12-bit code reaches only the first 4096. A PNG or TIFF predictor never
    /// writes more bytes than it reads, so the pre-predictor count bounds what PdfPig holds.
    /// </remarks>
    internal static long LzwDecodedLength(ReadOnlySpan<byte> input, bool isEarlyChange, long limit)
    {
        const int addressable = 1 << _LzwMaxCodeWidth;

        var lengths = ArrayPool<int>.Shared.Rent(addressable);

        try
        {
            var earlyChangeOffset = isEarlyChange ? 0 : 1;
            var totalBits = input.Length * 8L;
            long bitPosition = 0;
            long tableSize = _LzwFirstFreeCode;
            var previous = -1;
            var width = 9;
            long total = 0;

            while (bitPosition + width <= totalBits)
            {
                var code = _ReadCode(input, bitPosition, width);
                bitPosition += width;

                if (code == _LzwClearTable)
                {
                    tableSize = _LzwFirstFreeCode;
                    previous = -1;
                    width = 9;

                    continue;
                }

                if (code == _LzwEndOfData)
                {
                    return total;
                }

                // PdfPig throws on a code that refers to an entry the table does not hold yet; its own decode then
                // fails the same way, after no more output than counted here.
                if (previous >= tableSize || (code >= tableSize && previous < 0))
                {
                    return total;
                }

                int length;

                if (code < tableSize)
                {
                    length = code < _LzwClearTable ? 1 : lengths[code];

                    if (previous >= 0)
                    {
                        _Add(lengths, ref tableSize, _Length(lengths, previous) + 1);
                    }
                }
                else
                {
                    // The code is not in the table yet: PdfPig decodes it as the previous entry plus its first byte.
                    length = _Length(lengths, previous) + 1;
                    _Add(lengths, ref tableSize, length);
                }

                total += length;

                if (total > limit)
                {
                    return total;
                }

                previous = code;
                width =
                    tableSize < 511 + earlyChangeOffset ? 9
                    : tableSize < 1023 + earlyChangeOffset ? 10
                    : tableSize < 2047 + earlyChangeOffset ? 11
                    : _LzwMaxCodeWidth;
            }

            // The input ended without an end-of-data code; PdfPig throws there, after the output counted so far.
            return total;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(lengths);
        }

        static int _Length(int[] lengths, int code) => code < _LzwClearTable ? 1 : lengths[code];

        static void _Add(int[] lengths, ref long tableSize, int length)
        {
            if (tableSize < addressable)
            {
                lengths[tableSize] = length;
            }

            tableSize++;
        }
    }

    private static int _ReadCode(ReadOnlySpan<byte> input, long bitPosition, int width)
    {
        // A code of at most 12 bits starting at any bit offset spans at most three bytes.
        var index = (int)(bitPosition >> 3);
        var shift = (int)(bitPosition & 7);
        var window = input[index] << 16;

        if (index + 1 < input.Length)
        {
            window |= input[index + 1] << 8;
        }

        if (index + 2 < input.Length)
        {
            window |= input[index + 2];
        }

        return (window >> (24 - shift - width)) & ((1 << width) - 1);
    }

    private static InvalidDataException _TooLarge(string filterName, long maxDecodedBytes)
    {
        return new InvalidDataException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"A {filterName}-encoded PDF stream decodes past {maxDecodedBytes} bytes, the most the PDF text provider decodes from one stream."
            )
        );
    }
}
