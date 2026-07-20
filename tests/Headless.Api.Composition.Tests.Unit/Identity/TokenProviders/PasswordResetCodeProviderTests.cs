// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity.TokenProviders;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Identity.TokenProviders;

public sealed class PasswordResetCodeProviderTests : TestBase
{
    private readonly TotpRfc6238Generator _generator = new(TimeProvider.System);

    private readonly UserManager<PasswordResetTestUser> _userManager = Substitute.For<
        UserManager<PasswordResetTestUser>
    >(Substitute.For<IUserStore<PasswordResetTestUser>>(), null, null, null, null, null, null, null, null);

    private PasswordResetCodeProvider<PasswordResetTestUser> _CreateSut(
        PasswordResetCodeProviderOptions? options = null,
        TotpRfc6238Generator? generator = null
    )
    {
        return new PasswordResetCodeProvider<PasswordResetTestUser>(
            generator ?? _generator,
            Options.Create(options ?? new PasswordResetCodeProviderOptions())
        );
    }

    [Fact]
    public async Task should_generate_6_digit_code()
    {
        // given
        var user = new PasswordResetTestUser();
        _userManager
            .CreateSecurityTokenAsync(user)
            .Returns(Task.FromResult(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));
        var sut = _CreateSut();

        // when
        var code = await sut.GenerateAsync("purpose", _userManager, user);

        // then
        code.Should().HaveLength(6);
        code.Should().MatchRegex(@"^\d{6}$");
    }

    [Fact]
    public async Task should_validate_correct_code()
    {
        // given
        var user = new PasswordResetTestUser();
        _userManager
            .CreateSecurityTokenAsync(user)
            .Returns(Task.FromResult(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));
        var sut = _CreateSut();

        var code = await sut.GenerateAsync("purpose", _userManager, user);

        // when
        var result = await sut.ValidateAsync("purpose", code, _userManager, user);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_reject_wrong_code()
    {
        // given
        var user = new PasswordResetTestUser();
        _userManager
            .CreateSecurityTokenAsync(user)
            .Returns(Task.FromResult(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));
        var sut = _CreateSut();

        // when
        var result = await sut.ValidateAsync("purpose", "000000", _userManager, user);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_reject_non_numeric_token()
    {
        // given
        var user = new PasswordResetTestUser();
        _userManager
            .CreateSecurityTokenAsync(user)
            .Returns(Task.FromResult(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));
        var sut = _CreateSut();

        // when
        var result = await sut.ValidateAsync("purpose", "abc123", _userManager, user);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_return_false_for_can_generate_two_factor()
    {
        // given
        var user = new PasswordResetTestUser();
        var sut = _CreateSut();

        // when
        var result = await sut.CanGenerateTwoFactorTokenAsync(_userManager, user);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public async Task should_use_custom_timestep()
    {
        // given
        var fixedTime = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var generator = new TotpRfc6238Generator(timeProvider);
        var user = new PasswordResetTestUser();
        _userManager
            .CreateSecurityTokenAsync(user)
            .Returns(Task.FromResult(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        var options = new PasswordResetCodeProviderOptions { Timestep = TimeSpan.FromMinutes(5) };
        var sut = _CreateSut(options, generator);

        var code = await sut.GenerateAsync("purpose", _userManager, user);

        // when — advance past default 3-min window but within 5-min window
        timeProvider.Advance(TimeSpan.FromMinutes(4));
        var result = await sut.ValidateAsync("purpose", code, _userManager, user);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public async Task should_use_custom_variance()
    {
        // given
        var fixedTime = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var generator = new TotpRfc6238Generator(timeProvider);
        var user = new PasswordResetTestUser();
        _userManager
            .CreateSecurityTokenAsync(user)
            .Returns(Task.FromResult(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        var options = new PasswordResetCodeProviderOptions { Variance = 0 };
        var sut = _CreateSut(options, generator);

        var code = await sut.GenerateAsync("purpose", _userManager, user);

        // when — with zero variance, code is only valid in the exact same timestep
        var result = await sut.ValidateAsync("purpose", code, _userManager, user);

        // then
        result.Should().BeTrue();

        // and — advancing 1 full timestep should invalidate
        timeProvider.Advance(TimeSpan.FromMinutes(3));
        var resultAfterAdvance = await sut.ValidateAsync("purpose", code, _userManager, user);
        resultAfterAdvance.Should().BeFalse();
    }

    [Fact]
    public async Task should_use_custom_hash_mode()
    {
        // given
        var user = new PasswordResetTestUser();
        _userManager
            .CreateSecurityTokenAsync(user)
            .Returns(Task.FromResult(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        var options = new PasswordResetCodeProviderOptions { HashMode = TotpHashMode.Sha256 };
        var sut = _CreateSut(options);

        var code = await sut.GenerateAsync("purpose", _userManager, user);

        // when
        var result = await sut.ValidateAsync("purpose", code, _userManager, user);

        // then
        result.Should().BeTrue();
    }
}

public class PasswordResetTestUser
{
    public string Id { get; set; } = "user-456";
}
