// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.AspNetCore;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HeadlessTestServerDeriveTests : TestBase
{
    private HeadlessTestServer<Program>? _server;

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_layer_variant_settings_over_base_settings()
    {
        // given
        var baseMarker = new BaseMarker();
        _server = new HeadlessTestServer<Program>(
            configureTestServices: services => services.AddSingleton(baseMarker),
            configureWebHost: builder =>
                builder.UseSetting("Variant:Setting", "base").UseSetting("Base:Setting", "base")
        );
        await _server.InitializeAsync();
        var variantMarker = new VariantMarker();

        // when
        await using var variant = await _server.DeriveAsync(
            services => services.AddSingleton(variantMarker),
            builder => builder.UseSetting("Variant:Setting", "variant")
        );

        // then
        variant.Services.GetRequiredService<BaseMarker>().Should().BeSameAs(baseMarker);
        variant.Services.GetRequiredService<VariantMarker>().Should().BeSameAs(variantMarker);
        var configuration = variant.Services.GetRequiredService<IConfiguration>();
        configuration["Variant:Setting"].Should().Be("variant");
        configuration["Base:Setting"].Should().Be("base");
        _server.Services.GetService<VariantMarker>().Should().BeNull();
    }

    [Fact]
    public async Task should_share_the_clock_with_the_variant()
    {
        // given
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        // when
        await using var variant = await _server.DeriveAsync();
        var advanced = _server.AdvanceTime(TimeSpan.FromHours(3));

        // then
        variant.TimeProvider.Should().BeSameAs(_server.TimeProvider);
        variant.Services.GetRequiredService<TimeProvider>().GetUtcNow().Should().Be(advanced);
    }

    [Fact]
    public async Task should_share_the_clock_when_the_base_services_construct_their_own()
    {
        // given
        _server = new HeadlessTestServer<Program>(configureTestServices: services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider())
        );
        await _server.InitializeAsync();

        // when
        await using var variant = await _server.DeriveAsync();
        var advanced = _server.AdvanceTime(TimeSpan.FromHours(1));

        // then
        variant.TimeProvider.Should().BeSameAs(_server.TimeProvider);
        variant.Services.GetRequiredService<TimeProvider>().GetUtcNow().Should().Be(advanced);
    }

    [Fact]
    public async Task should_share_a_clock_that_is_not_fake_with_the_variant()
    {
        // given
        _server = new HeadlessTestServer<Program>(configureTestServices: services =>
            services.AddSingleton(TimeProvider.System)
        );
        await _server.InitializeAsync();

        // when
        await using var variant = await _server.DeriveAsync();

        // then
        variant.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public async Task should_layer_variant_host_configuration_over_base_host_configuration()
    {
        // given
        var baseMarker = new BaseMarker();
        _server = new HeadlessTestServer<Program>(configureHost: builder =>
            builder
                .ConfigureServices(services => services.AddSingleton(baseMarker))
                .ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10))
        );
        await _server.InitializeAsync();
        var variantMarker = new VariantMarker();

        // when
        await using var variant = await _server.DeriveAsync(configureHost: builder =>
            builder
                .ConfigureServices(services => services.AddSingleton(variantMarker))
                .ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20))
        );

        // then
        variant.Services.GetRequiredService<BaseMarker>().Should().BeSameAs(baseMarker);
        variant.Services.GetRequiredService<VariantMarker>().Should().BeSameAs(variantMarker);
        variant
            .Services.GetRequiredService<IOptions<HostOptions>>()
            .Value.ShutdownTimeout.Should()
            .Be(TimeSpan.FromSeconds(20));
        _server.Services.GetService<VariantMarker>().Should().BeNull();
    }

    [Fact]
    public async Task should_share_the_base_clock_when_base_host_configuration_registers_its_own_clock()
    {
        // given - the delegate builds a new clock each time it runs, and the variant re-runs it
        _server = new HeadlessTestServer<Program>(configureHost: builder =>
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider()))
        );
        await _server.InitializeAsync();

        // when
        await using var variant = await _server.DeriveAsync();

        // then
        variant.Services.GetRequiredService<TimeProvider>().Should().BeSameAs(_server.TimeProvider);
    }

    [Fact]
    public async Task should_await_warmups_and_readiness_checks_in_the_variant()
    {
        // given
        var readinessRuns = 0;
        var warmup = new CountingWarmup();
        _server = new HeadlessTestServer<Program>(configureTestServices: services => services.AddWarmup(_ => warmup));
        _server.AddReadinessCheck(_ =>
        {
            Interlocked.Increment(ref readinessRuns);
            return Task.CompletedTask;
        });
        await _server.InitializeAsync();

        // when
        await using var variant = await _server.DeriveAsync();

        // then
        readinessRuns.Should().Be(2);
        warmup.Runs.Should().Be(2);
    }

    [Fact]
    public async Task should_leave_the_base_server_running_when_the_variant_is_disposed()
    {
        // given
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();
        var variant = await _server.DeriveAsync();

        // when
        await variant.DisposeAsync();

        // then
        using var client = _server.CreateClient();
        using var response = await client.GetAsync("/", AbortToken);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_throw_when_deriving_from_an_uninitialized_server()
    {
        // given
        _server = new HeadlessTestServer<Program>();

        // when
        var act = async () => await _server.DeriveAsync();

        // then
        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("*InitializeAsync*");
    }

    [Fact]
    public async Task should_throw_when_deriving_from_a_disposed_server()
    {
        // given
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();
        await _server.DisposeAsync();

        // when
        var act = async () => await _server.DeriveAsync();

        // then
        await act.Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task should_surface_variant_initialization_failure()
    {
        // given
        _server = new HeadlessTestServer<Program>(warmupTimeout: TimeSpan.FromMilliseconds(500));
        await _server.InitializeAsync();

        // when
        var act = async () => await _server.DeriveAsync(services => services.AddWarmup(_ => new FaultingWarmup()));

        // then
        await act.Should()
            .ThrowExactlyAsync<TimeoutException>()
            .WithMessage("*Faulting (last error: Initialization failed.)*");
    }

    private sealed class BaseMarker;

    private sealed class VariantMarker;

    private sealed class CountingWarmup : IWarmup
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public string Name => "Counting";

        public Task WarmUpAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);

            return Task.CompletedTask;
        }
    }

    private sealed class FaultingWarmup : IWarmup
    {
        public string Name => "Faulting";

        public Task WarmUpAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Initialization failed."));
    }
}
