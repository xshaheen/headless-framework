// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Abstractions;
using Headless.Api.Security.Claims;
using Headless.Api.Security.Jwt;
using Headless.Constants;
using Headless.Testing.Helpers;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Tests.Security.Jwt;

public sealed class JwtTokenFactoryTests : TestBase
{
    private const string _TestSigningKey = "this-is-a-test-signing-key-must-be-32-bytes-or-more";
    // A256CBC-HS512 requires 512 bits (64 bytes) for encryption key
    private const string _TestEncryptingKey = "this-is-test-encrypt-key-must-be-64-bytes-for-aes256-cbc-hs512!!";
    private const string _TestIssuer = "test-issuer";
    private const string _TestAudience = "test-audience";

    private static JwtTokenFactory _CreateFactory(FakeTimeProvider? timeProvider = null)
    {
        timeProvider ??= new FakeTimeProvider(DateTimeOffset.UtcNow);
        var clock = new TestClock(timeProvider);
        var identityOptions = Options.Create(new IdentityOptions
        {
            ClaimsIdentity = new ClaimsIdentityOptions
            {
                UserNameClaimType = UserClaimTypes.UserName,
                RoleClaimType = UserClaimTypes.Roles,
            },
        });
        var claimsPrincipalFactory = new ClaimsPrincipalFactory(identityOptions);
        return new JwtTokenFactory(claimsPrincipalFactory, clock);
    }

    private static IClaimsPrincipalFactory _CreateMockClaimsPrincipalFactory()
    {
        var mock = Substitute.For<IClaimsPrincipalFactory>();
        mock.CreateClaimsIdentity(Arg.Any<IEnumerable<Claim>>()).Returns(callInfo =>
        {
            var claims = callInfo.Arg<IEnumerable<Claim>>();
            return new ClaimsIdentity(
                claims,
                AuthenticationConstants.IdentityAuthenticationType,
                UserClaimTypes.UserName,
                UserClaimTypes.Roles
            );
        });
        return mock;
    }

    private static List<Claim> _CreateTestClaims()
    {
        return
        [
            new Claim(UserClaimTypes.UserId, "user-123"),
            new Claim(UserClaimTypes.Email, "test@example.com"),
            new Claim(UserClaimTypes.UserName, "testuser"),
            new Claim(UserClaimTypes.Roles, "admin"),
        ];
    }

    #region CreateJwtToken

    [Fact]
    public void should_create_token_with_claims()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();

        // when
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // then
        token.Should().NotBeNullOrEmpty();

        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);

        jwt.Claims.Should().Contain(c => c.Type == UserClaimTypes.UserId && c.Value == "user-123");
        jwt.Claims.Should().Contain(c => c.Type == UserClaimTypes.Email && c.Value == "test@example.com");
        jwt.Claims.Should().Contain(c => c.Type == UserClaimTypes.UserName && c.Value == "testuser");
        jwt.Claims.Should().Contain(c => c.Type == UserClaimTypes.Roles && c.Value == "admin");
    }

    [Fact]
    public void should_set_issued_at_from_clock()
    {
        // given
        var now = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var factory = _CreateFactory(timeProvider);
        var claims = _CreateTestClaims();

        // when
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // then
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);

        jwt.IssuedAt.Should().BeCloseTo(now.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void should_set_expires_from_ttl()
    {
        // given
        var now = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var factory = _CreateFactory(timeProvider);
        var claims = _CreateTestClaims();
        var ttl = TimeSpan.FromHours(2);

        // when
        var token = factory.CreateJwtToken(
            claims,
            ttl,
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // then
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);

        var expectedExpiry = now.UtcDateTime.Add(ttl);
        jwt.ValidTo.Should().BeCloseTo(expectedExpiry, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void should_set_not_before_when_provided()
    {
        // given
        var now = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var factory = _CreateFactory(timeProvider);
        var claims = _CreateTestClaims();
        var notBefore = TimeSpan.FromMinutes(5);

        // when
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience,
            notBefore
        );

        // then
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);

        var expectedNbf = now.UtcDateTime.Add(notBefore);
        jwt.ValidFrom.Should().BeCloseTo(expectedNbf, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void should_skip_not_before_when_null()
    {
        // given
        var now = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var factory = _CreateFactory(timeProvider);
        var claims = _CreateTestClaims();

        // when
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience,
            notBefore: null
        );

        // then
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);

        // nbf claim should not exist or be DateTime.MinValue when not set
        jwt.ValidFrom.Should().Be(DateTime.MinValue);
    }

    [Fact]
    public void should_sign_token_with_hmac_sha256()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();

        // when
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // then
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);

        jwt.Alg.Should().Be(SecurityAlgorithms.HmacSha256);
    }

    [Fact]
    public void should_encrypt_token_when_encrypting_key_provided()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();

        // when
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            _TestEncryptingKey,
            _TestIssuer,
            _TestAudience
        );

        // then
        // JWE tokens have 5 parts separated by dots
        var parts = token.Split('.');
        parts.Should().HaveCount(5, "encrypted JWE tokens have 5 parts");

        // The token should be a valid JWE
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);

        jwt.Enc.Should().Be(SecurityAlgorithms.Aes256CbcHmacSha512);
    }

    [Fact]
    public void should_skip_encryption_when_key_null()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();

        // when
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // then
        // JWS tokens have 3 parts separated by dots
        var parts = token.Split('.');
        parts.Should().HaveCount(3, "non-encrypted JWS tokens have 3 parts");
    }

    [Fact]
    public void should_set_issuer_and_audience()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();

        // when
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // then
        var handler = new JsonWebTokenHandler();
        var jwt = handler.ReadJsonWebToken(token);

        jwt.Issuer.Should().Be(_TestIssuer);
        jwt.Audiences.Should().Contain(_TestAudience);
    }

    [Fact]
    public void should_throw_when_signing_key_too_short()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();
        const string shortKey = "short-key"; // Less than 32 bytes

        // when
        var act = () => factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            shortKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // then
        act.Should().Throw<ArgumentException>()
            .WithMessage("*256 bits*32 bytes*");
    }

    #endregion

    #region ParseJwtTokenAsync

    [Fact]
    public async Task should_parse_valid_token()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        result!.Claims.Should().Contain(c => c.Type == UserClaimTypes.UserId && c.Value == "user-123");
        result.Claims.Should().Contain(c => c.Type == UserClaimTypes.Email && c.Value == "test@example.com");
    }

    [Fact]
    public async Task should_return_null_for_expired_token()
    {
        // given
        var now = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var factory = _CreateFactory(timeProvider);
        var claims = _CreateTestClaims();

        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromMinutes(5),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // Advance time past expiration
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_for_invalid_signature()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        const string differentKey = "this-is-a-different-signing-key-must-be-32-bytes";

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            differentKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_for_wrong_issuer()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            _TestSigningKey,
            encryptingKey: null,
            issuer: "wrong-issuer",
            _TestAudience,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_for_wrong_audience()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            audience: "wrong-audience",
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull();
    }

    [Fact]
    public async Task should_skip_issuer_validation_when_disabled()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            _TestSigningKey,
            encryptingKey: null,
            issuer: "wrong-issuer",
            _TestAudience,
            validateIssuer: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task should_skip_audience_validation_when_disabled()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            audience: "wrong-audience",
            validateIssuer: true,
            validateAudience: false,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task should_decrypt_encrypted_token()
    {
        // given
        var factory = _CreateFactory();
        var claims = _CreateTestClaims();
        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromHours(1),
            _TestSigningKey,
            _TestEncryptingKey,
            _TestIssuer,
            _TestAudience
        );

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            _TestSigningKey,
            _TestEncryptingKey,
            _TestIssuer,
            _TestAudience,
            cancellationToken: AbortToken
        );

        // then
        result.Should().NotBeNull();
        result!.Claims.Should().Contain(c => c.Type == UserClaimTypes.UserId && c.Value == "user-123");
    }

    [Fact]
    public async Task should_use_zero_clock_skew()
    {
        // given
        var now = new DateTimeOffset(2024, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var factory = _CreateFactory(timeProvider);
        var claims = _CreateTestClaims();

        var token = factory.CreateJwtToken(
            claims,
            TimeSpan.FromMinutes(5),
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience
        );

        // Advance time exactly to expiration + 1 second (no skew allowed)
        timeProvider.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));

        // when
        var result = await factory.ParseJwtTokenAsync(
            token,
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull("zero clock skew means token should be invalid immediately after expiry");
    }

    #endregion

    #region Security

    [Fact]
    public async Task should_reject_malformed_token()
    {
        // given
        var factory = _CreateFactory();
        const string malformedToken = "not.a.valid.jwt.token";

        // when
        var result = await factory.ParseJwtTokenAsync(
            malformedToken,
            _TestSigningKey,
            encryptingKey: null,
            _TestIssuer,
            _TestAudience,
            cancellationToken: AbortToken
        );

        // then
        result.Should().BeNull();
    }

    #endregion
}
