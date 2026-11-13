// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using tusdotnet.Interfaces;
using tusdotnet.Stores;

namespace Tests;

public sealed class SetupHeadlessTusTests : TestBase
{
    // Constructing the disk store touches no file: it only keeps the path.
    private static TusDiskStore _DiskStore()
    {
        return new TusDiskStore(Path.GetTempPath());
    }

    private static ServiceCollection _Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        return services;
    }

    private static TusExpiredUploadsCleanupService _CleanupService(IServiceProvider provider)
    {
        return provider.GetServices<IHostedService>().OfType<TusExpiredUploadsCleanupService>().Single();
    }

    [Fact]
    public void should_throw_and_register_nothing_when_no_store_is_selected()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHeadlessTus(_ => { });

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Headless.Tus requires exactly one store. Call one of `UseS3`, `UseAzure`, or `UseStore`.");
        services.Should().BeEmpty();
    }

    [Fact]
    public void should_throw_and_register_nothing_when_two_stores_are_selected()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () => services.AddHeadlessTus(tus => tus.UseStore(_DiskStore()).UseStore(_DiskStore()));

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Headless.Tus requires exactly one store. Multiple stores were configured; call only one of "
                    + "`UseS3`, `UseAzure`, or `UseStore`."
            );
        services.Should().BeEmpty();
    }

    [Fact]
    public void should_throw_and_keep_the_first_registration_when_called_twice()
    {
        // given
        var first = _DiskStore();
        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseStore(first));
        var registered = services.Count;

        // when
        var act = () => services.AddHeadlessTus(tus => tus.UseStore(_DiskStore()));

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "AddHeadlessTus was already called on this service collection. Select the store and configure "
                    + "locks and cleanup in a single AddHeadlessTus call."
            );
        services.Should().HaveCount(registered);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITusStore>().Should().BeSameAs(first);
    }

    [Fact]
    public void should_resolve_a_store_instance_by_its_type_and_as_the_host_store()
    {
        // given
        var store = _DiskStore();
        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseStore(store));
        using var provider = services.BuildServiceProvider();

        // when / then
        provider.GetRequiredService<TusDiskStore>().Should().BeSameAs(store);
        provider.GetRequiredService<ITusStore>().Should().BeSameAs(store);
    }

    [Fact]
    public void should_resolve_a_store_factory_once_by_its_type_and_as_the_host_store()
    {
        // given - the factory reads a service, and counts how often the container asks it for a store
        var created = 0;
        var services = _Services();
        services.AddSingleton(new StoreDirectory(Path.GetTempPath()));
        services.AddHeadlessTus(tus =>
            tus.UseStore(provider =>
            {
                created++;

                return new TusDiskStore(provider.GetRequiredService<StoreDirectory>().Path);
            })
        );
        using var provider = services.BuildServiceProvider();

        // when
        var store = provider.GetRequiredService<ITusStore>();

        // then
        store.Should().BeOfType<TusDiskStore>();
        provider.GetRequiredService<TusDiskStore>().Should().BeSameAs(store);
        created.Should().Be(1);
    }

    [Fact]
    public void should_resolve_a_store_that_is_only_known_by_the_store_interface()
    {
        // given - the variable's type is the interface, so the interface is the only registration
        ITusStore store = _DiskStore();
        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseStore(store));
        using var provider = services.BuildServiceProvider();

        // when / then
        provider.GetRequiredService<ITusStore>().Should().BeSameAs(store);
        provider.GetServices<ITusStore>().Should().ContainSingle();
    }

    [Fact]
    public void should_register_the_cleanup_service_with_its_defaults_when_cleanup_is_not_configured()
    {
        // given
        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseStore(_DiskStore()));
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<TusExpiredUploadsCleanupOptions>>().Value;

        // then
        _CleanupService(provider).Should().NotBeNull();
        options.Enabled.Should().BeTrue();
        options.Interval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void should_apply_the_cleanup_configuration()
    {
        // given
        var services = _Services();
        services.AddHeadlessTus(tus =>
            tus.UseStore(_DiskStore())
                .ConfigureExpiredUploadsCleanup(options => options.Interval = TimeSpan.FromSeconds(30))
        );
        using var provider = services.BuildServiceProvider();

        // when
        var options = provider.GetRequiredService<IOptions<TusExpiredUploadsCleanupOptions>>().Value;

        // then
        options.Interval.Should().Be(TimeSpan.FromSeconds(30));
        options.Enabled.Should().BeTrue();
    }

    [Fact]
    public void should_reject_a_non_positive_cleanup_interval()
    {
        // given
        var services = _Services();
        services.AddHeadlessTus(tus =>
            tus.UseStore(_DiskStore()).ConfigureExpiredUploadsCleanup(options => options.Interval = TimeSpan.Zero)
        );
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<TusExpiredUploadsCleanupOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public async Task should_run_a_cleanup_pass_against_the_selected_store_when_cleanup_is_not_configured()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var firstPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.RemoveExpiredFilesAsync(Arg.Any<CancellationToken>()).Returns(0).AndDoes(_ => firstPass.TrySetResult());
        var services = _Services();
        services.AddHeadlessTus(tus => tus.UseStore(store));
        await using var provider = services.BuildServiceProvider();
        var cleanup = _CleanupService(provider);

        // when
        await cleanup.StartAsync(AbortToken);
        await firstPass.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await cleanup.StopAsync(AbortToken);

        // then
        await store.Received(1).RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_run_no_cleanup_pass_when_cleanup_is_disabled()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var services = _Services();
        services.AddHeadlessTus(tus =>
            tus.UseStore(store).ConfigureExpiredUploadsCleanup(options => options.Enabled = false)
        );
        await using var provider = services.BuildServiceProvider();
        var cleanup = _CleanupService(provider);

        // when - a disabled service ends its loop at once instead of waiting for the next interval
        await cleanup.StartAsync(AbortToken);
        await cleanup.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        await store.DidNotReceive().RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void should_reject_null_arguments()
    {
        // given
        var services = new ServiceCollection();

        // when
        var noConfigure = () => services.AddHeadlessTus(null!);
        var noStore = () => services.AddHeadlessTus(tus => tus.UseStore((TusDiskStore)null!));
        var noFactory = () => services.AddHeadlessTus(tus => tus.UseStore((Func<IServiceProvider, TusDiskStore>)null!));
        var noStoreAction = () => services.AddHeadlessTus(tus => tus.RegisterStore<TusDiskStore>(null!));
        var noExtension = () => services.AddHeadlessTus(tus => tus.RegisterCrossCuttingExtension(null!));
        var noCleanup = () => services.AddHeadlessTus(tus => tus.ConfigureExpiredUploadsCleanup(null!));

        // then
        noConfigure.Should().Throw<ArgumentNullException>();
        noStore.Should().Throw<ArgumentNullException>();
        noFactory.Should().Throw<ArgumentNullException>();
        noStoreAction.Should().Throw<ArgumentNullException>();
        noExtension.Should().Throw<ArgumentNullException>();
        noCleanup.Should().Throw<ArgumentNullException>();
    }

    private sealed record StoreDirectory(string Path);
}
