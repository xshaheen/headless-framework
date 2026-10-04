// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
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

    [Fact]
    public async Task should_carry_a_module_declared_responder_response_type_to_its_metadata_and_executor_descriptor()
    {
        // given
        await using var provider = _CreateProvider(transport: null, requestsEnabled: true, withResponder: true);

        // when
        var metadata = provider.GetRequiredService<ConsumerRegistry>().GetAll().Single();
        var descriptor = provider.GetRequiredService<IConsumerServiceSelector>().SelectCandidates().Single();

        // then
        metadata.ResponseType.Should().Be<Quote>();
        metadata.IsResponder.Should().BeTrue();
        metadata.Lane.Should().Be(MessageLane.Queue);
        descriptor.ResponseType.Should().Be<Quote>();
        descriptor.IsResponder.Should().BeTrue();
        descriptor.MethodName.Should().Be(nameof(IRespond<,>.RespondAsync));
    }

    [Fact]
    public async Task should_fail_bootstrap_naming_the_message_when_its_name_is_inside_the_reply_namespace()
    {
        // given
        await using var provider = _CreateProvider(
            transport: null,
            declare: static messaging => messaging.Message<GetQuote>("headless.reply.quotes")
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        // when
        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage($"*'headless.reply.quotes'*{typeof(GetQuote).FullName}*'{ReplyAddresses.Prefix}'*");
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_fail_bootstrap_when_the_message_name_prefix_moves_a_responder_into_the_reply_namespace()
    {
        // given
        await using var provider = _CreateProvider(
            transport: null,
            withResponder: true,
            messageNamePrefix: "headless.reply"
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        // when
        var act = () => bootstrapper.BootstrapAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<MessagingConfigurationException>()
            .WithMessage($"*'{ReplyAddresses.Prefix}*{nameof(MessagingOptions.MessageNamePrefix)}*");
        bootstrapper.IsStarted.Should().BeFalse();
    }

    [Fact]
    public async Task should_start_when_a_message_name_only_resembles_the_reply_namespace()
    {
        // given
        await using var provider = _CreateProvider(
            transport: null,
            declare: static messaging => messaging.Message<GetQuote>("headless.replyish.quotes")
        );
        var bootstrapper = provider.GetRequiredService<IBootstrapper>();

        // when
        await bootstrapper.BootstrapAsync(AbortToken);

        // then
        bootstrapper.IsStarted.Should().BeTrue();
    }

    // A non-null transport names a provider whose capability declaration, without request/reply support, replaces the
    // in-memory one; null keeps the in-memory declaration.
    private static ServiceProvider _CreateProvider(
        string? transport,
        bool requestsEnabled = false,
        bool withResponder = false,
        Action<MessagingContributionBuilder>? declare = null,
        string? messageNamePrefix = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (declare is not null)
        {
            services.ConfigureMessaging(declare);
        }

        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            setup.Options.MessageNamePrefix = messageNamePrefix;

            if (withResponder)
            {
                setup.AddModule<ResponderModule>();
            }
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

        return services.BuildServiceProvider();
    }

    // Declares the responder the way a generated module does, so the response type reaches the consumer registry through
    // the same catalog path.
    private sealed class ResponderModule : IMessagingModule
    {
        public static void Register(MessagingCatalogBuilder catalog)
        {
            catalog.AddQueueResponder<GetQuoteResponder, GetQuote, Quote>(
                _ResponderIdentity,
                static (_, _, _) => ValueTask.CompletedTask
            );
        }
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
