// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Validators;

namespace Tests.Validators;

public sealed class EmailValidatorTests
{
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
    public void IsValid_ShouldReturnExpectedResult(string? email, bool requireDotInDomainName, bool expected)
    {
        // given, when
        var result = EmailValidator.IsValid(email, requireDotInDomainName);

        // then
        result.Should().Be(expected);
    }
}
