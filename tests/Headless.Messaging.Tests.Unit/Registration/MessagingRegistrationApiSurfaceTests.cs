// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests.Registration;

public sealed class MessagingRegistrationApiSurfaceTests : TestBase
{
    [Fact]
    public void no_fluent_call_declares_a_consumer()
    {
        // given
        const BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Public;
        var registrationSurfaces = new[] { typeof(MessagingSetupBuilder), typeof(MessagingContributionBuilder) };

        // then: consumers declare themselves with an attribute, so the fluent surfaces only add modules, declare
        // message contracts, and tune declared consumers.
        registrationSurfaces
            .SelectMany(static type => type.GetProperties(publicInstance))
            .Select(static property => property.Name)
            .Should()
            .NotContain(["Bus", "Queue"]);
        registrationSurfaces
            .SelectMany(static type => type.GetMethods(publicInstance))
            .Select(static method => method.Name)
            .Should()
            .NotContain(["ForMessage", "Consumer", "AddConsumer", "ForConsumersFromAssembly"]);
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
}
