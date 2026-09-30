// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqlPortableTests : TestBase
{
    // Built in code: attribute arguments are stored as UTF-8, which would turn a lone surrogate into U+FFFD.
    private static readonly string[] _Unportable =
    [
        " lead",
        "trail ",
        "tab\t",
        " nbsp",
        "x\u0000y",
        "lone\ud800",
        "\udc00lone",
        "\udc00\ud800",
        "end\ud83d",
    ];

    private static readonly string[] _Portable =
    [
        "job",
        "Job",
        "a b",
        "caf\u00e9",
        "cafe\u0301",
        "r\u200b",
        "x\u0001y",
        "\ud83d\ude00",
        "",
    ];

    [Fact]
    public void should_name_why_each_unportable_key_is_refused()
    {
        foreach (var value in _Unportable)
        {
            SqlPortable
                .FindUnportableKeyText(value)
                .Should()
                .NotBeNull($"'{_Escape(value)}' cannot be stored unchanged");
        }
    }

    [Fact]
    public void should_accept_keys_every_engine_keeps_distinct()
    {
        foreach (var value in _Portable)
        {
            SqlPortable.FindUnportableKeyText(value).Should().BeNull($"'{_Escape(value)}' round-trips on every engine");
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 10)]
    [InlineData(15, 10)]
    [InlineData(-15, -10)]
    public void should_truncate_durations_toward_zero_at_microseconds(long ticks, long expected)
    {
        SqlPortable.Truncate(TimeSpan.FromTicks(ticks)).Ticks.Should().Be(expected);
    }

    private static string _Escape(string value)
    {
        return string.Concat(value.Select(c => c is < ' ' or > '~' ? $"\\u{(int)c:x4}" : c.ToString()));
    }
}
