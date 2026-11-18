// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class LiveQueriesSetupTests : TestBase
{
    [Fact]
    public void should_register_the_watch_the_invalidator_and_the_dependency_declaration_as_singletons()
    {
        var services = new ServiceCollection();

        services.AddHeadlessLiveQueries(static _ => { });

        foreach (
            var contract in (Type[])
                [typeof(ILiveQueries), typeof(ILiveQueryInvalidator), typeof(ILiveQueryDependencies)]
        )
        {
            services
                .Should()
                .ContainSingle(d => d.ServiceType == contract)
                .Which.Lifetime.Should()
                .Be(ServiceLifetime.Singleton);
        }

        using var provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetRequiredService<ILiveQueries>().Should().NotBeNull();
        provider.GetRequiredService<ILiveQueryInvalidator>().Should().NotBeNull();
        provider.GetRequiredService<ILiveQueryDependencies>().Should().NotBeNull();
    }

    [Fact]
    public void should_start_no_background_work()
    {
        var services = new ServiceCollection();

        services.AddHeadlessLiveQueries(static _ => { });

        services.Should().NotContain(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void should_keep_one_registration_when_added_twice()
    {
        var services = new ServiceCollection();

        services.AddHeadlessLiveQueries(static setup => setup.ConfigureOptions(o => o.MaxTrackedKeys = 5));
        services.AddHeadlessLiveQueries(static _ => { });

        services.Should().ContainSingle(d => d.ServiceType == typeof(ILiveQueries));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<LiveQueriesOptions>>().Value.MaxTrackedKeys.Should().Be(5);
    }

    [Fact]
    public void should_reject_a_null_configure_delegate()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessLiveQueries(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_default_the_tracked_key_limit()
    {
        _Options(static _ => { }).Value.MaxTrackedKeys.Should().Be(100_000);
    }

    [Fact]
    public void should_apply_option_configurations_in_call_order()
    {
        var options = _Options(static setup =>
            setup.ConfigureOptions(o => o.MaxTrackedKeys = 10).ConfigureOptions(o => o.MaxTrackedKeys *= 3)
        );

        options.Value.MaxTrackedKeys.Should().Be(30);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void should_reject_a_tracked_key_limit_below_one(int limit)
    {
        var options = _Options(setup => setup.ConfigureOptions(o => o.MaxTrackedKeys = limit));

        var act = () => options.Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxTrackedKeys*");
    }

    [Fact]
    public void should_ignore_a_declared_dependency_outside_a_live_query()
    {
        var services = new ServiceCollection();
        services.AddHeadlessLiveQueries(static _ => { });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ILiveQueryDependencies>().DependsOn("a");

        act.Should().NotThrow();
    }

    [Fact]
    public void should_reject_an_empty_dependency_key()
    {
        var services = new ServiceCollection();
        services.AddHeadlessLiveQueries(static _ => { });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ILiveQueryDependencies>().DependsOn("a", "");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_reject_an_empty_invalidated_key()
    {
        var services = new ServiceCollection();
        services.AddHeadlessLiveQueries(static _ => { });
        await using var provider = services.BuildServiceProvider();

        var act = async () =>
            await provider.GetRequiredService<ILiveQueryInvalidator>().InvalidateAsync(["a", ""], AbortToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task should_not_announce_when_the_token_is_already_cancelled()
    {
        var services = new ServiceCollection();
        services.AddHeadlessLiveQueries(static _ => { });
        await using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = async () =>
            await provider.GetRequiredService<ILiveQueryInvalidator>().InvalidateAsync(["a"], cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IOptions<LiveQueriesOptions> _Options(Action<HeadlessLiveQueriesSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddHeadlessLiveQueries(configure);

        return services.BuildServiceProvider().GetRequiredService<IOptions<LiveQueriesOptions>>();
    }
}
