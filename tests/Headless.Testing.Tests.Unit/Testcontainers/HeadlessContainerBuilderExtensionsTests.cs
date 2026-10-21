// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class HeadlessContainerBuilderExtensionsTests : TestBase
{
    [Fact]
    public void should_reuse_the_container_and_label_it_with_this_checkout()
    {
        var configuration = _Configuration(new ContainerBuilder("alpine:3").WithCheckoutReuse());

        configuration.Reuse.Should().BeTrue();
        configuration.Labels.Should().Contain(ReuseLabel.CheckoutKey, ReuseLabel.Checkout);
    }

    [Fact]
    public void should_keep_the_labels_the_caller_set_before()
    {
        var configuration = _Configuration(
            new ContainerBuilder("alpine:3").WithLabel("type", "features").WithCheckoutReuse()
        );

        configuration.Labels.Should().Contain("type", "features");
    }

    // The builder exposes its configuration only to subclasses, and ContainerBuilder is sealed. DeclaredOnly picks its
    // own typed override over the base builder's property of the same name.
    private static IContainerConfiguration _Configuration(ContainerBuilder builder)
    {
        return (IContainerConfiguration)
            typeof(ContainerBuilder)
                .GetProperty(
                    "DockerResourceConfiguration",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )!
                .GetValue(builder)!;
    }
}
