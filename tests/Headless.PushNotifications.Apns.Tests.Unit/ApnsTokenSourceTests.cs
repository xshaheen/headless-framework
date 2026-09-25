// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using Headless.PushNotifications.Apns;
using Headless.PushNotifications.Apns.Internals;
using Headless.Testing.Tests;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class ApnsTokenSourceTests : TestBase
{
    private const string _KeyId = "ABC123DEFG";
    private const string _TeamId = "TEAM123456";

    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    protected override ValueTask DisposeAsyncCore()
    {
        _key.Dispose();

        return base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_mint_es256_jwt_with_key_id_team_id_and_issued_at()
    {
        // given
        using var source = new ApnsTokenSource(_timeProvider);

        // when
        var token = await source.GetTokenAsync(_CreateOptions(), AbortToken);

        // then
        var segments = token.Value.Split('.');
        segments.Should().HaveCount(3);
        token.Value.Should().NotContain("=");

        using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[0]));
        header.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("alg", "kid");
        header.RootElement.GetProperty("alg").GetString().Should().Be("ES256");
        header.RootElement.GetProperty("kid").GetString().Should().Be(_KeyId);

        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[1]));
        claims.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("iss", "iat");
        claims.RootElement.GetProperty("iss").GetString().Should().Be(_TeamId);
        claims.RootElement.GetProperty("iat").GetInt64().Should().Be(_timeProvider.GetUtcNow().ToUnixTimeSeconds());

        var signature = Base64Url.DecodeFromChars(segments[2]);
        signature.Should().HaveCount(64, "ES256 in JWS is the fixed-width r||s form");
        _key.VerifyData(
                Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation
            )
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task should_reuse_token_when_younger_than_fifty_minutes()
    {
        // given
        using var source = new ApnsTokenSource(_timeProvider);
        var options = _CreateOptions();
        var first = await source.GetTokenAsync(options, AbortToken);

        // when
        _timeProvider.Advance(TimeSpan.FromMinutes(49));
        var second = await source.GetTokenAsync(options, AbortToken);

        // then
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task should_refresh_token_when_fifty_minutes_old()
    {
        // given
        using var source = new ApnsTokenSource(_timeProvider);
        var options = _CreateOptions();
        var first = await source.GetTokenAsync(options, AbortToken);

        // when
        _timeProvider.Advance(TimeSpan.FromMinutes(50));
        var second = await source.GetTokenAsync(options, AbortToken);

        // then
        second.Value.Should().NotBe(first.Value);
        second.Generation.Should().BeGreaterThan(first.Generation);
        _ReadIssuedAt(second.Value)
            .Should()
            .Be(_ReadIssuedAt(first.Value) + (long)TimeSpan.FromMinutes(50).TotalSeconds);
    }

    [Fact]
    public async Task should_mint_once_when_many_callers_race_on_cold_cache()
    {
        // given
        using var source = new ApnsTokenSource(_timeProvider);
        var options = _CreateOptions();
        using var start = new ManualResetEventSlim();

        // when
        var tasks = Enumerable
            .Range(0, 50)
            .Select(_ =>
                Task.Run(
                    async () =>
                    {
                        start.Wait(AbortToken);
                        return await source.GetTokenAsync(options, AbortToken);
                    },
                    AbortToken
                )
            )
            .ToArray();
        start.Set();
        var tokens = await Task.WhenAll(tasks);

        // then
        tokens.Select(t => t.Generation).Distinct().Should().ContainSingle();
        tokens.Select(t => t.Value).Distinct(StringComparer.Ordinal).Should().ContainSingle();
    }

    [Fact]
    public async Task should_remint_once_when_current_generation_invalidated_after_twenty_minutes()
    {
        // given
        using var source = new ApnsTokenSource(_timeProvider);
        var options = _CreateOptions();
        var rejected = await source.GetTokenAsync(options, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(20));

        // when
        var reminted = await source.InvalidateAsync(options, rejected.Generation, AbortToken);
        var afterStaleInvalidation = await source.InvalidateAsync(options, rejected.Generation, AbortToken);

        // then
        reminted.Generation.Should().BeGreaterThan(rejected.Generation);
        reminted.Value.Should().NotBe(rejected.Value);
        afterStaleInvalidation.Should().BeSameAs(reminted);
        (await source.GetTokenAsync(options, AbortToken)).Should().BeSameAs(reminted);
    }

    [Fact]
    public async Task should_keep_current_token_when_invalidated_under_twenty_minutes()
    {
        // given
        using var source = new ApnsTokenSource(_timeProvider);
        var options = _CreateOptions();
        var current = await source.GetTokenAsync(options, AbortToken);
        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        // when
        var afterInvalidation = await source.InvalidateAsync(options, current.Generation, AbortToken);

        // then
        afterInvalidation.Should().BeSameAs(current);
    }

    [Fact]
    public async Task should_share_token_across_option_sets_with_the_same_key()
    {
        // given
        using var source = new ApnsTokenSource(_timeProvider);
        var production = _CreateOptions();
        var sandbox = _CreateOptions();
        sandbox.Environment = ApnsEnvironment.Sandbox;
        sandbox.BundleId = "com.example.other";

        // when
        var first = await source.GetTokenAsync(production, AbortToken);
        var second = await source.GetTokenAsync(sandbox, AbortToken);

        // then
        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task should_refuse_second_private_key_for_the_same_key_identity()
    {
        // given
        using var source = new ApnsTokenSource(_timeProvider);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var other = _CreateOptions();
        other.PrivateKey = otherKey.ExportPkcs8PrivateKeyPem();
        await source.GetTokenAsync(_CreateOptions(), AbortToken);

        // when
        var act = async () => await source.GetTokenAsync(other, AbortToken);

        // then
        var exception = (await act.Should().ThrowExactlyAsync<InvalidOperationException>()).Which;
        exception.Message.Should().Contain(_KeyId);
        exception.Message.Should().NotContain(other.PrivateKey);
        exception.Message.Should().NotContain(_CreateOptions().PrivateKey);
    }

    [Fact]
    public async Task should_refuse_use_after_dispose()
    {
        // given
        var source = new ApnsTokenSource(_timeProvider);
        source.Dispose();

        // when
        var act = async () => await source.GetTokenAsync(_CreateOptions(), AbortToken);

        // then
        await act.Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    private ApnsOptions _CreateOptions()
    {
        return new ApnsOptions
        {
            KeyId = _KeyId,
            TeamId = _TeamId,
            PrivateKey = _key.ExportPkcs8PrivateKeyPem(),
            BundleId = "com.example.app",
        };
    }

    private static long _ReadIssuedAt(string jwt)
    {
        using var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Split('.')[1]));

        return claims.RootElement.GetProperty("iat").GetInt64();
    }
}
