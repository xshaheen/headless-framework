// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
}
