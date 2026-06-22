// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Communication.Email;
using Azure.Core;
using Headless.Emails;
using Headless.Emails.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Tests;

public sealed class SetupAzureEmailTests
{
    // Well-formed but non-functional connection string; EmailClient parses it at construction without contacting ACS.
    private const string _ConnectionString =
        "endpoint=https://my-resource.communication.azure.com/;accesskey=bm90LWEtcmVhbC1rZXk=";

    [Fact]
    public void should_resolve_azure_sender_and_email_client_for_connection_string_mode()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup => setup.UseAzure(options => options.ConnectionString = _ConnectionString));
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetServices<IEmailSender>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<AzureCommunicationEmailSender>();
        provider.GetServices<EmailClient>().Should().ContainSingle();
    }

    [Fact]
    public void should_resolve_azure_sender_for_endpoint_and_access_key_mode()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
            setup.UseAzure(options =>
            {
                options.Endpoint = new Uri("https://my-resource.communication.azure.com/");
                options.AccessKey = "bm90LWEtcmVhbC1rZXk=";
            })
        );
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<AzureCommunicationEmailSender>();
        provider.GetRequiredService<EmailClient>().Should().NotBeNull();
    }

    [Fact]
    public void should_resolve_azure_sender_for_endpoint_and_token_credential_mode()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
            setup.UseAzure(options =>
            {
                options.Endpoint = new Uri("https://my-resource.communication.azure.com/");
                options.TokenCredential = Substitute.For<TokenCredential>();
            })
        );
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<AzureCommunicationEmailSender>();
        provider.GetRequiredService<EmailClient>().Should().NotBeNull();
    }
}
