// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Constants;
using Microsoft.Extensions.Hosting;

namespace Tests.Environments;

public sealed class HostEnvironmentExtensionsTests
{
    [Fact]
    public void should_return_true_when_environment_is_test()
    {
        // given
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(EnvironmentNames.Test);

        // when
        var result = hostEnvironment.IsTest();

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_when_environment_is_not_test()
    {
        // given
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(EnvironmentNames.Development);

        // when
        var result = hostEnvironment.IsTest();

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_true_when_environment_is_development()
    {
        // given
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(EnvironmentNames.Development);

        // when
        var result = hostEnvironment.IsDevelopmentOrTest();

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_true_when_environment_is_test_for_dev_or_test()
    {
        // given
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(EnvironmentNames.Test);

        // when
        var result = hostEnvironment.IsDevelopmentOrTest();

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_when_environment_is_production()
    {
        // given
        var hostEnvironment = Substitute.For<IHostEnvironment>();
        hostEnvironment.EnvironmentName.Returns(EnvironmentNames.Production);

        // when
        var result = hostEnvironment.IsDevelopmentOrTest();

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_throw_when_host_environment_is_null()
    {
        // given
        IHostEnvironment hostEnvironment = null!;

        // when
        var act = () => hostEnvironment.IsTest();

        // then
        act.Should().Throw<ArgumentNullException>().WithParameterName("hostEnvironment");
    }
}
