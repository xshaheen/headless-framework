// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.RequestReply;

/// <summary>
/// Covers the startup gate that keeps a host which sends requests, or declares a responder, off a transport that has
/// no reply channel.
/// </summary>
public sealed class RequestReplyCapabilityTests : TestBase
{
    private const string _ResponderIdentity = "pricing.get-quote";

    [Fact]
    public async Task should_fail_bootstrap_naming_the_provider_when_a_responder_runs_on_a_transport_without_request_reply()
    {
        // given
        await using var provider = _CreateProvider(transport: "KafkaLike", withResponder: true);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        // when
        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage($"*'{_ResponderIdentity}'*'KafkaLike'*");
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_bootstrap_before_readiness_when_requests_are_enabled_on_a_transport_without_request_reply()
    {
        // given
        await using var provider = _CreateProvider(transport: "KafkaLike", requestsEnabled: true);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        // when
        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<MessagingConfigurationException>().WithMessage("*'KafkaLike'*");
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_start_without_requests_or_responders_on_a_transport_without_request_reply()
    {
        // given
        await using var provider = _CreateProvider(transport: "KafkaLike");
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        // when
        await bootstrapper.BootstrapAsync(AbortToken);

        // then
        bootstrapper.IsStarted.Should().BeTrue();
    }

    [Fact]
    public async Task should_start_with_requests_and_a_responder_on_the_in_memory_transport()
    {
        // given
        await using var provider = _CreateProvider(transport: null, requestsEnabled: true, withResponder: true);
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        // when
        await bootstrapper.BootstrapAsync(AbortToken);

        // then
        bootstrapper.IsStarted.Should().BeTrue();
        provider.GetService<IReplyTransport>().Should().NotBeNull();
    }

    // A non-null transport names a provider whose capability declaration, without request/reply support, replaces the
    // in-memory one; null keeps the in-memory declaration.
    private static ServiceProvider _CreateProvider(
        string? transport,
        bool requestsEnabled = false,
        bool withResponder = false
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
        });

        if (transport is not null)
        {
            var inMemoryTransport = services
                .Where(static descriptor =>
                    descriptor.ImplementationInstance
                        is MessagingProviderCapabilities { Role: MessagingProviderRole.Transport }
                )
                .ToArray();

            foreach (var descriptor in inMemoryTransport)
            {
                services.Remove(descriptor);
            }

            services.AddMessagingProviderCapabilities(
                MessagingProviderCapabilities.Transport(transport, [MessageLane.Bus, MessageLane.Queue], true)
            );
        }

        if (requestsEnabled)
        {
            services.AddSingleton(new RequestReplyMarkerService());
        }

        if (withResponder)
        {
            // No generated module registers responders yet, so the responder is seeded into the registry before the
            // host's registrations fold into it.
            services.AddSingleton(sp =>
            {
                var registry = new ConsumerRegistry();
                registry.Register(
                    new ConsumerMetadata(
                        typeof(GetQuote),
                        typeof(GetQuoteResponder),
                        _ResponderIdentity,
                        1,
                        MessageLane.Queue,
                        _ResponderIdentity,
                        MessageOptions.InitialContractVersion
                    )
                    {
                        ResponseType = typeof(Quote),
                    }
                );
                return SetupMessaging.BuildConsumerRegistry(sp, registry);
            });
        }

        return services.BuildServiceProvider();
    }

    private sealed record GetQuote(string Sku);

    private sealed record Quote(decimal Price);

    private sealed class GetQuoteResponder : IRespond<GetQuote, Quote>
    {
        public ValueTask<Quote> RespondAsync(ConsumeContext<GetQuote> context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new Quote(1m));
        }
    }
}
