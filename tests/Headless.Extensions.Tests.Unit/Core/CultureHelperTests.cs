// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Tests.Core;

public sealed class CultureHelperTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    public void is_valid_culture_code_should_accept_known_cultures(string code)
    {
        // when / then
        CultureHelper.IsValidCultureCode(code).Should().BeTrue();
    }

    [Theory]
    [InlineData("xx-XX")]
    [InlineData("zz")]
    [InlineData("klingon")]
    [InlineData("en-ZZ")]
    public void is_valid_culture_code_should_reject_synthesized_cultures(string code)
    {
        // given - on ICU runtimes GetCultureInfo does not throw for well-formed-but-unknown tags; it
        // synthesizes a placeholder (UserCustomCulture) culture that must be treated as invalid.

        // when / then
        CultureHelper.IsValidCultureCode(code).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void is_valid_culture_code_should_reject_blank_input(string? code)
    {
        // when / then
        CultureHelper.IsValidCultureCode(code).Should().BeFalse();
    }
}
