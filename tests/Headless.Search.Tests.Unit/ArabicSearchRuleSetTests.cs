// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Search;
using Headless.Testing.Tests;

namespace Tests;

public sealed class ArabicSearchRuleSetTests : TestBase
{
    private static readonly ArabicSearchRuleSet _Rules = ArabicSearchRuleSet.V1;

    [Fact]
    public void should_give_each_character_at_most_one_rule()
    {
        char[] all =
        [
            .. _Rules.OutputAlphabet,
            .. _Rules.Mappings.Keys,
            .. _Rules.Expansions.Keys,
            .. _Rules.Deletions,
        ];

        all.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void should_map_and_expand_only_into_the_output_alphabet()
    {
        var alphabet = _Rules.OutputAlphabet.ToHashSet();

        _Rules.Mappings.Values.Should().OnlyContain(c => alphabet.Contains(c));
        _Rules.Expansions.Values.SelectMany(static s => s).Should().OnlyContain(c => alphabet.Contains(c));
    }

    [Fact]
    public void should_carry_no_rule_for_a_surrogate_a_control_character_or_space()
    {
        char[] sources = [.. _Rules.Mappings.Keys, .. _Rules.Expansions.Keys, .. _Rules.Deletions];

        sources.Should().NotContain(static c => char.IsSurrogate(c) || c <= ' ');
        _Rules.OutputAlphabet.Should().NotContain(' ');
    }

    [Fact]
    public void should_list_the_alphabet_and_deletions_in_code_point_order()
    {
        _Rules.OutputAlphabet.Should().BeInAscendingOrder();
        _Rules.Deletions.Should().BeInAscendingOrder();
    }

    [Fact]
    public void should_hold_ascii_digits_and_letters_and_the_29_base_arabic_letters()
    {
        _Rules
            .OutputAlphabet.Should()
            .Equal("0123456789abcdefghijklmnopqrstuvwxyzءابتثجحخدذرزسشصضطظعغفقكلمنهوي".ToCharArray());
    }

    [Fact]
    public void should_return_v1_when_asked_for_v1()
    {
        ArabicSearchRuleSet.For(ArabicSearchVersion.V1).Should().BeSameAs(ArabicSearchRuleSet.V1);
    }
}
