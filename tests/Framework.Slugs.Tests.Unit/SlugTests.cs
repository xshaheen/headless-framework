// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed class SlugTests
{
    // URL: scheme:[//host[:port]]path[?query1=a&query2=a+b][#fragment]
    // Separate: -, ., _, ~
    // Reserved: ?, /, #, :, +, &, =

    [Theory]
    [InlineData("F13D1B0F57", ".NET Developer Needed!?", "dot-net-developer-needed-f13d1b0f57")]
    [InlineData("F13D1B0F57", "Developer ~ Needed", "developer-needed-f13d1b0f57")]
    [InlineData("F13D1B0F57", "Developer _ needed", "developer-needed-f13d1b0f57")]
    [InlineData("F13D1B0F57", "UI&UX Designer: Needed", "ui-and-ux-designer-needed-f13d1b0f57")]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        "  Freelance Back-End Developer (WordPress) ",
        "freelance-back-end-developer-wordpress-f13d1b0f57244688bcf294b36bc32f5a"
    )]
    [InlineData(
        "F13D1B0F57",
        "--Freelance Back-End Developer (WordPress)",
        "freelance-back-end-developer-wordpress-f13d1b0f57"
    )]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        "CTO/Team lead Full time - Remote",
        "cto-team-lead-full-time-remote-f13d1b0f57244688bcf294b36bc32f5a"
    )]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        " رسم كاريكاتير	",
        "رسم-كاريكاتير-f13d1b0f57244688bcf294b36bc32f5a"
    )]
    [InlineData("F13D1B0F57", "project using C#", "project-using-c-f13d1b0f57")]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        "project using C++",
        "project-using-c-plus-plus-f13d1b0f57244688bcf294b36bc32f5a"
    )]
    [InlineData("F13D1B0F57", "3D & Autocad _Jenaan-Alwadi", "3d-and-autocad-jenaan-alwadi-f13d1b0f57")]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        "3D & Autocad = Jenaan-alwadi",
        "3d-and-autocad-jenaan-alwadi-f13d1b0f57244688bcf294b36bc32f5a"
    )]
    [InlineData("F13D1B0F57", "crème brûlée", "creme-brulee-f13d1b0f57")]
    public void should_generate_urls_as_expected(string id, string name, string expected)
    {
        var perma = Slug.Create(name, id);
        perma.Should().NotBeNullOrWhiteSpace();
        perma.Should().Be(expected);
    }
}
