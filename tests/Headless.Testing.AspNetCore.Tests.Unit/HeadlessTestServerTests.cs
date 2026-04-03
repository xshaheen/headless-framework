// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Abstractions;
using Headless.Testing.AspNetCore;
using Headless.Testing.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class HeadlessTestServerTests : IAsyncLifetime
{
    private HeadlessTestServer<Program>? _server;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    [Fact]
    public async Task should_start_and_resolve_services()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        _server.Services.Should().NotBeNull();
    }

    [Fact]
    public async Task should_create_working_http_client()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        using var client = _server.CreateClient();
        using var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("ok");
    }

    [Fact]
    public async Task should_auto_register_fake_time_provider()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        var resolved = _server.Services.GetRequiredService<TimeProvider>();

        resolved.Should().BeOfType<FakeTimeProvider>();
        resolved.Should().BeSameAs(_server.TimeProvider);
    }

    [Fact]
    public async Task should_auto_register_test_clock_as_iclock()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        var clock = _server.Services.GetRequiredService<IClock>();

        clock.Should().BeOfType<TestClock>();
    }

    [Fact]
    public async Task should_execute_scope_with_result()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        var resolved = await _server.ExecuteScopeAsync(sp =>
        {
            var tp = sp.GetRequiredService<TimeProvider>();
            return Task.FromResult(tp);
        });

        resolved.Should().BeOfType<FakeTimeProvider>();
    }

    [Fact]
    public async Task should_execute_scope_without_result()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        TimeProvider? captured = null;
        await _server.ExecuteScopeAsync(sp =>
        {
            captured = sp.GetRequiredService<TimeProvider>();
            return Task.CompletedTask;
        });

        captured.Should().BeOfType<FakeTimeProvider>();
    }

    [Fact]
    public async Task should_advance_time()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        var before = _server.TimeProvider.GetUtcNow();
        _server.AdvanceTime(TimeSpan.FromMinutes(5));
        var after = _server.TimeProvider.GetUtcNow();

        (after - before).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task should_set_time()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        var target = new DateTimeOffset(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);
        _server.SetTime(target);

        _server.TimeProvider.GetUtcNow().Should().Be(target);
    }

    [Fact]
    public async Task should_invoke_configure_test_services()
    {
        var marker = new MarkerService();
        _server = new HeadlessTestServer<Program>(configureTestServices: services => services.AddSingleton(marker));
        await _server.InitializeAsync();

        var resolved = _server.Services.GetRequiredService<MarkerService>();

        resolved.Should().BeSameAs(marker);
    }

    [Fact]
    public async Task should_run_readiness_check()
    {
        var checkRan = false;
        _server = new HeadlessTestServer<Program>();
        _server.WaitForReadiness(_ =>
        {
            checkRan = true;
            return Task.CompletedTask;
        });
        await _server.InitializeAsync();

        checkRan.Should().BeTrue();
    }

    [Fact]
    public async Task should_throw_timeout_when_readiness_check_exceeds_timeout()
    {
        _server = new HeadlessTestServer<Program>();
        _server.WaitForReadiness(_ => Task.Delay(TimeSpan.FromSeconds(30)), timeout: TimeSpan.FromMilliseconds(50));

        Func<Task> act = async () => await _server.InitializeAsync();

        await act.Should().ThrowExactlyAsync<TimeoutException>();
    }

    [Fact]
    public async Task should_be_safe_to_dispose_twice()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        await _server.DisposeAsync();
        await _server.DisposeAsync(); // Should not throw
    }

    [Fact]
    public async Task should_cleanup_factory_on_init_failure()
    {
        _server = new HeadlessTestServer<Program>();
        _server.WaitForReadiness(_ => Task.Delay(TimeSpan.FromSeconds(30)), timeout: TimeSpan.FromMilliseconds(50));

        Func<Task> act = async () => await _server.InitializeAsync();
        await act.Should().ThrowExactlyAsync<TimeoutException>();

        // After init failure, dispose should be safe (factory already cleaned up)
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task should_provide_independent_scopes_for_concurrent_calls()
    {
        _server = new HeadlessTestServer<Program>();
        await _server.InitializeAsync();

        var providers = new ConcurrentBag<IServiceProvider>();

        await Task.WhenAll(
            _server.ExecuteScopeAsync(sp =>
            {
                providers.Add(sp);
                return Task.CompletedTask;
            }),
            _server.ExecuteScopeAsync(sp =>
            {
                providers.Add(sp);
                return Task.CompletedTask;
            }),
            _server.ExecuteScopeAsync(sp =>
            {
                providers.Add(sp);
                return Task.CompletedTask;
            })
        );

        providers.Should().HaveCount(3);
        providers.Distinct().Should().HaveCount(3);
    }

    private sealed class MarkerService;
}
