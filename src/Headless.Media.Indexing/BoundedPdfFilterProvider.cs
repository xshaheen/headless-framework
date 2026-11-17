// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Headless.Media.Indexing;

/// <summary>
/// PdfPig's default filters, with every Flate stream refused when it would inflate past a size limit.
/// </summary>
/// <remarks>
/// PdfPig inflates each Flate stream fully into memory and exposes no size limit, so a few kilobytes of a crafted
/// content stream can inflate to gigabytes. PdfPig resolves every filter through
/// <see cref="UglyToad.PdfPig.ParsingOptions.FilterProvider"/>, which makes this provider the one place to bound it.
/// </remarks>
internal sealed class BoundedPdfFilterProvider(long maxDecodedBytes) : IFilterProvider
{
    private static readonly IFilterProvider _Default = DefaultFilterProvider.Instance;

    private readonly BoundedFlateFilter _flate = new(maxDecodedBytes);

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
            bounded[i] = filters[i] is FlateFilter ? _flate : filters[i];
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
                throw new InvalidDataException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"A Flate-encoded PDF stream inflates past {maxDecodedBytes} bytes, the most the PDF text provider decodes from one stream."
                    )
                );
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

        private static MemoryStream _ReadOnlyStream(Memory<byte> memory)
        {
            // PdfPig hands filters array-backed memory; the copy covers any other backing without changing the result.
            return MemoryMarshal.TryGetArray<byte>(memory, out var segment)
                ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
                : new MemoryStream(memory.ToArray(), writable: false);
        }
    }
}
