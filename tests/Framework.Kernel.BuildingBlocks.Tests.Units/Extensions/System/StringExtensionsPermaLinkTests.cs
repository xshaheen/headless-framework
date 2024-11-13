// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Extensions.System;

public sealed class StringExtensionsPermaLinkTests
{
    // URL: scheme:[//host[:port]]path[?query1=a&query2=a+b][#fragment]
    // Separate: -, ., _, ~
    // Reserved: ?, /, #, :, +, &, =

    [Theory]
    [InlineData("F13D1B0F57", ".NET Developer Needed!?", ".NET-Developer-Needed-F13D1B0F57")]
    [InlineData("F13D1B0F57", "Developer ~ Needed", "Developer-Needed-F13D1B0F57")]
    [InlineData("F13D1B0F57", "Developer _ needed", "Developer-Needed-F13D1B0F57")]
    [InlineData("F13D1B0F57", "UI&UX Designer: Needed", "UI-And-UX-Designer-Needed-F13D1B0F57")]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        "  Freelance Back-End Developer (WordPress) ",
        "Freelance-Back-End-Developer-WordPress-F13D1B0F57244688BCF294B36BC32F5A"
    )]
    [InlineData(
        "F13D1B0F57",
        "--Freelance Back-End Developer (WordPress)",
        "Freelance-Back-End-Developer-WordPress-F13D1B0F57"
    )]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        "CTO/Team lead Full time - Remote",
        "CTO-Team-Lead-Full-Time-Remote-F13D1B0F57244688BCF294B36BC32F5A"
    )]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        " رسم كاريكاتير	",
        "رسم-كاريكاتير-F13D1B0F57244688BCF294B36BC32F5A"
    )]
    [InlineData("F13D1B0F57", "project using C#", "Project-Using-C-Sharp-F13D1B0F57")]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        "project using C++",
        "Project-Using-C-Plus-Plus-F13D1B0F57244688BCF294B36BC32F5A"
    )]
    [InlineData("F13D1B0F57", "3D & Autocad _Jenaan-Alwadi", "3D-And-Autocad-Jenaan-Alwadi-F13D1B0F57")]
    [InlineData(
        "F13D1B0F57244688BCF294B36BC32F5A",
        "3D & Autocad = Jenaan-alwadi",
        "3D-And-Autocad-Jenaan-Alwadi-F13D1B0F57244688BCF294B36BC32F5A"
    )]
    [InlineData("F13D1B0F57", "crème brûlée", "Creme-Brulee-F13D1B0F57")]
    public void should_generate_urls_as_expected(string id, string name, string expected)
    {
        // - act

        var perma = name.PermaLink(id);

        // - assert

        perma.Should().NotBeNullOrWhiteSpace();
        perma.Should().Be(expected);
    }
}
