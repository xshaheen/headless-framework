// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Headless.Emails;
using Headless.Emails.Aws;
using Headless.Emails.Dev;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SetupAwsSesNamedEmailTests
{
    // Region + explicit (fake) credentials so the SES client constructs deterministically without any
    // ambient AWS configuration or network access.
    private static AWSOptions _AwsOptions() =>
        new()
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("fake-access-key", "fake-secret-key"),
        };

    [Fact]
    public void should_resolve_named_aws_sender_alongside_default_noop()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("ses", instance => instance.UseAwsSes(_AwsOptions()));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IEmailSender>().Should().BeOfType<NoopEmailSender>();
        provider.GetRequiredKeyedService<IEmailSender>("ses").Should().BeOfType<AwsSesEmailSender>();
        provider.GetRequiredService<IEmailSenderProvider>().GetSender("ses").Should().BeOfType<AwsSesEmailSender>();
    }

    [Fact]
    public void should_isolate_ses_clients_across_two_named_instances()
    {
        // given - distinct regions per instance so the test proves each keyed client is built from its own
        // options, not merely that the two client instances differ.
        var services = new ServiceCollection();
        services.AddLogging();
        var east = new AWSOptions
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("fake-access-key", "fake-secret-key"),
        };
        var west = new AWSOptions
        {
            Region = RegionEndpoint.EUWest1,
            Credentials = new BasicAWSCredentials("fake-access-key", "fake-secret-key"),
        };

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("ses1", instance => instance.UseAwsSes(east));
            setup.AddNamed("ses2", instance => instance.UseAwsSes(west));
        });
        using var provider = services.BuildServiceProvider();

        // then
        var client1 = provider.GetRequiredKeyedService<IAmazonSimpleEmailServiceV2>("ses1");
        var client2 = provider.GetRequiredKeyedService<IAmazonSimpleEmailServiceV2>("ses2");
        client1.Should().NotBeSameAs(client2);
        client1.Config.RegionEndpoint.SystemName.Should().Be("us-east-1");
        client2.Config.RegionEndpoint.SystemName.Should().Be("eu-west-1");
    }

    [Fact]
    public void should_resolve_named_aws_sender_with_null_options_from_ambient_aws_options()
    {
        // given - parity with the default path: null options falls back to the ambient AWSOptions in DI.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDefaultAWSOptions(_AwsOptions());

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("ses", instance => instance.UseAwsSes(null));
        });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredKeyedService<IEmailSender>("ses").Should().BeOfType<AwsSesEmailSender>();
    }

    [Fact]
    public void should_build_named_ses_client_from_iconfiguration_when_options_null()
    {
        // given - AWS:Region in IConfiguration and credentials via the standard chain (env), with no ambient
        // AWSOptions. The named null-options path must read IConfiguration just like TryAddAWSService(null).
        var previousKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var previousSecret = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "fake-access-key");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "fake-secret-key");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["AWS:Region"] = "eu-west-1" }
                )
                .Build();
            services.AddSingleton<IConfiguration>(configuration);

            // when
            services.AddHeadlessEmails(setup =>
            {
                setup.UseNoop();
                setup.AddNamed("ses", instance => instance.UseAwsSes(null));
            });
            using var provider = services.BuildServiceProvider();

            // then - the keyed client picks up AWS:Region from IConfiguration.
            var client = provider.GetRequiredKeyedService<IAmazonSimpleEmailServiceV2>("ses");
            client.Config.RegionEndpoint.SystemName.Should().Be("eu-west-1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", previousKey);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", previousSecret);
        }
    }
}
