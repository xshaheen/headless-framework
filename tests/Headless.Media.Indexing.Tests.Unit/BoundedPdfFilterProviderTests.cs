// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Media.Indexing;
using Headless.Testing.Tests;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace Tests;

public sealed class BoundedPdfFilterProviderTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_count_the_same_lzw_decoded_length_as_pdfpig_when_early_change_varies(bool isEarlyChange)
    {
        // given: two compressed runs separated by a clear code, long enough to fill the table and reach 12-bit codes
        var first = _Text(seed: 1, words: 40_000);
        var second = _Text(seed: 2, words: 2_000);
        int[] codes =
        [
            LzwTestEncoder.ClearTable,
            .. LzwTestEncoder.Compress(first),
            LzwTestEncoder.ClearTable,
            .. LzwTestEncoder.Compress(second),
            LzwTestEncoder.EndOfData,
        ];
        codes.Max().Should().BeGreaterThan(2047, "the stream must reach 12-bit codes");
        var encoded = LzwTestEncoder.Pack(codes, isEarlyChange);
        var expected = new LzwFilter()
            .Decode(encoded, _LzwDictionary(isEarlyChange), DefaultFilterProvider.Instance, 0)
            .Length;

        // when
        var counted = BoundedPdfFilterProvider.LzwDecodedLength(encoded, isEarlyChange, long.MaxValue);

        // then
        expected.Should().Be(first.Length + second.Length);
        counted.Should().Be(expected);
    }

    [Fact]
    public void should_stop_counting_an_lzw_stream_once_it_passes_the_limit()
    {
        // given: a stream that decodes to 1 GiB
        var encoded = LzwTestEncoder.Pack(LzwTestEncoder.Bomb((byte)' ', 1L << 30));

        // when
        var counted = BoundedPdfFilterProvider.LzwDecodedLength(encoded, isEarlyChange: true, limit: 1024 * 1024);

        // then: the count stops within one code of the limit instead of reaching the full size
        counted.Should().BeInRange((1024 * 1024) + 1, (1024 * 1024) + 4096);
    }

    [Fact]
    public void should_refuse_an_lzw_stream_past_the_provider_limit_before_pdfpig_decodes_it()
    {
        // given: a provider bounded at 1 MiB and a stream that decodes to 2 MiB
        var provider = new BoundedPdfFilterProvider(1024 * 1024);
        var dictionary = _LzwDictionary(isEarlyChange: true);
        var filter = provider.GetFilters(dictionary).Should().ContainSingle().Subject;
        var encoded = LzwTestEncoder.Pack(LzwTestEncoder.Bomb((byte)' ', 2L * 1024 * 1024));

        // when
        var act = () => filter.Decode(encoded, dictionary, provider, 0);

        // then
        act.Should().Throw<InvalidDataException>().WithMessage("*LZW*");
    }

    [Fact]
    public void should_leave_a_truncated_lzw_stream_to_fail_in_pdfpig()
    {
        // given: a stream cut off before its end-of-data code, which PdfPig rejects
        var encoded = LzwTestEncoder.Pack([LzwTestEncoder.ClearTable, .. LzwTestEncoder.Compress("abcabcabc"u8)]);
        var provider = new BoundedPdfFilterProvider(1024 * 1024);
        var dictionary = _LzwDictionary(isEarlyChange: true);
        var filter = provider.GetFilters(dictionary).Should().ContainSingle().Subject;

        // when
        var act = () => filter.Decode(encoded, dictionary, provider, 0);

        // then: PdfPig's own error, not the size limit
        act.Should().Throw<InvalidOperationException>();
    }

    private static DictionaryToken _LzwDictionary(bool isEarlyChange)
    {
        return new DictionaryToken(
            new Dictionary<NameToken, IToken>
            {
                [NameToken.Filter] = NameToken.LzwDecode,
                [NameToken.DecodeParms] = new DictionaryToken(
                    new Dictionary<NameToken, IToken>
                    {
                        [NameToken.EarlyChange] = new NumericToken(isEarlyChange ? 1 : 0),
                    }
                ),
            }
        );
    }

    private static byte[] _Text(int seed, int words)
    {
        string[] vocabulary =
        [
            "alpha",
            "beta",
            "gamma",
            "delta",
            "stream",
            "page",
            "text",
            "LZW",
            "(",
            ")",
            "Tj",
            "\n",
        ];
#pragma warning disable CA5394 // A seeded, non-cryptographic generator keeps the test input reproducible.
        var random = new Random(seed);
        var builder = new StringBuilder();

        for (var i = 0; i < words; i++)
        {
            builder.Append(vocabulary[random.Next(vocabulary.Length)]).Append(' ');
            builder.Append(random.Next(1000));
        }
#pragma warning restore CA5394

        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
