// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Headless.Emails;
using Headless.Emails.Aws;
using Headless.Emails.Dev;
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
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessEmails(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("ses1", instance => instance.UseAwsSes(_AwsOptions()));
            setup.AddNamed("ses2", instance => instance.UseAwsSes(_AwsOptions()));
        });
        using var provider = services.BuildServiceProvider();

        // then
        var client1 = provider.GetRequiredKeyedService<IAmazonSimpleEmailServiceV2>("ses1");
        var client2 = provider.GetRequiredKeyedService<IAmazonSimpleEmailServiceV2>("ses2");
        client1.Should().NotBeSameAs(client2);
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
}
