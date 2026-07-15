// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Core;
using Headless.Emails.Azure;

namespace Tests;

public sealed class AzureCommunicationEmailOptionsValidatorTests
{
    private readonly AzureCommunicationEmailOptionsValidator _validator = new();

    [Fact]
    public void should_accept_connection_string_only()
    {
        // given
        var options = new AzureCommunicationEmailOptions { ConnectionString = "endpoint=https://x;accesskey=k" };

        // when & then
        _validator.Validate(options).IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_accept_endpoint_and_access_key()
    {
        // given
        var options = new AzureCommunicationEmailOptions
        {
            Endpoint = new Uri("https://my-resource.communication.azure.com"),
            AccessKey = "the-key",
        };

        // when & then
        _validator.Validate(options).IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_accept_endpoint_and_token_credential()
    {
        // given
        var options = new AzureCommunicationEmailOptions
        {
            Endpoint = new Uri("https://my-resource.communication.azure.com"),
            TokenCredential = Substitute.For<TokenCredential>(),
        };

        // when & then
        _validator.Validate(options).IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_reject_when_no_auth_mode_is_configured()
    {
        // given
        var options = new AzureCommunicationEmailOptions();

        // when & then
        _validator.Validate(options).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_reject_when_endpoint_set_without_key_or_credential()
    {
        // given
        var options = new AzureCommunicationEmailOptions
        {
            Endpoint = new Uri("https://my-resource.communication.azure.com"),
        };

        // when & then
        _validator.Validate(options).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_reject_ambiguous_connection_string_and_access_key()
    {
        // given
        var options = new AzureCommunicationEmailOptions
        {
            ConnectionString = "endpoint=https://x;accesskey=k",
            Endpoint = new Uri("https://my-resource.communication.azure.com"),
            AccessKey = "the-key",
        };

        // when & then
        _validator.Validate(options).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_reject_ambiguous_access_key_and_token_credential()
    {
        // given
        var options = new AzureCommunicationEmailOptions
        {
            Endpoint = new Uri("https://my-resource.communication.azure.com"),
            AccessKey = "the-key",
            TokenCredential = Substitute.For<TokenCredential>(),
        };

        // when & then
        _validator.Validate(options).IsValid.Should().BeFalse();
    }

    [Fact]
    public void should_not_leak_connection_string_or_access_key_when_to_string()
    {
        // given
        var options = new AzureCommunicationEmailOptions
        {
            ConnectionString = "endpoint=https://x;accesskey=super-secret",
        };
        var keyOptions = new AzureCommunicationEmailOptions
        {
            Endpoint = new Uri("https://my-resource.communication.azure.com"),
            AccessKey = "super-secret-key",
        };

        // when & then
        options.ToString().Should().NotContain("super-secret");
        keyOptions.ToString().Should().NotContain("super-secret-key");
    }
}
