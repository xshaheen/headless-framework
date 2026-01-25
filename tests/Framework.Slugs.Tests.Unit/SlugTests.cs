// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Unicode;
using Framework.Slugs;

namespace Tests;

public sealed class SlugTests
{
    // URL: scheme:[//host[:port]]path[?query1=a&query2=a+b][#fragment]
    // Separate: -, ., _, ~
    // Reserved: ?, /, #, :, +, &, =

    [Theory]
    [InlineData(".NET Developer Needed!?", "dot-net-developer-needed")]
    [InlineData("Developer ~ Needed", "developer-needed")]
    [InlineData("Developer _ needed", "developer-needed")]
    [InlineData("UI&UX Designer: Needed", "ui-and-ux-designer-needed")]
    [InlineData("  Freelance Back-End Developer (WordPress) ", "freelance-back-end-developer-wordpress")]
    [InlineData("--Freelance Back-End Developer (WordPress)", "freelance-back-end-developer-wordpress")]
    [InlineData("CTO/Team lead Full time - Remote", "cto-team-lead-full-time-remote")]
    [InlineData(" رسم كاريكاتير	", "رسم-كاريكاتير")]
    [InlineData("project using C#", "project-using-c")]
    [InlineData("project using C++", "project-using-c-plus-plus")]
    [InlineData("3D & Autocad _Jenaan-Alwadi", "3d-and-autocad-jenaan-alwadi")]
    [InlineData("3D & Autocad = Jenaan-alwadi", "3d-and-autocad-jenaan-alwadi")]
    [InlineData("crème brûlée", "creme-brulee")]
    public void should_generate_urls_as_expected(string name, string expected)
    {
        var options = new SlugOptions
        {
            MaximumLength = 0,
            CasingTransformation = CasingTransformation.ToLowerCase,
            Separator = "-",
            Culture = null,
            CanEndWithSeparator = false,
        };

        var perma = Slug.Create(name, options);
        perma.Should().NotBeNullOrWhiteSpace();
        perma.Should().Be(expected);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("z", "z")]
    [InlineData("A", "A")]
    [InlineData("Z", "Z")]
    [InlineData("0", "0")]
    [InlineData("9", "9")]
    [InlineData("test", "test")]
    [InlineData("TeSt", "TeSt")]
    [InlineData("teste\u0301", "teste")]
    [InlineData("TeSt test", "TeSt-test")]
    [InlineData("TeSt test ", "TeSt-test")]
    [InlineData("TeSt:test ", "TeSt-test")]
    public void should_keep_case(string text, string expected)
    {
        var options = new SlugOptions { CasingTransformation = CasingTransformation.PreserveCase, Separator = "-" };

        var slug = Slug.Create(text, options);
        slug.Should().Be(expected);
    }

    [Fact]
    public void should_return_null_when_input_is_null()
    {
        Slug.Create(null).Should().BeNull();
    }

    [Fact]
    public void should_return_empty_when_input_is_empty()
    {
        Slug.Create("").Should().BeEmpty();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void should_return_empty_when_input_is_whitespace_only(string input)
    {
        Slug.Create(input).Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, "hello", "h")]
    [InlineData(5, "hello-world", "hello")]
    [InlineData(80, "short", "short")]
    public void should_respect_maximum_length(int max, string input, string expected)
    {
        var options = new SlugOptions { MaximumLength = max };
        Slug.Create(input, options).Should().Be(expected);
    }

    [Fact]
    public void should_allow_trailing_separator_when_configured()
    {
        var options = new SlugOptions { CanEndWithSeparator = true };
        Slug.Create("hello-", options).Should().EndWith("-");
    }

    [Fact]
    public void should_throw_when_separator_is_null()
    {
        var act = () => new SlugOptions { Separator = null! };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_throw_when_separator_is_empty()
    {
        var act = () => new SlugOptions { Separator = "" };
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_use_custom_separator()
    {
        var options = new SlugOptions { Separator = "_" };
        Slug.Create("hello world", options).Should().Be("hello_world");
    }

    [Fact]
    public void should_apply_uppercase_transformation()
    {
        var options = new SlugOptions { CasingTransformation = CasingTransformation.ToUpperCase };
        Slug.Create("hello", options).Should().Be("HELLO");
    }

    [Fact]
    public void should_filter_out_emoji()
    {
        Slug.Create("test 🎉 emoji").Should().Be("test-emoji");
    }

    [Fact]
    public void should_filter_out_surrogate_pairs()
    {
        // Mathematical bold A (U+1D400)
        Slug.Create("test \U0001D400 math").Should().Be("test-math");
    }

    #region Slug.Create - Edge Cases

    [Fact]
    public void should_use_default_options_when_null()
    {
        Slug.Create("Hello World", options: null).Should().Be("hello-world");
    }

    [Fact]
    public void should_handle_very_long_input()
    {
        var longInput = new string('a', 1000);
        var result = Slug.Create(longInput);

        result.Should().HaveLength(SlugOptions.DefaultMaximumLength);
        result.Should().Be(new string('a', 80));
    }

    [Theory]
    [InlineData("hello---world", "hello-world")]
    [InlineData("hello------world", "hello-world")]
    public void should_handle_consecutive_separators_in_input(string input, string expected)
    {
        Slug.Create(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("---hello", "hello")]
    [InlineData("   hello", "hello")]
    [InlineData("-hello", "hello")]
    public void should_not_start_with_separator(string input, string expected)
    {
        Slug.Create(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("!@#$%^*()", "percent")]
    [InlineData("!@#^*()", "")]
    public void should_handle_only_special_characters(string input, string expected)
    {
        // % gets replaced with " percent " by default replacements
        var options = new SlugOptions { MaximumLength = 0 };
        Slug.Create(input, options).Should().Be(expected);
    }

    [Fact]
    public void should_handle_mixed_unicode_scripts()
    {
        var options = new SlugOptions { MaximumLength = 0 };
        Slug.Create("hello مرحبا world", options).Should().Be("hello-مرحبا-world");
    }

    #endregion

    #region SlugOptions - AllowedRanges

    [Fact]
    public void should_allow_all_characters_when_ranges_empty()
    {
        var options = new SlugOptions { AllowedRanges = [], MaximumLength = 0 };

        // With empty ranges, all chars including special should pass through
        Slug.Create("hello!world", options).Should().Be("hello!world");
    }

    [Fact]
    public void should_filter_by_custom_unicode_range()
    {
        var options = new SlugOptions
        {
            AllowedRanges = [UnicodeRange.Create('a', 'f')], // only a-f allowed
            MaximumLength = 0,
        };

        Slug.Create("abcdefghij", options).Should().Be("abcdef");
    }

    [Fact]
    public void should_allow_arabic_characters_by_default()
    {
        // Arabic range U+0620-U+064A is in default AllowedRanges
        Slug.Create("مرحبا").Should().Be("مرحبا");
    }

    [Theory]
    [InlineData("test123", "test123")]
    [InlineData("0123456789", "0123456789")]
    public void should_allow_digits_by_default(string input, string expected)
    {
        Slug.Create(input).Should().Be(expected);
    }

    #endregion

    #region SlugOptions - Culture

    [Fact]
    public void should_use_culture_for_lowercase()
    {
        // Turkish: uppercase I becomes dotless ı (U+0131) in lowercase
        var options = new SlugOptions
        {
            Culture = new CultureInfo("tr-TR"),
            CasingTransformation = CasingTransformation.ToLowerCase,
        };

        Slug.Create("HI", options).Should().Be("hı");
    }

    [Fact]
    public void should_use_culture_for_uppercase()
    {
        // Turkish: lowercase i becomes İ (U+0130) in uppercase
        var options = new SlugOptions
        {
            Culture = new CultureInfo("tr-TR"),
            CasingTransformation = CasingTransformation.ToUpperCase,
        };

        Slug.Create("hi", options).Should().Be("Hİ");
    }

    [Fact]
    public void should_use_invariant_when_culture_null()
    {
        // With null culture, I becomes regular lowercase i
        var options = new SlugOptions { Culture = null, CasingTransformation = CasingTransformation.ToLowerCase };

        Slug.Create("HI", options).Should().Be("hi");
    }

    #endregion

    #region SlugOptions - Replacements

    [Theory]
    [InlineData("rock & roll", "rock-and-roll")]
    [InlineData("C++", "c-plus-plus")]
    [InlineData(".NET", "dot-net")]
    [InlineData("100%", "100-percent")]
    public void should_apply_default_replacements(string input, string expected)
    {
        var options = new SlugOptions { MaximumLength = 0 };
        Slug.Create(input, options).Should().Be(expected);
    }

    [Fact]
    public void should_apply_custom_replacements()
    {
        var options = new SlugOptions
        {
            Replacements = new Dictionary<string, string> { { "@", " at " } }.ToFrozenDictionary(),
            MaximumLength = 0,
        };

        Slug.Create("user@domain", options).Should().Be("user-at-domain");
    }

    [Fact]
    public void should_handle_empty_replacements()
    {
        var options = new SlugOptions { Replacements = FrozenDictionary<string, string>.Empty, MaximumLength = 0 };

        // & won't be replaced, will be filtered as disallowed character
        Slug.Create("rock & roll", options).Should().Be("rock-roll");
    }

    [Fact]
    public void should_apply_replacements_before_filtering()
    {
        // Replacements happen first, then filtering by AllowedRanges
        var options = new SlugOptions
        {
            Replacements = new Dictionary<string, string> { { "&", "AND" } }.ToFrozenDictionary(),
            MaximumLength = 0,
        };

        // & replaced with AND, then lowercased
        Slug.Create("A&B", options).Should().Be("aandb");
    }

    #endregion

    #region SlugOptions - MaximumLength Edge Cases

    [Fact]
    public void should_not_truncate_when_max_is_zero()
    {
        var options = new SlugOptions { MaximumLength = 0 };
        Slug.Create("hello world this is a long string", options).Should().Be("hello-world-this-is-a-long-string");
    }

    [Fact]
    public void should_truncate_to_exact_max()
    {
        var options = new SlugOptions { MaximumLength = 5 };
        Slug.Create("hello-world", options).Should().Be("hello");
    }

    [Fact]
    public void should_remove_trailing_separator_after_truncation()
    {
        // "hello-world" truncated to 6 chars = "hello-", then trailing "-" removed
        var options = new SlugOptions { MaximumLength = 6 };
        Slug.Create("hello world", options).Should().Be("hello");
    }

    #endregion

    #region SlugOptions - IsAllowed Method

    [Fact]
    public void should_return_true_for_allowed_rune()
    {
        var options = new SlugOptions();
        options.IsAllowed(new Rune('a')).Should().BeTrue();
        options.IsAllowed(new Rune('Z')).Should().BeTrue();
        options.IsAllowed(new Rune('5')).Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_disallowed_rune()
    {
        var options = new SlugOptions();
        options.IsAllowed(new Rune('!')).Should().BeFalse();
        options.IsAllowed(new Rune('@')).Should().BeFalse();
        options.IsAllowed(new Rune(' ')).Should().BeFalse();
    }

    [Fact]
    public void should_handle_boundary_values()
    {
        var options = new SlugOptions { AllowedRanges = [UnicodeRange.Create('a', 'z')] };

        // Boundary: first char 'a' and last char 'z' should be allowed
        options.IsAllowed(new Rune('a')).Should().BeTrue();
        options.IsAllowed(new Rune('z')).Should().BeTrue();

        // Just outside: '`' (before 'a') and '{' (after 'z') should be disallowed
        options.IsAllowed(new Rune('`')).Should().BeFalse();
        options.IsAllowed(new Rune('{')).Should().BeFalse();
    }

    #endregion

    #region SlugOptions - Multi-char Separator

    [Fact]
    public void should_support_multi_char_separator()
    {
        var options = new SlugOptions { Separator = "__" };
        Slug.Create("hello world", options).Should().Be("hello__world");
    }

    [Fact]
    public void should_not_duplicate_multi_char_separator()
    {
        var options = new SlugOptions { Separator = "__", MaximumLength = 0 };
        // Multiple spaces should not result in "____"
        Slug.Create("hello   world", options).Should().Be("hello__world");
    }

    #endregion
}
