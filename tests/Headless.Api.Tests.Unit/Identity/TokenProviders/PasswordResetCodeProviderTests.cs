// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity.TokenProviders;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Tests.Identity.TokenProviders;

public sealed class PasswordResetCodeProviderTests : TestBase
{
    private readonly IOptions<PasswordResetCodeProviderOptions> _options;
    private readonly UserManager<PasswordResetTestUser> _userManager;
    private readonly PasswordResetCodeProvider<PasswordResetTestUser> _sut;

    public PasswordResetCodeProviderTests()
    {
        _options = Options.Create(new PasswordResetCodeProviderOptions());
        _userManager = Substitute.For<UserManager<PasswordResetTestUser>>(
            Substitute.For<IUserStore<PasswordResetTestUser>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        _sut = new PasswordResetCodeProvider<PasswordResetTestUser>(_options);
    }

    [Fact]
    public async Task should_generate_modifier_with_user_id()
    {
        // given
        var user = new PasswordResetTestUser();
        _userManager.GetUserIdAsync(user).Returns(Task.FromResult(user.Id));

        // when
        var modifier = await _sut.GetUserModifierAsync("purpose", _userManager, user);

        // then
        modifier.Should().Contain(user.Id);
        modifier.Should().Be($"{PasswordResetCodeProviderOptions.DefaultName}:purpose:{user.Id}");
    }

    [Fact]
    public async Task should_return_false_for_can_generate_two_factor()
    {
        // given
        var user = new PasswordResetTestUser();

        // when
        var result = await _sut.CanGenerateTwoFactorTokenAsync(_userManager, user);

        // then
        result.Should().BeFalse();
    }
}

public class PasswordResetTestUser
{
    public string Id { get; set; } = "user-456";
}
