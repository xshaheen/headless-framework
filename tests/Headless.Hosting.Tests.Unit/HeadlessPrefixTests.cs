// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class HeadlessPrefixTests : TestBase
{
    [Fact]
    public void should_read_the_prefix_from_configuration_with_no_code()
    {
        // given
        var services = _CreateServices(("Headless:Prefix", "wt1"));

        // when
        services.AddHeadlessPrefix();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<HeadlessPrefixOptions>>().Value.Prefix.Should().Be("wt1");
    }

    [Fact]
    public void should_have_an_empty_prefix_when_nothing_sets_one()
    {
        // given
        var services = _CreateServices();

        // when
        services.AddHeadlessPrefix();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<HeadlessPrefixOptions>>().Value.Prefix.Should().BeEmpty();
    }

    [Fact]
    public void should_let_code_win_over_configuration_when_code_is_registered_first()
    {
        // given
        var services = _CreateServices(("Headless:Prefix", "a"));
        services.ConfigureHeadlessPrefix("b");

        // when
        services.AddHeadlessPrefix();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<HeadlessPrefixOptions>>().Value.Prefix.Should().Be("b");
    }

    [Fact]
    public void should_let_code_win_over_configuration_when_code_is_registered_last()
    {
        // given
        var services = _CreateServices(("Headless:Prefix", "a"));
        services.AddHeadlessPrefix();

        // when
        services.ConfigureHeadlessPrefix("b");
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<HeadlessPrefixOptions>>().Value.Prefix.Should().Be("b");
    }

    [Fact]
    public void should_register_the_binding_once_however_many_features_ask_for_it()
    {
        // given
        var services = _CreateServices(("Headless:Prefix", "wt1"));

        // when
        services.AddHeadlessPrefix();
        services.AddHeadlessPrefix();
        services.AddHeadlessPrefix();

        // then
        services.Count(static x => x.ServiceType == typeof(IConfigureOptions<HeadlessPrefixOptions>)).Should().Be(1);
        services
            .Count(static x => x.ServiceType == typeof(FluentValidation.IValidator<HeadlessPrefixOptions>))
            .Should()
            .Be(1);
        services
            .Count(static x =>
                x.ServiceType == typeof(IHostedService)
                && string.Equals(x.ImplementationType?.Name, "HeadlessPrefixStartupLog", StringComparison.Ordinal)
            )
            .Should()
            .Be(1);
    }

    [Theory]
    [InlineData("WT1")]
    [InlineData("wt.1")]
    [InlineData("wt_1")]
    [InlineData("wt:1")]
    [InlineData("-wt1")]
    [InlineData("wt1-")]
    [InlineData("w t1")]
    [InlineData("abcdefghijklmnopqrstu")]
    public void should_reject_a_prefix_outside_the_allowed_shape(string prefix)
    {
        // given
        var services = _CreateServices(("Headless:Prefix", prefix));
        services.AddHeadlessPrefix();
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<HeadlessPrefixOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>().WithMessage("*Headless:Prefix*");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("wt1")]
    [InlineData("feat-shared-infra")]
    [InlineData("abcdefghijklmnopqrst")]
    public void should_accept_a_prefix_inside_the_allowed_shape(string prefix)
    {
        // given
        var services = _CreateServices(("Headless:Prefix", prefix));
        services.AddHeadlessPrefix();
        using var provider = services.BuildServiceProvider();

        // when
        var value = provider.GetRequiredService<IOptions<HeadlessPrefixOptions>>().Value;

        // then
        value.Prefix.Should().Be(prefix);
    }

    [Fact]
    public void should_apply_the_prefix_once_to_every_named_instance_however_many_registrations_ask()
    {
        // given
        var services = _CreateServices(("Headless:Prefix", "wt1"));
        services.Configure<StubKeyOptions>(static x => x.KeyPrefix = "app:");
        services.Configure<StubKeyOptions>("orders", static x => x.KeyPrefix = "orders:");

        // when - a provider registration runs once per default and named instance
        services.ApplyHeadlessPrefix<StubKeyOptions>(static (x, prefix) => x.KeyPrefix = prefix + ":" + x.KeyPrefix);
        services.ApplyHeadlessPrefix<StubKeyOptions>(static (x, prefix) => x.KeyPrefix = prefix + ":" + x.KeyPrefix);
        services.ApplyHeadlessPrefix<StubKeyOptions>(static (x, prefix) => x.KeyPrefix = prefix + ":" + x.KeyPrefix);
        using var provider = services.BuildServiceProvider();

        // then
        var monitor = provider.GetRequiredService<IOptionsMonitor<StubKeyOptions>>();
        monitor.CurrentValue.KeyPrefix.Should().Be("wt1:app:");
        monitor.Get("orders").KeyPrefix.Should().Be("wt1:orders:");
    }

    [Fact]
    public void should_leave_names_unchanged_when_the_host_has_no_prefix()
    {
        // given
        var services = _CreateServices();
        services.Configure<StubKeyOptions>(static x => x.KeyPrefix = "app:");

        // when
        services.ApplyHeadlessPrefix<StubKeyOptions>(static (x, prefix) => x.KeyPrefix = prefix + ":" + x.KeyPrefix);
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IOptions<StubKeyOptions>>().Value.KeyPrefix.Should().Be("app:");
    }

    [Fact]
    public void should_join_a_name_to_the_prefix_only_when_one_is_set()
    {
        new HeadlessPrefixOptions { Prefix = "wt1" }
            .Apply("orders", ".")
            .Should()
            .Be("wt1.orders");
        new HeadlessPrefixOptions().Apply("orders", ".").Should().Be("orders");
    }

    [Fact]
    public async Task should_log_the_prefix_once_when_the_host_starts()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders().AddProvider(logs);
        builder.Services.ConfigureHeadlessPrefix("wt1");
        builder.Services.AddHeadlessPrefix();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        logs.Messages.Where(static x => x.StartsWith("Headless names in shared stores", StringComparison.Ordinal))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("'wt1'");
    }

    [Fact]
    public async Task should_log_nothing_about_the_prefix_when_none_is_set()
    {
        // given
        using var logs = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders().AddProvider(logs);
        builder.Services.AddHeadlessPrefix();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        logs.Messages.Should()
            .NotContain(static x => x.StartsWith("Headless names in shared stores", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_fail_the_host_start_on_an_invalid_prefix()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHeadlessPrefix("Not Valid");
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<OptionsValidationException>().WithMessage("*Headless:Prefix*");
    }

    private static ServiceCollection _CreateServices(params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
                .Build()
        );

        return services;
    }

    private sealed class StubKeyOptions
    {
        public string KeyPrefix { get; set; } = "";
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _messages = [];
        private readonly Lock _lock = new();

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_lock)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private void _Add(string message)
        {
            lock (_lock)
            {
                _messages.Add(message);
            }
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                owner._Add(formatter(state, exception));
            }
        }
    }
}
