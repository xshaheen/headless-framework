// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.UserAgent;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.UserAgent;

public sealed class UserAgentParserTests : TestBase
{
    private const string _ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private const string _SafariOnIphone =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

    private const string _Googlebot = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_return_null_for_blank_user_agent(string? userAgent)
    {
        using var sut = _CreateSut();

        sut.GetDeviceInfo(userAgent).Should().BeNull();
        sut.Parse(userAgent).Should().BeNull();
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
        sut.Parse("!!!not-a-user-agent!!!").Should().BeNull();
    }

    [Fact]
    public void should_expose_the_individual_fields_a_summary_cannot_carry()
    {
        using var sut = _CreateSut();

        var info = sut.Parse(_ChromeOnWindows);

        info.Should().NotBeNull();
        info!.UserAgent.Should().Be(_ChromeOnWindows);
        info.IsBot.Should().BeFalse();
        info.Device.Should().Be(DeviceType.Desktop);
        info.OsName.Should().Be("Windows");
        info.ClientName.Should().Be("Chrome");
        info.ClientVersion.Should().NotBeNullOrWhiteSpace();
        info.ClientType.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void should_identify_a_phone_and_its_brand()
    {
        using var sut = _CreateSut();

        var info = sut.Parse(_SafariOnIphone);

        info.Should().NotBeNull();
        info!.Device.Should().Be(DeviceType.Phone);
        info.DeviceBrand.Should().Be("Apple");
        info.OsName.Should().Be("iOS");
        info.IsBot.Should().BeFalse();
    }

    [Fact]
    public void should_identify_a_bot_and_report_it_as_one()
    {
        using var sut = _CreateSut();

        var info = sut.Parse(_Googlebot);

        info.Should().NotBeNull();
        info!.IsBot.Should().BeTrue();
        // A bot is reported as a bot device regardless of what the detector guesses the hardware is.
        info.Device.Should().Be(DeviceType.Bot);
        info.BotName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void should_return_the_same_text_from_both_entry_points()
    {
        using var sut = _CreateSut();

        sut.GetDeviceInfo(_ChromeOnWindows).Should().Be(sut.Parse(_ChromeOnWindows)!.Summary);
    }

    [Fact]
    public void should_parse_once_no_matter_which_entry_point_is_used()
    {
        var parseCalls = 0;
        using var sut = _CreateSut(parser: _ =>
        {
            ++parseCalls;
            return _Info("Windows", "Chrome");
        });

        // Both members share one memo, so the second and third calls cost nothing.
        sut.Parse(_ChromeOnWindows).Should().NotBeNull();
        sut.GetDeviceInfo(_ChromeOnWindows).Should().Be("Windows Chrome");
        sut.Parse(_ChromeOnWindows).Should().NotBeNull();

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
                return _Info(os: null, client: userAgent);
            }
        );

        var a = _ChromeOnWindows + new string('a', 200);
        var b = _ChromeOnWindows + new string('b', 200);

        // Both exceed the cap and are identical up to it, so they key the same memo entry.
        sut.GetDeviceInfo(a).Should().Be(sut.GetDeviceInfo(b));
        parseCalls.Should().Be(1);
    }

    [Fact]
    public void should_cap_the_user_agent_it_reports_back()
    {
        using var sut = _CreateSut(maxUserAgentLength: 64);

        var info = sut.Parse(_ChromeOnWindows + new string('a', 200));

        // The contract says UserAgent is the value after capping, so a caller persisting it cannot be handed
        // an unbounded string it never asked for.
        info.Should().NotBeNull();
        info!.UserAgent.Length.Should().Be(64);
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
                return _Info(os: null, client: userAgent);
            }
        );

        sut.GetDeviceInfo("first").Should().Be("first");
        sut.GetDeviceInfo("first").Should().Be("first");
        sut.GetDeviceInfo("second").Should().Be("second");
        sut.GetDeviceInfo("second").Should().Be("second");

        parseCalls.Should().Be(3);
    }

    private static UserAgentInfo _Info(string? os, string? client)
    {
        return new UserAgentInfo
        {
            UserAgent = "ua",
            IsBot = false,
            Device = DeviceType.Unknown,
            OsName = os,
            ClientName = client,
        };
    }

    private static UserAgentParser _CreateSut(
        int maxUserAgentLength = 512,
        int maxEntries = 1_000,
        Func<string, UserAgentInfo?>? parser = null
    )
    {
        var options = Options.Create(
            new UserAgentParserOptions { MaxEntries = maxEntries, MaxUserAgentLength = maxUserAgentLength }
        );

        return parser is null ? new UserAgentParser(options) : new UserAgentParser(options, parser);
    }
}

public sealed class UserAgentInfoTests : TestBase
{
    [Theory]
    [InlineData("Windows", "Chrome", "Windows Chrome")]
    [InlineData("Windows", null, "Windows")]
    [InlineData(null, "Chrome", "Chrome")]
    [InlineData(null, null, null)]
    [InlineData("  ", "  ", null)]
    public void should_join_only_the_parts_it_actually_has(string? os, string? client, string? expected)
    {
        var info = new UserAgentInfo
        {
            UserAgent = "ua",
            IsBot = false,
            Device = DeviceType.Unknown,
            OsName = os,
            ClientName = client,
        };

        info.Summary.Should().Be(expected);
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
