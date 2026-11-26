// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class AttemptLimiterConfigurationTests : TestBase
{
    private static readonly string _SubjectKey = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void should_expose_the_purposes_declared_in_code()
    {
        // given
        var quota = new AttemptQuota(5, TimeSpan.FromMinutes(15));
        var services = new ServiceCollection();
        services.AddAttemptLimiter(options =>
        {
            options.SubjectKey = _SubjectKey;
            options.Purposes["sign-in.password"] = quota;
            options.Purposes["password-reset:email"] = new AttemptQuota(3, TimeSpan.FromHours(1));
        });
        using var provider = services.BuildServiceProvider();

        // when
        var purposes = provider.GetRequiredService<IOptions<AttemptLimiterOptions>>().Value.Purposes;

        // then
        purposes.Keys.Should().BeEquivalentTo("sign-in.password", "password-reset:email");
        purposes["sign-in.password"].Should().Be(quota);
    }

    [Fact]
    public void should_bind_declared_purposes_from_configuration()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["SubjectKey"] = _SubjectKey,
                    ["Purposes:otp.verify:Limit"] = "5",
                    ["Purposes:otp.verify:Window"] = "00:10:00",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddAttemptLimiter(configuration);
        using var provider = services.BuildServiceProvider();

        // when
        var purposes = provider.GetRequiredService<IOptions<AttemptLimiterOptions>>().Value.Purposes;

        // then
        purposes.Should().ContainKey("otp.verify");
        purposes["otp.verify"].Should().Be(new AttemptQuota(5, TimeSpan.FromMinutes(10)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_refuse_a_blank_declared_purpose(string purpose)
    {
        // given
        var services = new ServiceCollection();
        services.AddAttemptLimiter(options =>
        {
            options.SubjectKey = _SubjectKey;
            options.Purposes[purpose] = new AttemptQuota(5, TimeSpan.FromMinutes(1));
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<AttemptLimiterOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*must not be blank*");
    }

    [Fact]
    public void should_refuse_a_declared_purpose_without_a_quota()
    {
        // given
        var services = new ServiceCollection();
        services.AddAttemptLimiter(options =>
        {
            options.SubjectKey = _SubjectKey;
            options.Purposes["otp.verify"] = null!;
        });
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<AttemptLimiterOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*needs a quota*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==")] // 31 bytes
    public void should_refuse_a_missing_malformed_or_short_subject_key(string subjectKey)
    {
        // given
        var services = new ServiceCollection();
        services.AddAttemptLimiter(options => options.SubjectKey = subjectKey);
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<AttemptLimiterOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_accept_a_32_byte_subject_key()
    {
        // given
        var services = new ServiceCollection();
        services.AddAttemptLimiter(options => options.SubjectKey = Convert.ToBase64String(new byte[32]));
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<AttemptLimiterOptions>>().Value;

        // then
        options.KeyPrefix.Should().Be("attempts");
    }

    [Fact]
    public void should_let_a_hand_built_rejected_result_throw_too_many_requests()
    {
        // given - the shape a test stub for IAttemptLimiter returns
        var rejected = new AttemptResult("otp-delivery", count: 6, limit: 5, retryAfter: TimeSpan.FromSeconds(30));

        // when
        var act = () => rejected.ThrowIfRejected();

        // then
        rejected.IsAllowed.Should().BeFalse();
        rejected.Remaining.Should().Be(0);
        rejected.ResetToken.Should().BeNull();
        act.Should()
            .ThrowExactly<Headless.TooManyRequestsException>()
            .Which.RetryAfter.Should()
            .Be(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData("", 1, 1, 1)]
    [InlineData("otp", 0, 1, 1)]
    [InlineData("otp", 1, 0, 1)]
    [InlineData("otp", 1, 1, 0)]
    public void should_refuse_a_result_that_contradicts_the_limiter(
        string purpose,
        long count,
        int limit,
        int retryAfterSeconds
    )
    {
        var act = () => new AttemptResult(purpose, count, limit, TimeSpan.FromSeconds(retryAfterSeconds));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_refuse_a_non_positive_limit()
    {
        var act = () => new AttemptQuota(0, TimeSpan.FromMinutes(1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_refuse_a_non_positive_window()
    {
        var act = () => new AttemptQuota(1, TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_refuse_a_window_that_is_not_whole_seconds()
    {
        var act = () => new AttemptQuota(1, TimeSpan.FromMilliseconds(1500));

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("window");
    }
}
