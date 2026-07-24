// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.UserAgent;
using Headless.Caching;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;

namespace Tests.UserAgent;

public sealed class UserAgentParserTests : TestBase
{
    private const string _ChromeOnWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    // --- parse correctness, exercised through the no-cache path (cache is optional and absent) ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_return_null_for_blank_user_agent(string? userAgent)
    {
        var sut = _CreateSut();

        (await sut.GetDeviceInfoAsync(userAgent, AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_parse_os_and_client_from_a_known_user_agent()
    {
        var sut = _CreateSut();

        var result = await sut.GetDeviceInfoAsync(_ChromeOnWindows, AbortToken);

        result.Should().NotBeNullOrWhiteSpace();
        result.Should().Contain("Windows");
        result.Should().Contain("Chrome");
    }

    [Fact]
    public async Task should_return_null_for_an_unidentifiable_user_agent()
    {
        var sut = _CreateSut();

        (await sut.GetDeviceInfoAsync("!!!not-a-user-agent!!!", AbortToken)).Should().BeNull();
    }

    [Fact]
    public async Task should_parse_directly_when_no_cache_is_registered()
    {
        // cache is null -> every call parses; correctness must not depend on a cache being present.
        var sut = _CreateSut(cache: null);

        (await sut.GetDeviceInfoAsync(_ChromeOnWindows, AbortToken)).Should().Contain("Chrome");
        (await sut.GetDeviceInfoAsync(_ChromeOnWindows, AbortToken)).Should().Contain("Chrome");
    }

    // --- caching behavior against a real Headless cache ---

    [Fact]
    public async Task should_memoize_a_parse_under_the_feature_namespaced_key()
    {
        using var cache = _CreateCache();
        var sut = _CreateSut(cache: cache);

        var result = await sut.GetDeviceInfoAsync(_ChromeOnWindows, AbortToken);

        // The value the parser returned must be the value now sitting in the host cache, under api:user-agent:.
        var cached = await cache.GetAsync<string?>("api:user-agent:" + _ChromeOnWindows, AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().Be(result);
    }

    [Fact]
    public async Task should_cache_a_negative_result_so_garbage_is_parsed_at_most_once()
    {
        using var cache = _CreateCache();
        var sut = _CreateSut(cache: cache);

        (await sut.GetDeviceInfoAsync("!!!not-a-user-agent!!!", AbortToken)).Should().BeNull();

        // The null is memoized (HasValue true, Value null), not treated as a miss on the next read.
        var cached = await cache.GetAsync<string?>("api:user-agent:!!!not-a-user-agent!!!", AbortToken);
        cached.HasValue.Should().BeTrue();
        cached.Value.Should().BeNull();
    }

    [Fact]
    public async Task should_collapse_user_agents_that_share_a_truncated_prefix()
    {
        using var cache = _CreateCache();
        var sut = _CreateSut(cache: cache, maxUserAgentLength: 64);

        var a = _ChromeOnWindows + new string('a', 200);
        var b = _ChromeOnWindows + new string('b', 200);

        // Both exceed the cap and are identical up to it, so they key the same memo entry.
        (await sut.GetDeviceInfoAsync(a, AbortToken))
            .Should()
            .Be(await sut.GetDeviceInfoAsync(b, AbortToken));
        (await cache.GetAsync<string?>("api:user-agent:" + _ChromeOnWindows[..64], AbortToken))
            .HasValue.Should()
            .BeTrue();
    }

    private static UserAgentParser _CreateSut(ICache? cache = null, int maxUserAgentLength = 512)
    {
        return new UserAgentParser(
            Options.Create(new UserAgentParserOptions { MaxUserAgentLength = maxUserAgentLength }),
            cache
        );
    }

    private static InMemoryCache _CreateCache() => new(TimeProvider.System, new InMemoryCacheOptions());
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
