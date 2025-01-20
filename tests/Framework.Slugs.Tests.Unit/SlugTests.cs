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
}
