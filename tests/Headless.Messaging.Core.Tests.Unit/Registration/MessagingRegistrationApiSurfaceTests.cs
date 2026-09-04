// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;
using Headless.Testing.Tests;

namespace Tests.Registration;

public sealed class MessagingRegistrationApiSurfaceTests : TestBase
{
    [Fact]
    public void setup_exposes_bus_and_queue_as_the_only_message_registration_roots()
    {
        // given
        var setupType = typeof(MessagingSetupBuilder);

        // then
        const BindingFlags publicInstanceDeclared =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
        setupType.GetProperty(nameof(MessagingSetupBuilder.Bus), publicInstanceDeclared).Should().NotBeNull();
        setupType.GetProperty(nameof(MessagingSetupBuilder.Queue), publicInstanceDeclared).Should().NotBeNull();

        var laneFreeMethods = typeof(SetupMessaging)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(static method => method.Name.StartsWith("ForMessage", StringComparison.Ordinal));

        laneFreeMethods.Should().BeEmpty();
    }

    [Fact]
    public void registration_surface_has_no_lane_switching_terminals_or_shared_message_builder()
    {
        // given
        var assembly = typeof(IScannedConsumerBuilder).Assembly;
        var publicRegistrationTypes = assembly
            .GetExportedTypes()
            .Where(static type =>
                string.Equals(type.Namespace, typeof(IScannedConsumerBuilder).Namespace, StringComparison.Ordinal)
            )
            .ToArray();

        // then
        publicRegistrationTypes
            .SelectMany(static type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            .Select(static method => method.Name)
            .Should()
            .NotContain(["OnBus", "OnQueue"]);

        publicRegistrationTypes
            .Select(static type => type.FullName)
            .Should()
            .NotContain(
                "Headless.Messaging.Registration.IMessageBuilder`1",
                "Headless.Messaging.Registration.IMessagingRegistrationContributor"
            );
    }

    [Fact]
    public void message_registration_owns_its_lane()
    {
        // then
        typeof(MessageRegistration)
            .GetProperty(
                nameof(MessageRegistration.Lane),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly
            )
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void message_scoped_middleware_registration_requires_an_explicit_lane()
    {
        // given
        var messageScopedMethods = typeof(MessagingBuilder)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(static method =>
                method.Name
                    is nameof(MessagingBuilder.AddPublishMiddlewareFor)
                        or nameof(MessagingBuilder.AddConsumeMiddlewareFor)
            )
            .ToArray();

        // then
        messageScopedMethods.Should().NotBeEmpty();
        messageScopedMethods
            .Should()
            .AllSatisfy(method =>
                method.GetParameters().Should().Contain(parameter => parameter.ParameterType == typeof(MessageLane))
            );
    }

    [Fact]
    public void durable_consumer_builders_expose_explicit_identity_and_contract_version()
    {
        var genericMethods = typeof(IConsumerBuilderBase<,>).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        var scannedMethods = typeof(IScannedConsumerBuilder).GetMethods(BindingFlags.Instance | BindingFlags.Public);

        genericMethods.Should().Contain(method => method.Name == "ConsumerIdentity");
        genericMethods.Should().Contain(method => method.Name == "ContractVersion");
        scannedMethods.Should().Contain(method => method.Name == "ConsumerIdentity");
        scannedMethods.Should().Contain(method => method.Name == "ContractVersion");
    }

    [Theory]
    [InlineData(typeof(IBusMessageBuilder<>))]
    [InlineData(typeof(IQueueMessageBuilder<>))]
    public void direct_consumer_registration_requires_an_explicit_contract_callback(Type builderType)
    {
        var consumerMethod = builderType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == "Consumer");

        consumerMethod.GetParameters().Should().ContainSingle();
    }

    [Theory]
    [InlineData(typeof(IBusRegistrationBuilder))]
    [InlineData(typeof(IQueueRegistrationBuilder))]
    public void assembly_scanning_requires_an_explicit_consumer_contract_callback(Type builderType)
    {
        var scanMethods = builderType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name.StartsWith("ForConsumersFromAssembly", StringComparison.Ordinal))
            .ToArray();

        scanMethods.Should().HaveCount(2);
        scanMethods.Single(method => method.Name == "ForConsumersFromAssembly").GetParameters().Should().HaveCount(2);
        scanMethods
            .Single(method => method.Name == "ForConsumersFromAssemblyContaining")
            .GetParameters()
            .Should()
            .ContainSingle();
    }
}
