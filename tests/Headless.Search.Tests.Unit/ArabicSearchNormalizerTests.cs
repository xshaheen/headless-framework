// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Search;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ArabicSearchNormalizerTests : TestBase
{
    public static TheoryData<ArabicSearchCorpusRow> CorpusV1 => [.. ArabicSearchCorpus.V1];

    [Theory]
    [MemberData(nameof(CorpusV1))]
    public void should_build_the_hand_checked_key_when_normalizing_a_golden_corpus_row(ArabicSearchCorpusRow row)
    {
        ArabicSearchNormalizer.Normalize(row.Input, ArabicSearchVersion.V1).Should().Be(row.Expected);
    }

    [Fact]
    public void should_return_null_when_value_is_null()
    {
        ArabicSearchNormalizer.Normalize(null, ArabicSearchVersion.V1).Should().BeNull();
    }

    [Fact]
    public void should_return_empty_when_value_is_empty()
    {
        ArabicSearchNormalizer.Normalize(string.Empty, ArabicSearchVersion.V1).Should().BeEmpty();
    }

    [Fact]
    public void should_throw_when_version_is_not_defined()
    {
        var act = () => ArabicSearchNormalizer.Normalize("x", (ArabicSearchVersion)99);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("version");
    }

    // The old searchable-string helper read newline and tab as control characters and dropped them, gluing words.
    [Fact]
    public void should_separate_words_when_a_newline_or_tab_joins_them_unlike_the_old_helper()
    {
        ArabicSearchNormalizer.Normalize("محمد\nعلي\tحسن", ArabicSearchVersion.V1).Should().Be("محمد علي حسن");
    }

    // The old helper deleted hyphens and apostrophes, so "عبد-الله" and "O'Brien" became single words.
    [Fact]
    public void should_separate_words_when_a_hyphen_or_apostrophe_joins_them_unlike_the_old_helper()
    {
        ArabicSearchNormalizer.Normalize("عبد-الله O'Brien", ArabicSearchVersion.V1).Should().Be("عبد الله o brien");
    }

    // The old helper folded thal into dal, which merges different names (منذر and مندر).
    [Fact]
    public void should_keep_thal_distinct_from_dal_unlike_the_old_helper()
    {
        ArabicSearchNormalizer.Normalize("منذر", ArabicSearchVersion.V1).Should().Be("منذر");
    }

    [Fact]
    public void should_return_its_own_output_when_normalizing_a_key_again()
    {
        foreach (var input in _Inputs())
        {
            var key = ArabicSearchNormalizer.Normalize(input, ArabicSearchVersion.V1);

            ArabicSearchNormalizer.Normalize(key, ArabicSearchVersion.V1).Should().Be(key, "input {0}", _Escape(input));
        }
    }

    [Fact]
    public void should_emit_only_alphabet_characters_and_single_inner_spaces_for_any_input()
    {
        var alphabet = ArabicSearchRuleSet.V1.OutputAlphabet.ToHashSet();

        foreach (var input in _Inputs())
        {
            var key = ArabicSearchNormalizer.Normalize(input, ArabicSearchVersion.V1);

            key.Should().NotStartWith(" ").And.NotEndWith(" ").And.NotContain("  ", "input {0}", _Escape(input));
            key.Where(c => c != ' ').Should().OnlyContain(c => alphabet.Contains(c), "input {0}", _Escape(input));
        }
    }

    [Theory]
    [InlineData("أ")] // alef with hamza above
    [InlineData("إ")] // alef with hamza below
    [InlineData("آ")] // alef with madda
    [InlineData("ؤ")] // waw with hamza
    [InlineData("ئ")] // yeh with hamza
    [InlineData("ۀ")] // heh with yeh above
    [InlineData("ۂ")] // heh goal with hamza
    [InlineData("ۓ")] // yeh barree with hamza
    [InlineData("é")] // e with acute
    public void should_build_one_key_when_a_letter_is_composed_or_decomposed(string composed)
    {
        var decomposed = composed.Normalize(System.Text.NormalizationForm.FormD);
        decomposed.Should().NotBe(composed, "the letter has a canonical decomposition");

        ArabicSearchNormalizer
            .Normalize(decomposed, ArabicSearchVersion.V1)
            .Should()
            .Be(ArabicSearchNormalizer.Normalize(composed, ArabicSearchVersion.V1));
    }

    private static IEnumerable<string> _Inputs()
    {
        return ArabicSearchCorpus
            .EveryBmpCharacter()
            .Concat(ArabicSearchCorpus.SupplementaryCharacters())
            .Concat(ArabicSearchCorpus.Random(10_000, seed: 20261029))
            .Concat(ArabicSearchCorpus.V1.Select(static r => r.Input));
    }

    private static string _Escape(string value) =>
        string.Concat(value.Select(static c => c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}"));
}
