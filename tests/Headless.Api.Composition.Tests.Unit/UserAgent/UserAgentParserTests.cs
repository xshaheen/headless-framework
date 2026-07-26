// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.UserAgent;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.UserAgent;

public sealed class UserAgentParserTests : TestBase
{
    private const string _ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_return_null_for_blank_user_agent(string? userAgent)
    {
        using var sut = _CreateSut();

        sut.GetDeviceInfo(userAgent).Should().BeNull();
    }

    [Fact]
    public void should_parse_os_and_client_from_a_known_user_agent()
    {
        using var sut = _CreateSut();

        var result = sut.GetDeviceInfo(_ChromeOnWindows);

        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("Windows");
        result.Should().Contain("Chrome");
    }

    [Fact]
    public void should_return_null_for_an_unidentifiable_user_agent()
    {
        using var sut = _CreateSut();

        sut.GetDeviceInfo("!!!not-a-user-agent!!!").Should().BeNull();
    }

    [Fact]
    public void should_memoize_a_parse()
    {
        var parseCalls = 0;
        using var sut = _CreateSut(parser: _ =>
        {
            ++parseCalls;
            return "Windows Chrome";
        });

        sut.GetDeviceInfo(_ChromeOnWindows).Should().Be("Windows Chrome");
        sut.GetDeviceInfo(_ChromeOnWindows).Should().Be("Windows Chrome");
        parseCalls.Should().Be(1);
    }

    [Fact]
    public void should_memoize_a_negative_result_for_subsequent_calls()
    {
        var parseCalls = 0;
        using var sut = _CreateSut(parser: _ =>
        {
            ++parseCalls;
            return null;
        });

        sut.GetDeviceInfo("!!!not-a-user-agent!!!").Should().BeNull();
        sut.GetDeviceInfo("!!!not-a-user-agent!!!").Should().BeNull();
        parseCalls.Should().Be(1);
    }

    [Fact]
    public void should_collapse_user_agents_that_share_a_truncated_prefix()
    {
        var parseCalls = 0;
        using var sut = _CreateSut(
            maxUserAgentLength: 64,
            parser: userAgent =>
            {
                ++parseCalls;
                return userAgent;
            }
        );

        var a = _ChromeOnWindows + new string('a', 200);
        var b = _ChromeOnWindows + new string('b', 200);

        // Both exceed the cap and are identical up to it, so they key the same memo entry.
        sut.GetDeviceInfo(a).Should().Be(sut.GetDeviceInfo(b));
        parseCalls.Should().Be(1);
    }

    [Fact]
    public void should_not_cache_entries_beyond_the_configured_capacity()
    {
        var parseCalls = 0;
        using var sut = _CreateSut(
            maxEntries: 1,
            parser: userAgent =>
            {
                ++parseCalls;
                return userAgent;
            }
        );

        sut.GetDeviceInfo("first").Should().Be("first");
        sut.GetDeviceInfo("first").Should().Be("first");
        sut.GetDeviceInfo("second").Should().Be("second");
        sut.GetDeviceInfo("second").Should().Be("second");

        parseCalls.Should().Be(3);
    }

    private static UserAgentParser _CreateSut(
        int maxUserAgentLength = 512,
        int maxEntries = 1_000,
        Func<string, string?>? parser = null
    )
    {
        var options = Options.Create(
            new UserAgentParserOptions { MaxEntries = maxEntries, MaxUserAgentLength = maxUserAgentLength }
        );

        return parser is null ? new UserAgentParser(options) : new UserAgentParser(options, parser);
    }
}

public sealed class UserAgentParserOptionsValidatorTests : TestBase
{
    [Fact]
    public void should_reject_a_non_positive_sliding_expiration()
    {
        var result = new UserAgentParserOptionsValidator().Validate(
            new UserAgentParserOptions { SlidingExpiration = TimeSpan.Zero }
        );

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_non_positive_max_entries(int maxEntries)
    {
        var result = new UserAgentParserOptionsValidator().Validate(
            new UserAgentParserOptions { MaxEntries = maxEntries }
        );

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_reject_a_sliding_expiration_greater_than_the_absolute_duration()
    {
        var result = new UserAgentParserOptionsValidator().Validate(
            new UserAgentParserOptions
            {
                Duration = TimeSpan.FromMinutes(10),
                SlidingExpiration = TimeSpan.FromMinutes(20),
            }
        );

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_non_positive_max_user_agent_length(int maxUserAgentLength)
    {
        var result = new UserAgentParserOptionsValidator().Validate(
            new UserAgentParserOptions { MaxUserAgentLength = maxUserAgentLength }
        );

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_accept_the_defaults()
    {
        new UserAgentParserOptionsValidator().Validate(new UserAgentParserOptions()).IsValid.Should().BeTrue();
    }
}
