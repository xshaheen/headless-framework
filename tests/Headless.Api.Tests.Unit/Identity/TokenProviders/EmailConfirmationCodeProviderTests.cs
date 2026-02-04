// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity.TokenProviders;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Tests.Identity.TokenProviders;

public sealed class EmailConfirmationCodeProviderTests : TestBase
{
    private readonly IOptions<EmailConfirmationCodeProviderOptions> _options;
    private readonly UserManager<EmailTestUser> _userManager;
    private readonly EmailConfirmationCodeProvider<EmailTestUser> _sut;

    public EmailConfirmationCodeProviderTests()
    {
        _options = Options.Create(new EmailConfirmationCodeProviderOptions());
        _userManager = Substitute.For<UserManager<EmailTestUser>>(
            Substitute.For<IUserStore<EmailTestUser>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        _sut = new EmailConfirmationCodeProvider<EmailTestUser>(_options);
    }

    [Fact]
    public async Task should_generate_modifier_with_email()
    {
        // given
        var user = new EmailTestUser();
        var email = "test@example.com";
        _userManager.GetEmailAsync(user).Returns(Task.FromResult<string?>(email));

        // when
        var modifier = await _sut.GetUserModifierAsync("purpose", _userManager, user);

        // then
        modifier.Should().Contain(email);
        modifier.Should().Be($"{EmailConfirmationCodeProviderOptions.DefaultName}:purpose:{email}");
    }

    [Fact]
    public async Task should_throw_when_user_has_no_email()
    {
        // given
        var user = new EmailTestUser();
        _userManager.GetEmailAsync(user).Returns(Task.FromResult<string?>(null));

        // when
        var act = () => _sut.GetUserModifierAsync("purpose", _userManager, user);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The user does not have an email.");
    }

    [Fact]
    public async Task should_return_false_for_can_generate_two_factor()
    {
        // given
        var user = new EmailTestUser();

        // when
        var result = await _sut.CanGenerateTwoFactorTokenAsync(_userManager, user);

        // then
        result.Should().BeFalse();
    }
}

public class EmailTestUser
{
    public string Id { get; set; } = "user-123";
}
