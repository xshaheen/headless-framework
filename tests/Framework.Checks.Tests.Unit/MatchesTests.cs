// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using AwesomeAssertions.Extensions;
using Framework.Checks;

namespace Tests;

public sealed class MatchesTests
{
    [Fact]
    public void matches_should_return_argument_when_valid_pattern()
    {
        // given
        const string argument = "Sleem123";
        var pattern = new Regex("^[a-zA-Z0-9]+$", RegexOptions.None, 1.Seconds());

        // when & then
        Argument.Matches(argument, pattern);
    }

    [Fact]
    public void matches_should_throw_when_argument_does_not_match_pattern()
    {
        // given
        const string argument = "sleem@123";
        var pattern = new Regex("^[a-zA-Z0-9]+$", RegexOptions.None, 1.Seconds());

        // when
        Action action = () => Argument.Matches(argument, pattern);

        // then
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void matches_should_return_argument_when_pattern_is_complex()
    {
        // given
        const string argument = "user@example.com";
        var pattern = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, 1.Seconds());

        // when & then
        Argument.Matches(argument, pattern);
    }

    [Fact]
    public void matches_should_work_with_case_insensitive_patterns()
    {
        // given
        const string argument = "Hello";
        var pattern = new Regex("^[a-z]+$", RegexOptions.IgnoreCase, 1.Seconds());

        // when & then
        Argument.Matches(argument, pattern);
    }
}
