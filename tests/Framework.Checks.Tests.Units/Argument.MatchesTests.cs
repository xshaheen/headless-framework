// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Framework.Checks;

namespace Tests;

public class ArgumentMatchesTests
{
    [Fact]
    public void matches_should_return_argument_when_valid_pattern()
    {
        // given
        string argument = "Sleem123";
        var pattern = new Regex(@"^[a-zA-Z0-9]+$");

        // when & then
        Argument.Matches(argument, pattern);
    }

    [Fact]
    public void matches_should_throw_when_argument_does_not_match_pattern()
    {
        // given
        string argument = "sleem@123";
        var pattern = new Regex(@"^[a-zA-Z0-9]+$");

        // when & then
        Assert.Throws<ArgumentException>(() => Argument.Matches(argument, pattern));
    }

    [Fact]
    public void matches_should_return_argument_when_pattern_is_complex()
    {
        // given
        string argument = "user@example.com";
        var pattern = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        // when & then
        Argument.Matches(argument, pattern);
    }

    [Fact]
    public void matches_should_work_with_case_insensitive_patterns()
    {
        // given
        string argument = "Hello";
        var pattern = new Regex(@"^[a-z]+$", RegexOptions.IgnoreCase);

        // when & then
        Argument.Matches(argument, pattern);
    }
}
