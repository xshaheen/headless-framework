// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.AspNetCore;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task should_await_initializers_and_readiness_checks_in_the_variant()
    {
        // given
        var readinessRuns = 0;
        var initializer = new CountingInitializer();
        _server = new HeadlessTestServer<Program>(configureTestServices: services =>
            services.AddSingleton<IInitializer>(initializer)
        );
        _server.WaitForReadiness(_ =>
        {
            Interlocked.Increment(ref readinessRuns);
            return Task.CompletedTask;
        });
        await _server.InitializeAsync();

        // when
        await using var variant = await _server.DeriveAsync();

        // then
        readinessRuns.Should().Be(2);
        initializer.Waits.Should().Be(2);
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
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        // when
        var act = async () =>
            await _server.DeriveAsync(services => services.AddSingleton<IInitializer>(new FaultingInitializer()));

        // then
        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("*FaultingInitializer*");
    }

    private sealed class BaseMarker;

    private sealed class VariantMarker;

    private sealed class CountingInitializer : IInitializer
    {
        private int _waits;

        public int Waits => Volatile.Read(ref _waits);

        public bool IsInitialized => true;

        public Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _waits);
            return Task.CompletedTask;
        }
    }

    private sealed class FaultingInitializer : IInitializer
    {
        public bool IsInitialized => false;

        public Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException(new InvalidOperationException("Initialization failed."));
        }
    }
}
