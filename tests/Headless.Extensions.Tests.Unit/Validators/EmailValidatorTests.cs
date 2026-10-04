// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Validators;

namespace Tests.Validators;

public sealed class EmailValidatorTests
{
    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user.name@example.com")]
    [InlineData("user+tag@example.com")]
    [InlineData("user@subdomain.example.com")]
    [InlineData("a@b.c")]
    public void should_return_true_for_valid_email(string email)
    {
        var result = EmailValidator.IsValid(email);
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_null()
    {
        var result = EmailValidator.IsValid(null);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_empty()
    {
        var result = EmailValidator.IsValid("");
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("plainaddress")]
    [InlineData("@missinglocal.com")]
    [InlineData("missing@")]
    [InlineData("double@@at.com")]
    [InlineData("spaces in@email.com")]
    public void should_return_false_for_invalid_format(string email)
    {
        var result = EmailValidator.IsValid(email);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_missing_domain()
    {
        var result = EmailValidator.IsValid("user@");
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_missing_local_part()
    {
        var result = EmailValidator.IsValid("@domain.com");
        result.Should().BeFalse();
    }

    [Fact]
    public void should_accept_dotless_domain_by_default()
    {
        var result = EmailValidator.IsValid("user@localhost");
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("user@localhost")]
    [InlineData("admin@intranet")]
    public void should_reject_dotless_domain_when_required(string email)
    {
        var result = EmailValidator.IsValid(email, requireDotInDomainName: true);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("user@sub.example.com")]
    public void should_accept_domain_with_dot_when_required(string email)
    {
        var result = EmailValidator.IsValid(email, requireDotInDomainName: true);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("user+tag@example.com")]
    [InlineData("user+tag+another@example.com")]
    public void should_accept_plus_addressing(string email)
    {
        var result = EmailValidator.IsValid(email);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("user@sub.example.com")]
    [InlineData("user@a.b.c.example.com")]
    public void should_accept_subdomains(string email)
    {
        var result = EmailValidator.IsValid(email);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("test@example.com", false, true)]
    [InlineData("test@example.com", true, true)]
    [InlineData("test@sub.example.com", false, true)]
    [InlineData("test@sub.example.com", true, true)]
    [InlineData("test@example", false, true)]
    [InlineData("test@example", true, false)]
    [InlineData("test@", false, false)]
    [InlineData("test@", true, false)]
    [InlineData("@example.com", false, false)]
    [InlineData("@example.com", true, false)]
    [InlineData("test@.com", false, false)]
    [InlineData("test@.com", true, false)]
    [InlineData("test@com", false, true)]
    [InlineData("test@com", true, false)]
    [InlineData("test", false, false)]
    [InlineData("test", true, false)]
    [InlineData(null, false, false)]
    [InlineData(null, true, false)]
    public void should_return_expected_result_when_is_valid(string? email, bool requireDotInDomainName, bool expected)
    {
        // given, when
        var result = EmailValidator.IsValid(email, requireDotInDomainName);

        // then
        result.Should().Be(expected);
    }
}
