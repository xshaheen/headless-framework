// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Communication.Email;
using Azure.Core;
using Headless.Emails;
using Headless.Emails.Azure;
using Headless.Emails.Dev;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupAzureNamedEmailTests
{
    // Well-formed but non-functional connection strings; EmailClient parses them without contacting ACS.
    private const string _ConnectionString1 =
        "endpoint=https://res1.communication.azure.com/;accesskey=bm90LWEtcmVhbC1rZXk=";

    private const string _ConnectionString2 =
        "endpoint=https://res2.communication.azure.com/;accesskey=YW5vdGhlci1mYWtlLWtleQ==";

    [Fact]
    public void should_resolve_named_azure_sender_alongside_default_noop()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("acs", instance => instance.UseAzure(o => o.ConnectionString = _ConnectionString1));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NoopEmailSender>();
        provider.GetRequiredKeyedService<IEmailSender>("acs").Should().BeOfType<AzureCommunicationEmailSender>();
        provider
            .GetRequiredService<IEmailSenderProvider>()
            .GetSender("acs")
            .Should()
            .BeOfType<AzureCommunicationEmailSender>();
    }

    [Fact]
    public void should_isolate_email_clients_and_options_across_two_named_instances()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("acs1", instance => instance.UseAzure(o => o.ConnectionString = _ConnectionString1));
            setup.AddNamed("acs2", instance => instance.UseAzure(o => o.ConnectionString = _ConnectionString2));
        });
        using var provider = services.BuildServiceProvider();

        // then - distinct keyed clients, each driven by its own named options snapshot.
        var client1 = provider.GetRequiredKeyedService<EmailClient>("acs1");
        var client2 = provider.GetRequiredKeyedService<EmailClient>("acs2");
        client1.Should().NotBeSameAs(client2);

        var monitor = provider.GetRequiredService<IOptionsMonitor<AzureCommunicationEmailOptions>>();
        monitor.Get("acs1").ConnectionString.Should().Be(_ConnectionString1);
        monitor.Get("acs2").ConnectionString.Should().Be(_ConnectionString2);
    }

    [Fact]
    public void should_resolve_named_azure_for_configuration_and_service_provider_overloads()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["ConnectionString"] = _ConnectionString1 }
            )
            .Build();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("fromConfig", instance => instance.UseAzure(configuration));
            setup.AddNamed("fromSp", instance => instance.UseAzure((o, _) => o.ConnectionString = _ConnectionString2));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredKeyedService<IEmailSender>("fromConfig")
            .Should()
            .BeOfType<AzureCommunicationEmailSender>();
        provider.GetRequiredKeyedService<IEmailSender>("fromSp").Should().BeOfType<AzureCommunicationEmailSender>();
    }

    [Fact]
    public void should_resolve_named_azure_for_access_key_and_token_credential_modes()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed(
                "accessKey",
                instance =>
                    instance.UseAzure(o =>
                    {
                        o.Endpoint = new Uri("https://res.communication.azure.com/");
                        o.AccessKey = "bm90LWEtcmVhbC1rZXk=";
                    })
            );
            setup.AddNamed(
                "tokenCred",
                instance =>
                    instance.UseAzure(o =>
                    {
                        o.Endpoint = new Uri("https://res.communication.azure.com/");
                        o.TokenCredential = Substitute.For<TokenCredential>();
                    })
            );
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredKeyedService<IEmailSender>("accessKey").Should().BeOfType<AzureCommunicationEmailSender>();
        provider.GetRequiredKeyedService<IEmailSender>("tokenCred").Should().BeOfType<AzureCommunicationEmailSender>();
    }
}
