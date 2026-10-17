// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class InMemoryTransportEnvironmentWarningTests : TestBase
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task should_warn_when_the_transport_runs_outside_development_and_test(string environmentName)
    {
        // given
        var logger = _EnabledLogger();
        var warning = new InMemoryTransportEnvironmentWarning(_Environment(environmentName), logger);

        // when
        await warning.StartAsync(AbortToken);

        // then
        logger
            .Received(1)
            .Log(
                LogLevel.Warning,
                Arg.Is<EventId>(id => id.Id == 3013),
                Arg.Any<Arg.AnyType>(),
                null,
                Arg.Any<Func<Arg.AnyType, Exception?, string>>()
            );
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public async Task should_stay_quiet_in_development_and_test(string environmentName)
    {
        // given
        var logger = _EnabledLogger();
        var warning = new InMemoryTransportEnvironmentWarning(_Environment(environmentName), logger);

        // when
        await warning.StartAsync(AbortToken);

        // then
        logger
            .DidNotReceive()
            .Log(
                Arg.Any<LogLevel>(),
                Arg.Any<EventId>(),
                Arg.Any<Arg.AnyType>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<Arg.AnyType, Exception?, string>>()
            );
    }

    [Fact]
    public void should_register_the_warning_as_a_hosted_service_when_use_in_memory()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessMessaging(options => options.UseInMemory());

        // then
        services
            .Should()
            .ContainSingle(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(InMemoryTransportEnvironmentWarning)
            );
    }

    private static ILogger<InMemoryTransportEnvironmentWarning> _EnabledLogger()
    {
        var logger = Substitute.For<ILogger<InMemoryTransportEnvironmentWarning>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        return logger;
    }

    private static IHostEnvironment _Environment(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        return environment;
    }
}
