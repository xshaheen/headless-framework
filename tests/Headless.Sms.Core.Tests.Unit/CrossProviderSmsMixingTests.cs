// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Runtime;
using Headless.Sms;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Cross-cutting tests that a single <c>AddHeadlessSms</c> call can compose a default sender with named
/// instances from every provider — each owning its own keyed backend and options — without DI collisions,
/// and that the same provider registered twice sends with its own configuration (through the named
/// HttpClient stub, proving real options binding + keyed resolution end to end).
/// </summary>
public sealed class CrossProviderSmsMixingTests : TestBase
{
    private static IConfiguration _Config(params (string Key, string Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.Select(static item => new KeyValuePair<string, string?>(item.Key, item.Value))
            )
            .Build();
    }

    [Fact]
    public void should_register_default_and_heterogeneous_named_providers_without_collision()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var awsOptions = new AWSOptions
        {
            Region = RegionEndpoint.USEast1,
            Credentials = new BasicAWSCredentials("fake-access-key", "fake-secret-key"),
        };

        // when - a default Noop plus one named instance per provider, all in a single call.
        services.AddHeadlessSms(setup =>
        {
            setup.UseNoop();
            setup.AddNamed("sink", static instance => instance.UseDevelopment("out.txt"));
            setup.AddNamed(
                "cequens",
                instance =>
                    instance.UseCequens(_Config(("ApiKey", "api-key"), ("UserName", "user"), ("SenderName", "SENDER")))
            );
            setup.AddNamed(
                "connekio",
                instance =>
                    instance.UseConnekio(
                        _Config(
                            ("Sender", "SENDER"),
                            ("AccountId", "account"),
                            ("UserName", "user"),
                            ("Password", "password")
                        )
                    )
            );
            setup.AddNamed(
                "infobip",
                instance =>
                    instance.UseInfobip(
                        _Config(("Sender", "SENDER"), ("ApiKey", "api-key"), ("BasePath", "https://api.infobip.test"))
                    )
            );
            setup.AddNamed(
                "victorylink",
                instance =>
                    instance.UseVictoryLink(
                        _Config(("Sender", "SENDER"), ("UserName", "user"), ("Password", "password"))
                    )
            );
            setup.AddNamed(
                "vodafone",
                instance =>
                    instance.UseVodafone(
                        _Config(
                            ("Sender", "SENDER"),
                            ("AccountId", "account"),
                            ("Password", "password"),
                            ("SecureHash", "hash")
                        )
                    )
            );
            setup.AddNamed(
                "twilio",
                instance =>
                    instance.UseTwilio(
                        _Config(
                            ("Sid", "AC0000000000000000000000000000000"),
                            ("AuthToken", "token"),
                            ("PhoneNumber", "+15551234567")
                        )
                    )
            );
            setup.AddNamed("sns", instance => instance.UseAwsSns(_Config(("SenderId", "SENDER")), awsOptions));
        });
        using var provider = services.BuildServiceProvider();

        // then - the default and every named sender resolve to their own provider type. Sender types are
        // internal to their packages, so assert by runtime type name.
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();
        provider.GetRequiredService<ISmsSender>().GetType().Name.Should().Be("NoopSmsSender");
        senderProvider.GetSender("sink").GetType().Name.Should().Be("DevSmsSender");
        senderProvider.GetSender("cequens").GetType().Name.Should().Be("CequensSmsSender");
        senderProvider.GetSender("connekio").GetType().Name.Should().Be("ConnekioSmsSender");
        senderProvider.GetSender("infobip").GetType().Name.Should().Be("InfobipSmsSender");
        senderProvider.GetSender("victorylink").GetType().Name.Should().Be("VictoryLinkSmsSender");
        senderProvider.GetSender("vodafone").GetType().Name.Should().Be("VodafoneSmsSender");
        senderProvider.GetSender("twilio").GetType().Name.Should().Be("TwilioSmsSender");
        senderProvider.GetSender("sns").GetType().Name.Should().Be("AwsSnsSmsSender");

        // keyed resolution stays in sync with the factory for every name.
        foreach (
            var name in (string[])["sink", "cequens", "connekio", "infobip", "victorylink", "vodafone", "twilio", "sns"]
        )
        {
            provider.GetRequiredKeyedService<ISmsSender>(name).Should().BeSameAs(senderProvider.GetSender(name));
        }

        // bulk capability per name: bulk-capable providers forward, single-only providers do not (AE4).
        foreach (var name in (string[])["sink", "cequens", "connekio", "infobip", "victorylink", "vodafone"])
        {
            var sender = senderProvider.GetSender(name);
            sender.Should().BeAssignableTo<IBulkSmsSender>();
            provider.GetRequiredKeyedService<IBulkSmsSender>(name).Should().BeSameAs(sender);
        }

        foreach (var name in (string[])["twilio", "sns"])
        {
            senderProvider.GetSender(name).Should().NotBeAssignableTo<IBulkSmsSender>();
            provider.GetKeyedService<IBulkSmsSender>(name).Should().BeNull();
        }
    }

    [Fact]
    public async Task same_provider_twice_should_send_with_its_own_configuration()
    {
        // given - AE1: the same provider registered under two names with different endpoints and sender ids;
        // each named HttpClient's transport is stubbed by its exact name, so a send proves the whole chain:
        // factory -> keyed sender -> named options -> named HttpClient.
        var services = new ServiceCollection();
        services.AddLogging();
        var otpStub = services.StubSmsHttpClient(
            "Headless:VodafoneSms:otp",
            responseBody: "<Response><Success>true</Success></Response>"
        );
        var marketingStub = services.StubSmsHttpClient(
            "Headless:VodafoneSms:marketing",
            responseBody: "<Response><Success>true</Success></Response>"
        );

        services.AddHeadlessSms(setup =>
        {
            setup.UseNoop();
            setup.AddNamed(
                "otp",
                instance =>
                    instance.UseVodafone(
                        _Config(
                            ("SendSmsEndpoint", "https://otp.example.test/sms"),
                            ("Sender", "OTP-SENDER"),
                            ("AccountId", "otp-account"),
                            ("Password", "password"),
                            ("SecureHash", "hash")
                        )
                    )
            );
            setup.AddNamed(
                "marketing",
                instance =>
                    instance.UseVodafone(
                        _Config(
                            ("SendSmsEndpoint", "https://marketing.example.test/sms"),
                            ("Sender", "MKT-SENDER"),
                            ("AccountId", "marketing-account"),
                            ("Password", "password"),
                            ("SecureHash", "hash")
                        )
                    )
            );
        });
        await using var provider = services.BuildServiceProvider();
        var senderProvider = provider.GetRequiredService<ISmsSenderProvider>();

        // when
        var otpResult = await senderProvider.GetSender("otp").SendAsync(SmsRequests.Single(), AbortToken);
        var marketingResult = await senderProvider.GetSender("marketing").SendAsync(SmsRequests.Single(), AbortToken);

        // then - each send succeeded through its own named client, hitting its own endpoint with its own
        // sender id; nothing leaked across the two instances.
        otpResult.Success.Should().BeTrue();
        marketingResult.Success.Should().BeTrue();

        var otpRequest = otpStub.Requests.Should().ContainSingle().Which;
        otpRequest.Uri.Should().Be(new Uri("https://otp.example.test/sms"));
        otpRequest.Body.Should().Contain("OTP-SENDER").And.Contain("otp-account").And.NotContain("MKT-SENDER");

        var marketingRequest = marketingStub.Requests.Should().ContainSingle().Which;
        marketingRequest.Uri.Should().Be(new Uri("https://marketing.example.test/sms"));
        marketingRequest
            .Body.Should()
            .Contain("MKT-SENDER")
            .And.Contain("marketing-account")
            .And.NotContain("OTP-SENDER");
    }
}
