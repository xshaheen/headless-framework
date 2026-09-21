// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Abstractions;

public sealed class HostIdentityAccessorTests
{
    private static readonly Guid _Guid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void should_prefer_configured_host_name()
    {
        // given
        var accessor = _Create(
            new HostIdentityOptions { HostName = "orders-worker-0" },
            env: _Pod("orders-7d", "prod")
        );

        // then
        accessor.HostName.Should().Be("orders-worker-0");
    }

    [Fact]
    public void should_use_pod_name_and_namespace_when_no_host_name_is_configured()
    {
        // given
        var accessor = _Create(env: _Pod("orders-7d", "prod"), machineName: () => "container-abc");

        // then the pod name is what operators see, not the container hostname a rollout regenerates
        accessor.HostName.Should().Be("prod/orders-7d");
    }

    [Fact]
    public void should_use_pod_name_without_namespace_when_namespace_is_absent()
    {
        // given
        var accessor = _Create(env: _Pod("orders-7d", podNamespace: null));

        // then
        accessor.HostName.Should().Be("orders-7d");
    }

    [Fact]
    public void should_use_machine_name_when_pod_name_is_absent()
    {
        // given
        var accessor = _Create(machineName: () => "host-a");

        // then
        accessor.HostName.Should().Be("host-a");
    }

    [Fact]
    public void should_generate_host_name_when_no_stable_source_exists()
    {
        // given
        var accessor = _Create(machineName: () => "");

        // then
        accessor.HostName.Should().Be("generated-11111111111111111111111111111111");
    }

    [Fact]
    public void should_embed_host_name_in_instance_id()
    {
        // given
        var accessor = _Create(machineName: () => "host-a");

        // then a value read from a log or a message names both the host and the incarnation
        accessor.InstanceId.Should().Be("host-a:11111111111111111111111111111111");
    }

    [Fact]
    public void should_prefer_configured_application_name_over_assembly_title()
    {
        // given
        var accessor = _Create(new HostIdentityOptions { ApplicationName = "Orders" });

        // then
        accessor.ApplicationName.Should().Be("Orders");
    }

    [Fact]
    public void should_fall_back_to_unknown_application_name_when_the_assembly_declares_no_title()
    {
        // given
        var build = Substitute.For<IBuildInformationAccessor>();
        build.GetTitle().Returns((string?)null);

        // when
        var accessor = _Create(build: build);

        // then
        accessor.ApplicationName.Should().Be("Unknown");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_a_blank_configured_host_name(string hostName)
    {
        // given
        var act = () => _Create(new HostIdentityOptions { HostName = hostName });

        // then a blank override would silently hide the discovered identity
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_register_a_single_instance_id_per_container()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessHostIdentity(options => options.ApplicationName = "Orders");

        using var provider = services.BuildServiceProvider();

        // when
        var first = provider.GetRequiredService<IHostIdentityAccessor>();
        var second = provider.GetRequiredService<IHostIdentityAccessor>();

        // then every subsystem stamping an origin agrees on one instance
        first.Should().BeSameAs(second);
        first.ApplicationName.Should().Be("Orders");
        first.InstanceId.Should().StartWith(first.HostName + ":");
    }

    [Fact]
    public void should_keep_an_earlier_registration_when_registered_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessHostIdentity(options => options.ApplicationName = "Host");
        services.AddHeadlessHostIdentity(options => options.ApplicationName = "Package");

        using var provider = services.BuildServiceProvider();

        // then a feature package calling it after the host cannot override the host's choice
        provider.GetRequiredService<IHostIdentityAccessor>().ApplicationName.Should().Be("Host");
    }

    private static HostIdentityAccessor _Create(
        HostIdentityOptions? options = null,
        IBuildInformationAccessor? build = null,
        Func<string, string?>? env = null,
        Func<string>? machineName = null
    )
    {
        if (build is null)
        {
            build = Substitute.For<IBuildInformationAccessor>();
            build.GetTitle().Returns("Title");
        }

        return new HostIdentityAccessor(
            options ?? new HostIdentityOptions(),
            build,
            new FixedGuidGenerator(_Guid),
            logger: null,
            env ?? (_ => null),
            machineName ?? (() => "")
        );
    }

    private static Func<string, string?> _Pod(string podName, string? podNamespace)
    {
        return name =>
            name switch
            {
                "POD_NAME" => podName,
                "POD_NAMESPACE" => podNamespace,
                _ => null,
            };
    }

    private sealed class FixedGuidGenerator(Guid guid) : IGuidGenerator
    {
        public Guid Create()
        {
            return guid;
        }
    }
}
