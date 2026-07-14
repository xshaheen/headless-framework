// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Headless.CommitCoordination.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Verifies the startup self-probe (<see cref="CommitInterceptorStartupGate{TContext}"/>) catches the silent
/// "transactional outbox enabled but the interceptor isn't firing" footgun: it passes when the interceptor is
/// auto-attached, and fails loud (Warn logs, Strict throws) when a deliberately-unwired DbContext is used.
/// </summary>
public sealed class CommitInterceptorStartupGateTests : TestBase
{
    [Fact]
    public async Task should_pass_when_the_interceptor_is_attached_via_the_options_configuration()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(AbortToken);

        await using var provider = _BuildProvider(connection, wireInterceptor: true);

        // when — the probe commits an empty transaction; the attached interceptor signals the commit edge.
        // then — no throw (the on-by-default wiring is healthy). A throw here fails the test.
        await _RunGateAsync(provider);
    }

    [Fact]
    public async Task should_throw_in_strict_mode_when_the_interceptor_is_not_attached()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(AbortToken);

        await using var provider = _BuildProvider(connection, wireInterceptor: false, mode: CommitProbeMode.Strict);

        // when — the interceptor never fires, so the commit edge is not observed.
        InvalidOperationException? captured = null;
        try
        {
            await _RunGateAsync(provider);
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        // then — strict mode fails the host start with an actionable message.
        captured.Should().NotBeNull();
        captured!.Message.Should().Contain("the commit interceptor did not");
    }

    [Fact]
    public async Task should_not_throw_in_warn_mode_when_the_interceptor_is_not_attached()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync(AbortToken);

        await using var provider = _BuildProvider(connection, wireInterceptor: false, mode: CommitProbeMode.Warn);

        // when — Warn is the default posture: surface the mis-wire but let the host start (relay recovers).
        // then — no throw. A throw here fails the test.
        await _RunGateAsync(provider);
    }

    [Fact]
    public async Task should_skip_probe_without_touching_services_when_disabled()
    {
        // Disabled mode is the opt-out: the gate must return before it resolves a scope or context, so a host that
        // turned the probe off pays no startup round-trip.
        var provider = new SpyServiceProvider();
        var gate = new CommitInterceptorStartupGate<GateTestDbContext>(
            provider,
            Options.Create(new CommitInterceptorProbeOptions { Mode = CommitProbeMode.Disabled }),
            NullLogger<CommitInterceptorStartupGate<GateTestDbContext>>.Instance
        );

        await gate.StartingAsync(AbortToken);

        provider.WasQueried.Should().BeFalse("Disabled mode returns before resolving anything");
    }

    [Fact]
    public async Task should_allow_startup_when_probe_is_inconclusive_even_in_strict_mode()
    {
        // A service provider that cannot create a scope models an unreachable/unresolvable probe environment. That
        // is inconclusive — NOT the interceptor-mis-wire signal — so even Strict mode must allow the host to start.
        var provider = new SpyServiceProvider();
        var gate = new CommitInterceptorStartupGate<GateTestDbContext>(
            provider,
            Options.Create(new CommitInterceptorProbeOptions { Mode = CommitProbeMode.Strict }),
            NullLogger<CommitInterceptorStartupGate<GateTestDbContext>>.Instance
        );

        var act = async () => await gate.StartingAsync(AbortToken);

        await act.Should().NotThrowAsync("an unreachable database is inconclusive, not a mis-wire");
        provider.WasQueried.Should().BeTrue("the inconclusive path must reach DI/probe resolution before bailing");
    }

    private static ServiceProvider _BuildProvider(
        SqliteConnection connection,
        bool wireInterceptor,
        CommitProbeMode mode = CommitProbeMode.Warn
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkCommitCoordination();

        if (wireInterceptor)
        {
            services.AddCommitCoordinationDbContextConfiguration(typeof(GateTestDbContext));
        }

        // Plain AddDbContext — the on-by-default path attaches the interceptor via the options configuration above,
        // never via the consumer's options action.
        services.AddDbContext<GateTestDbContext>(options => options.UseSqlite(connection));
        services.AddCommitInterceptorStartupGate(typeof(GateTestDbContext));
        services.Configure<CommitInterceptorProbeOptions>(o => o.Mode = mode);

        return services.BuildServiceProvider();
    }

    private static async Task _RunGateAsync(ServiceProvider provider)
    {
        var gate = provider
            .GetServices<IHostedService>()
            .OfType<IHostedLifecycleService>()
            .Single(s => s.GetType().Name.StartsWith("CommitInterceptorStartupGate", StringComparison.Ordinal));

        await gate.StartingAsync(AbortToken);
    }

    private sealed class GateTestDbContext(DbContextOptions<GateTestDbContext> options) : DbContext(options);

    // Records whether the probe touched DI, and resolves nothing — so CreateAsyncScope (which needs an
    // IServiceScopeFactory) fails, exercising the inconclusive path.
    private sealed class SpyServiceProvider : IServiceProvider
    {
        public bool WasQueried { get; private set; }

        public object? GetService(Type serviceType)
        {
            WasQueried = true;

            return null;
        }
    }
}
