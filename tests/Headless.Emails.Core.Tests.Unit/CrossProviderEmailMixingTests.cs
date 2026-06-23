// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Headless.Emails;
using Headless.Emails.Aws;
using Headless.Emails.Azure;
using Headless.Emails.Dev;
using Headless.Emails.Mailkit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Cross-cutting tests that a single <c>AddHeadlessEmails</c> call can compose a default sender with named
/// instances from every provider, each owning its own keyed backend, without DI collisions.
/// </summary>
public sealed class CrossProviderEmailMixingTests
{
    private const string _AzureConnectionString =
        "endpoint=https://res.communication.azure.com/;accesskey=bm90LWEtcmVhbC1rZXk=";

    [Fact]
    public void should_register_default_and_heterogeneous_named_providers_without_collision()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        var mailkitConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Server"] = "smtp.example.com",
                    ["Port"] = 2525.ToString(CultureInfo.InvariantCulture),
                }
            )
            .Build();
        var awsOptions = new AWSOptions
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("fake-access-key", "fake-secret-key"),
        };

        // when - a default Noop plus one named instance per provider, all in a single call.
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("acs", instance => instance.UseAzure(o => o.ConnectionString = _AzureConnectionString));
            setup.AddNamed("ses", instance => instance.UseAwsSes(awsOptions));
            setup.AddNamed("smtp", instance => instance.UseMailkit(mailkitConfig));
            setup.AddNamed("sink", instance => instance.UseDevelopment("out.txt"));
        });
        using var provider = services.BuildServiceProvider();

        // then - the default and every named sender resolve to their own provider type.
        var senderProvider = provider.GetRequiredService<IEmailSenderProvider>();
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NoopEmailSender>();

        // AzureCommunicationEmailSender is internal to its package; assert by runtime type name.
        senderProvider.GetSender("acs").GetType().Name.Should().Be("AzureCommunicationEmailSender");
        senderProvider.GetSender("ses").Should().BeOfType<AwsSesEmailSender>();
        senderProvider.GetSender("smtp").Should().BeOfType<MailkitEmailSender>();
        senderProvider.GetSender("sink").Should().BeOfType<DevEmailSender>();

        // keyed resolution stays in sync with the provider.
        provider.GetRequiredKeyedService<IEmailSender>("ses").Should().BeSameAs(senderProvider.GetSender("ses"));
    }
}
