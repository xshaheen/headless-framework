// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Framework.BuildingBlocks;
using Framework.BuildingBlocks.Abstractions;
using Tests.Fakers;

namespace Tests.Abstractions;

public sealed class ThreadCurrentPrincipalAccessorTests
{
    private static ClaimsPrincipal _FakerClaimsPrincipal()
    {
        return new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(FrameworkClaimTypes.Name, FakerData.GenerateName()),
                    new Claim(FrameworkClaimTypes.Email, FakerData.GenerateEmail()),
                ]
            )
        );
    }

    private readonly ThreadCurrentPrincipalAccessor _accessor;

    public ThreadCurrentPrincipalAccessorTests()
    {
        _accessor = new ThreadCurrentPrincipalAccessor();
    }

    [Fact]
    public void change_should_set_and_restore_principal()
    {
        // given
        var originalPrincipal = _FakerClaimsPrincipal();

        Thread.CurrentPrincipal = originalPrincipal;

        const string nameValue = "zad-charities";
        const string emailValue = "zad-charities@tt.net";

        var newPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(FrameworkClaimTypes.Name, nameValue), new Claim(FrameworkClaimTypes.Email, emailValue)]
            )
        );

        // when
        _accessor.Change(newPrincipal);

        // then
        var name = _accessor
            .Principal.Claims.First(x => string.Equals(x.Type, FrameworkClaimTypes.Name, StringComparison.Ordinal))
            .Value;
        var email = _accessor
            .Principal.Claims.First(x => string.Equals(x.Type, FrameworkClaimTypes.Email, StringComparison.Ordinal))
            .Value;

        name.Should().Be(nameValue);
        email.Should().Be(emailValue);
    }

    [Fact]
    public void principal_should_return_thread_current_principal()
    {
        // given
        var expectedPrincipal = _FakerClaimsPrincipal();

        Thread.CurrentPrincipal = expectedPrincipal;

        // then
        _accessor.Principal.Should().BeSameAs(expectedPrincipal);
    }

    [Fact]
    public void principal_should_throw_if_thread_current_principal_is_null_or_invalid()
    {
        // when
        var action = () =>
        {
            var principal = _accessor.Principal;
        };

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Thread.CurrentPrincipal is null or not a ClaimsPrincipal.");
    }

    [Fact]
    public void change_should_restore_original_principal_when_disposed()
    {
        // given
        const string newName = "zad-charities";

        var originalPrincipal = _FakerClaimsPrincipal();

        Thread.CurrentPrincipal = originalPrincipal;

        var newPrincipal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, newName)]));

        // when
        var disposable = _accessor.Change(newPrincipal);

        // then
        _accessor.Principal.Claims.First().Value.Should().Be(newName);

        // when
        disposable.Dispose();

        // then
        var oldName = originalPrincipal.Claims.First().Value;
        _accessor.Principal.Claims.First().Value.Should().Be(oldName);
    }
}
