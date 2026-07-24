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
    public void should_return_the_same_result_on_a_repeated_parse()
    {
        using var sut = _CreateSut();

        var first = sut.GetDeviceInfo(_ChromeOnWindows);
        var second = sut.GetDeviceInfo(_ChromeOnWindows);

        // The second call is served from the memo; it must be indistinguishable from the first.
        second.Should().Be(first);
    }

    [Fact]
    public void should_treat_user_agents_sharing_a_truncated_prefix_as_one_entry()
    {
        // Both exceed MaxUserAgentLength and are identical up to it, so they collapse to the same
        // memo key and must resolve to the same device info.
        using var sut = _CreateSut(maxUserAgentLength: 64);

        var a = _ChromeOnWindows + new string('a', 200);
        var b = _ChromeOnWindows + new string('b', 200);

        sut.GetDeviceInfo(a).Should().Be(sut.GetDeviceInfo(b));
    }

    [Fact]
    public void should_still_parse_when_the_memo_cannot_retain_entries()
    {
        // A one-entry memo forces eviction between calls; parsing must stay correct regardless of
        // whether a given call hits or misses.
        using var sut = _CreateSut(maxEntries: 1);

        sut.GetDeviceInfo(_ChromeOnWindows).Should().Contain("Chrome");
        sut.GetDeviceInfo("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)").Should().NotBeNull();
        sut.GetDeviceInfo(_ChromeOnWindows).Should().Contain("Chrome");
    }

    [Fact]
    public void should_return_null_for_an_unidentifiable_user_agent()
    {
        using var sut = _CreateSut();

        sut.GetDeviceInfo("!!!not-a-user-agent!!!").Should().BeNull();
    }

    [Fact]
    public void should_be_safe_for_concurrent_use()
    {
        using var sut = _CreateSut();

        var results = new string?[64];
        Parallel.For(0, results.Length, i => results[i] = sut.GetDeviceInfo(_ChromeOnWindows));

        results.Should().OnlyContain(x => x != null).And.AllBe(results[0]);
    }

    private static UserAgentParser _CreateSut(int maxEntries = 1000, int maxUserAgentLength = 512)
    {
        return new UserAgentParser(
            Options.Create(
                new UserAgentParserOptions { MaxEntries = maxEntries, MaxUserAgentLength = maxUserAgentLength }
            )
        );
    }
}

public sealed class UserAgentParserOptionsValidatorTests : TestBase
{
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
    public void should_reject_a_non_positive_sliding_expiration()
    {
        var result = new UserAgentParserOptionsValidator().Validate(
            new UserAgentParserOptions { SlidingExpiration = TimeSpan.Zero }
        );

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_accept_the_defaults()
    {
        new UserAgentParserOptionsValidator().Validate(new UserAgentParserOptions()).IsValid.Should().BeTrue();
    }
}
