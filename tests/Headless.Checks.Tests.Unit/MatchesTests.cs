// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using AwesomeAssertions.Extensions;
using Headless.Checks;

namespace Tests;

public sealed class MatchesTests
{
    [Fact]
    public void should_return_argument_when_matches_valid_pattern()
    {
        // given
        const string argument = "Sleem123";
        var pattern = new Regex("^[a-zA-Z0-9]+$", RegexOptions.None, 1.Seconds());

        // when & then
        Argument.Matches(argument, pattern);
    }

    [Fact]
    public void should_throw_when_matches_argument_does_not_match_pattern()
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
    public void should_return_argument_when_matches_pattern_is_complex()
    {
        // given
        const string argument = "user@example.com";
        var pattern = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.None, 1.Seconds());

        // when & then
        Argument.Matches(argument, pattern);
    }

    [Fact]
    public void should_work_with_case_insensitive_patterns_when_matches()
    {
        // given
        const string argument = "Hello";
        var pattern = new Regex("^[a-z]+$", RegexOptions.IgnoreCase, 1.Seconds());

        // when & then
        Argument.Matches(argument, pattern);
    }

    [Fact]
    public void should_throw_argument_null_exception_with_correct_param_name_when_matches_argument_is_null()
    {
        // given
        const string? value = null;
        var pattern = new Regex("^[a-z]+$", RegexOptions.None, 1.Seconds());

        // when
        Action action = () => Argument.Matches(value!, pattern);

        // then
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(value));
    }
}
