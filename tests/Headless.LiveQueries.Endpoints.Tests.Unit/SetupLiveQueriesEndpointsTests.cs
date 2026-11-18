// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Api;
using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupLiveQueriesEndpointsTests : TestBase
{
    [Fact]
    public void should_bound_streams_by_default()
    {
        // given
        using var provider = _Provider(setup => setup.UseEndpoints());

        // when
        var options = provider.GetRequiredService<IOptions<LiveEndpointsOptions>>().Value;

        // then
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(20));
        options.ReconnectDelay.Should().Be(TimeSpan.FromSeconds(3));
        options.MaxStreamsPerUser.Should().Be(50);
        options.MaxStreams.Should().Be(10_000);
        options.MaxConcurrentReruns.Should().Be(32);
        options.MinRerunInterval.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Theory]
    [InlineData(nameof(LiveEndpointsOptions.HeartbeatInterval), 0)]
    [InlineData(nameof(LiveEndpointsOptions.HeartbeatInterval), 2 * 60 * 60)]
    [InlineData(nameof(LiveEndpointsOptions.ReconnectDelay), -1)]
    [InlineData(nameof(LiveEndpointsOptions.MaxStreamsPerUser), 0)]
    [InlineData(nameof(LiveEndpointsOptions.MaxStreams), 0)]
    [InlineData(nameof(LiveEndpointsOptions.MaxConcurrentReruns), 0)]
    [InlineData(nameof(LiveEndpointsOptions.MinRerunInterval), -1)]
    public void should_reject_a_limit_outside_its_range(string option, int value)
    {
        // given
        using var provider = _Provider(setup =>
            setup.UseEndpoints(options =>
            {
                switch (option)
                {
                    case nameof(LiveEndpointsOptions.HeartbeatInterval):
                        options.HeartbeatInterval = TimeSpan.FromSeconds(value);
                        break;
                    case nameof(LiveEndpointsOptions.ReconnectDelay):
                        options.ReconnectDelay = TimeSpan.FromSeconds(value);
                        break;
                    case nameof(LiveEndpointsOptions.MaxStreamsPerUser):
                        options.MaxStreamsPerUser = value;
                        break;
                    case nameof(LiveEndpointsOptions.MaxStreams):
                        options.MaxStreams = value;
                        break;
                    case nameof(LiveEndpointsOptions.MaxConcurrentReruns):
                        options.MaxConcurrentReruns = value;
                        break;
                    default:
                        options.MinRerunInterval = TimeSpan.FromSeconds(value);
                        break;
                }
            })
        );

        // when
        var reading = () => provider.GetRequiredService<IOptions<LiveEndpointsOptions>>().Value;

        // then
        reading.Should().Throw<OptionsValidationException>().WithMessage($"*{option}*");
    }

    [Fact]
    public void should_bind_the_options_from_configuration()
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["MaxStreamsPerUser"] = "7",
                    ["HeartbeatInterval"] = "00:00:05",
                }
            )
            .Build();
        using var provider = _Provider(setup => setup.UseEndpoints(configuration));

        // when
        var options = provider.GetRequiredService<IOptions<LiveEndpointsOptions>>().Value;

        // then
        options.MaxStreamsPerUser.Should().Be(7);
        options.HeartbeatInterval.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void should_configure_the_options_with_the_service_provider()
    {
        // given
        using var provider = _Provider(
            setup =>
                setup.UseEndpoints(
                    (options, services) => options.MaxStreams = services.GetRequiredService<Limit>().Value
                ),
            services => services.AddSingleton(new Limit(123))
        );

        // when
        var options = provider.GetRequiredService<IOptions<LiveEndpointsOptions>>().Value;

        // then
        options.MaxStreams.Should().Be(123);
    }

    [Fact]
    public async Task should_fail_when_the_endpoints_are_built_and_live_endpoints_are_not_registered()
    {
        // given - live queries without the endpoints package's registration
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHeadlessLiveQueries(_ => { });
        await using var app = builder.Build();
        app.MapGet("/todos", () => new[] { "milk" }).WithLiveUpdates();

        // when
        var building = () => ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToList();

        // then
        building.Should().Throw<InvalidOperationException>().WithMessage("*/todos*UseEndpoints()*");
    }

    [Fact]
    public async Task should_mark_only_the_live_endpoint_for_api_documentation()
    {
        // given
        await using var app = await LiveTestApp.StartAsync(
            app =>
            {
                app.MapGet("/todos", () => new[] { "milk" }).WithLiveUpdates();
                app.MapGet("/plain", () => new[] { "milk" });
            },
            AbortToken
        );

        // when
        var endpoints = ((IEndpointRouteBuilder)app.App)
            .DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToDictionary(endpoint => endpoint.RoutePattern.RawText!, StringComparer.Ordinal);

        // then
        endpoints["/todos"].Metadata.GetMetadata<LiveUpdatesMetadata>().Should().NotBeNull();
        endpoints["/plain"].Metadata.GetMetadata<LiveUpdatesMetadata>().Should().BeNull();
    }

    [Fact]
    public async Task should_fail_the_start_of_a_host_that_cannot_write_problem_details()
    {
        // given - live endpoints on a host that registered no problem-details creator
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHeadlessLiveQueries(setup => setup.UseEndpoints());
        await using var app = builder.Build();

        // when
        var starting = () => app.StartAsync(AbortToken);

        // then
        await starting.Should().ThrowAsync<Exception>().WithMessage("*IProblemDetailsCreator*Headless live endpoints*");
    }

    [Fact]
    public void should_wrap_the_accessor_the_host_registered_before_live_endpoints()
    {
        // given - a host with an accessor of its own
        var hosts = new FixedHttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(hosts);
        services.AddHeadlessLiveQueries(setup => setup.UseEndpoints());
        using var provider = services.BuildServiceProvider();

        // when
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var read = accessor.HttpContext;
        var assigned = new DefaultHttpContext();
        accessor.HttpContext = assigned;

        // then - outside a run it reads and writes the host's accessor
        accessor.Should().NotBeSameAs(hosts);
        read.Should().NotBeNull();
        hosts.HttpContext.Should().BeSameAs(assigned);
    }

    [Fact]
    public void should_keep_wrapping_the_accessor_when_the_host_adds_the_default_one_afterwards()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessLiveQueries(setup => setup.UseEndpoints());
        services.AddHttpContextAccessor();
        using var provider = services.BuildServiceProvider();

        // when
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();

        // then
        services.Count(descriptor => descriptor.ServiceType == typeof(IHttpContextAccessor)).Should().Be(1);
        accessor.Should().NotBeOfType<HttpContextAccessor>();
    }

    [Fact]
    public void should_wrap_the_accessor_once_when_live_endpoints_are_registered_twice()
    {
        // given - an accessor that counts how often an assignment reaches it
        var hosts = new FixedHttpContextAccessor();
        var services = new ServiceCollection();
        services.AddSingleton<IHttpContextAccessor>(hosts);
        services.AddHeadlessLiveQueries(setup => setup.UseEndpoints().UseEndpoints());
        services.AddHeadlessLiveQueries(setup => setup.UseEndpoints());
        using var provider = services.BuildServiceProvider();

        // when
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();

        // then - one wrapper stands between the container and the host's accessor
        accessor
            .GetType()
            .GetFields(_InstanceFields)
            .Select(field => field.GetValue(accessor))
            .Should()
            .Contain(hosts);
    }

    [Fact]
    public async Task should_fail_the_start_of_a_host_that_replaces_the_accessor_after_live_endpoints()
    {
        // given - an accessor registered after live endpoints wrapped the one before it
        var starting = () =>
            LiveTestApp.StartAsync(
                app => app.MapGet("/todos", () => new[] { "milk" }).WithLiveUpdates(),
                AbortToken,
                configureBuilder: builder =>
                    builder.Services.AddSingleton<IHttpContextAccessor, FixedHttpContextAccessor>()
            );

        // when, then
        await starting
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*IHttpContextAccessor*FixedHttpContextAccessor*before AddHeadlessLiveQueries*");
    }

    private const BindingFlags _InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static ServiceProvider _Provider(
        Action<HeadlessLiveQueriesSetupBuilder> configure,
        Action<IServiceCollection>? configureServices = null
    )
    {
        var services = new ServiceCollection();
        services.AddHeadlessLiveQueries(configure);
        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private sealed record Limit(int Value);

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
