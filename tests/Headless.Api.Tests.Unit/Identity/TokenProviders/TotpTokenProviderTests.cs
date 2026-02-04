// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity.TokenProviders;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Tests.Identity.TokenProviders;

public sealed class TotpTokenProviderTests : TestBase
{
    private readonly TotpRfc6238Generator _generator;
    private readonly IOptions<TotpTokenProviderOptions> _options;
    private readonly UserManager<TestUser> _userManager;
    private readonly TotpTokenProvider<TestUser> _sut;

    public TotpTokenProviderTests()
    {
        var timeProvider = TimeProvider.System;
        _generator = new TotpRfc6238Generator(timeProvider);
        _options = Options.Create(new TotpTokenProviderOptions());
        _userManager = Substitute.For<UserManager<TestUser>>(
            Substitute.For<IUserStore<TestUser>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        _sut = new TotpTokenProvider<TestUser>(_generator, _options);
    }

    [Fact]
    public async Task should_generate_6_digit_code()
    {
        // given
        var user = new TestUser();
        var securityToken = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        _userManager.CreateSecurityTokenAsync(user).Returns(Task.FromResult(securityToken));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        // when
        var code = await _sut.GenerateAsync("purpose", _userManager, user);

        // then
        code.Should().HaveLength(6);
        code.Should().MatchRegex(@"^\d{6}$");
    }

    [Fact]
    public async Task should_use_security_token_from_manager()
    {
        // given
        var user = new TestUser();
        var securityToken = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        _userManager.CreateSecurityTokenAsync(user).Returns(Task.FromResult(securityToken));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        // when
        await _sut.GenerateAsync("purpose", _userManager, user);

        // then
        await _userManager.Received(1).CreateSecurityTokenAsync(user);
    }

    [Fact]
    public async Task should_validate_correct_code()
    {
        // given
        var user = new TestUser();
        var securityToken = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        _userManager.CreateSecurityTokenAsync(user).Returns(Task.FromResult(securityToken));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        var code = await _sut.GenerateAsync("purpose", _userManager, user);

        // when
        var result = await _sut.ValidateAsync("purpose", code, _userManager, user);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_wrong_code()
    {
        // given
        var user = new TestUser();
        var securityToken = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        _userManager.CreateSecurityTokenAsync(user).Returns(Task.FromResult(securityToken));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        // when
        var result = await _sut.ValidateAsync("purpose", "000000", _userManager, user);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_for_can_generate_two_factor()
    {
        // given
        var user = new TestUser();

        // when
        var result = await _sut.CanGenerateTwoFactorTokenAsync(_userManager, user);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_non_numeric_token()
    {
        // given
        var user = new TestUser();
        var securityToken = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        _userManager.CreateSecurityTokenAsync(user).Returns(Task.FromResult(securityToken));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        // when
        var result = await _sut.ValidateAsync("purpose", "abc123", _userManager, user);

        // then
        result.Should().BeFalse();
    }
}

public class TestUser
{
    public string Id { get; set; } = "user-123";
}
