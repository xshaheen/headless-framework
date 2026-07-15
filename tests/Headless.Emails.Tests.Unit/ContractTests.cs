// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;

namespace Tests;

public sealed class SendSingleEmailResponseTests
{
    [Fact]
    public void should_have_no_failure_error_or_provider_message_id_when_succeeded()
    {
        var response = SendSingleEmailResponse.Succeeded();

        response.Success.Should().BeTrue();
        response.FailureError.Should().BeNull();
        response.ProviderMessageId.Should().BeNull();
    }

    [Fact]
    public void should_carry_the_provider_message_id_when_succeeded()
    {
        var response = SendSingleEmailResponse.Succeeded("msg-123");

        response.Success.Should().BeTrue();
        response.ProviderMessageId.Should().Be("msg-123");
    }

    [Fact]
    public void should_carry_the_failure_error_when_failed()
    {
        var response = SendSingleEmailResponse.Failed("boom");

        response.Success.Should().BeFalse();
        response.FailureError.Should().Be("boom");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_reject_a_null_or_empty_failure_error_when_failed(string? failureError)
    {
        var act = () => SendSingleEmailResponse.Failed(failureError!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_surface_the_exception_message_when_from_exception()
    {
        var response = SendSingleEmailResponse.FromException(new InvalidOperationException("smtp down"));

        response.Success.Should().BeFalse();
        response.FailureError.Should().Be("smtp down");
    }

    [Fact]
    public void should_fall_back_to_the_type_name_when_from_exception_the_message_is_empty()
    {
        var response = SendSingleEmailResponse.FromException(new InvalidOperationException(""));

        response.Success.Should().BeFalse();
        response.FailureError.Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public void should_reject_a_null_exception_when_from_exception()
    {
        var act = () => SendSingleEmailResponse.FromException(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

public sealed class EmailRequestAddressTests
{
    [Fact]
    public void should_return_bare_address_when_to_string_no_display_name()
    {
        var address = new EmailRequestAddress("user@example.com");

        address.ToString().Should().Be("user@example.com");
    }

    [Fact]
    public void should_format_display_name_when_to_string_present()
    {
        var address = new EmailRequestAddress("user@example.com", "Alice");

        address.ToString().Should().Be("Alice <user@example.com>");
    }

    [Fact]
    public void should_set_only_the_address_when_implicit_conversion_from_string()
    {
        EmailRequestAddress address = "user@example.com";

        address.EmailAddress.Should().Be("user@example.com");
        address.DisplayName.Should().BeNull();
    }

    [Fact]
    public void should_match_the_implicit_operator_when_from_string()
    {
        EmailRequestAddress.FromString("user@example.com").Should().Be(new EmailRequestAddress("user@example.com"));
    }
}

public sealed class EnsureHasBodyTests
{
    [Fact]
    public void should_not_throw_when_text_body_present()
    {
        var request = _Request(text: "hello", html: null);

        var act = request.EnsureHasBody;

        act.Should().NotThrow();
    }

    [Fact]
    public void should_not_throw_when_html_body_present()
    {
        var request = _Request(text: null, html: "<p>hi</p>");

        var act = request.EnsureHasBody;

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void should_throw_when_both_bodies_missing_or_whitespace(string? text, string? html)
    {
        var request = _Request(text: text, html: html);

        var act = request.EnsureHasBody;

        act.Should().Throw<InvalidOperationException>();
    }

    private static SendSingleEmailRequest _Request(string? text, string? html) =>
        new()
        {
            From = "from@example.com",
            Destination = new EmailRequestDestination { ToAddresses = [new EmailRequestAddress("to@example.com")] },
            Subject = "subject",
            MessageText = text,
            MessageHtml = html,
        };
}
