// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Settings.Definitions;
using Headless.Settings.Models;
using Headless.Settings.Seeders;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Seeders;

public sealed class SettingsInitializationBackgroundServiceTests : TestBase
{
    private readonly IServiceScopeFactory _serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _serviceScope = Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly IDynamicSettingDefinitionStore _store = Substitute.For<IDynamicSettingDefinitionStore>();
    private readonly FakeTimeProvider _timeProvider = new();

    public SettingsInitializationBackgroundServiceTests()
    {
        _serviceScopeFactory.CreateScope().Returns(_serviceScope);
        _serviceScope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(IDynamicSettingDefinitionStore)).Returns(_store);
    }

    #region IInitializer Contract

    [Fact]
    public async Task should_report_not_initialized_before_start()
    {
        // given
        var sut = _CreateSut(
            new SettingManagementOptions { SaveStaticSettingsToDatabase = true, IsDynamicSettingStoreEnabled = false }
        );

        // then
        sut.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task should_report_initialized_when_options_disabled()
    {
        // given
        var sut = _CreateSut(
            new SettingManagementOptions { SaveStaticSettingsToDatabase = false, IsDynamicSettingStoreEnabled = false }
        );

        // when
        await sut.StartAsync(AbortToken);

        // then - skip path calls TrySetResult(), so IsInitialized == true
        sut.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task should_report_initialized_after_successful_completion()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = true,
            IsDynamicSettingStoreEnabled = false,
        };

        var saveDone = new TaskCompletionSource();
        _store
            .SaveAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveDone.TrySetResult();
                return Task.CompletedTask;
            });

        var sut = _CreateSut(options);
        await sut.StartAsync(AbortToken);
        await saveDone.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // Allow the TCS to be set
        await Task.Delay(50, AbortToken);

        // then
        sut.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task should_wait_for_initialization_async_completes_after_success()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = true,
            IsDynamicSettingStoreEnabled = false,
        };

        _store.SaveAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sut = _CreateSut(options);

        // when
        await sut.StartAsync(AbortToken);

        // then - should complete within timeout
        await sut.WaitForInitializationAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        sut.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public async Task should_propagate_fault_to_wait_for_initialization_async()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = false,
            IsDynamicSettingStoreEnabled = true,
        };

        var exception = new InvalidOperationException("Store exploded");

        _store
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SettingDefinition>>(_ => throw exception);

        var sut = _CreateSut(options);

        // when
        await sut.StartAsync(CancellationToken.None);

        // then - faulted TCS propagates the exception to all waiters
        var act = () => sut.WaitForInitializationAsync(AbortToken).WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Store exploded");

        sut.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task should_propagate_cancellation_to_wait_for_initialization_async_when_stopped()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = true,
            IsDynamicSettingStoreEnabled = false,
        };

        var saveStarted = new TaskCompletionSource();

        _store
            .SaveAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                saveStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, callInfo.Arg<CancellationToken>());
            });

        var sut = _CreateSut(options);
        await sut.StartAsync(AbortToken);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // when - stop cancels the internal CTS via StopAsync
        await sut.StopAsync(CancellationToken.None);

        // then - WaitForInitializationAsync should throw OCE, not hang
        var act = () => sut.WaitForInitializationAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Early Exit

    [Fact]
    public async Task should_not_start_when_both_options_disabled()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = false,
            IsDynamicSettingStoreEnabled = false,
        };

        var sut = _CreateSut(options);

        // when
        await sut.StartAsync(AbortToken);

        // Allow brief time since no background task should start
        await Task.Delay(50, AbortToken);

        // then - store should never be touched
        await _store.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
        await _store.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Save Static Settings

    [Fact]
    public async Task should_save_static_settings_when_enabled()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = true,
            IsDynamicSettingStoreEnabled = false,
        };

        var saveCalled = new TaskCompletionSource();
        _store
            .SaveAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                saveCalled.TrySetResult();
                return Task.CompletedTask;
            });

        var sut = _CreateSut(options);

        // when
        await sut.StartAsync(AbortToken);
        await saveCalled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        await _store.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Pre-cache Dynamic Settings

    [Fact]
    public async Task should_pre_cache_dynamic_settings_when_enabled()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = false,
            IsDynamicSettingStoreEnabled = true,
        };

        var preCacheCalled = new TaskCompletionSource();
        _store
            .GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                preCacheCalled.TrySetResult();
                return Task.FromResult<IReadOnlyList<SettingDefinition>>([]);
            });

        var sut = _CreateSut(options);

        // when
        await sut.StartAsync(AbortToken);
        await preCacheCalled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then - GetAllAsync is used to pre-cache
        await _store.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Retry Behavior

    [Fact]
    public async Task should_retry_on_save_failure()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = true,
            IsDynamicSettingStoreEnabled = false,
        };

        var callCount = 0;
        var completionSource = new TaskCompletionSource();

        _store
            .SaveAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;

                if (callCount < 3)
                {
                    throw new InvalidOperationException("Simulated failure");
                }

                completionSource.TrySetResult();

                return Task.CompletedTask;
            });

        using var sut = _CreateSut(options);

        // when
        await sut.StartAsync(AbortToken);

        // Advance time to allow retries to proceed (2s, 4s base delays with exponential backoff)
        for (var i = 0; i < 5 && !completionSource.Task.IsCompleted; i++)
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(10));
            await Task.Delay(50, AbortToken);
        }

        // then - should have retried until success (3 calls: 2 failures + 1 success)
        callCount.Should().BeGreaterThanOrEqualTo(3);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task should_cancel_on_start_token_cancellation()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = true,
            IsDynamicSettingStoreEnabled = false,
        };

        using var cts = new CancellationTokenSource();
        var saveStarted = new TaskCompletionSource();
        var saveCancelled = new TaskCompletionSource();
        var taskCompleted = new TaskCompletionSource();

        _store
            .SaveAsync(Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                saveStarted.TrySetResult();
                var ct = callInfo.Arg<CancellationToken>();

                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    saveCancelled.TrySetResult();
                    taskCompleted.TrySetResult();

                    throw;
                }
            });

        var sut = _CreateSut(options);

        // when - start with a cancellable token
        await sut.StartAsync(cts.Token);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        var cancelledTask = await Task.WhenAny(saveCancelled.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        // then
        cancelledTask.Should().Be(saveCancelled.Task, "cancellation token should cancel the background task");

        await Task.WhenAny(taskCompleted.Task, Task.Delay(TimeSpan.FromSeconds(1)));
    }

    #endregion

    #region Disposal

    [Fact]
    public async Task should_dispose_cancellation_token_source()
    {
        // given
        var options = new SettingManagementOptions
        {
            SaveStaticSettingsToDatabase = false,
            IsDynamicSettingStoreEnabled = false,
        };

        var sut = _CreateSut(options);
        await sut.StartAsync(AbortToken);

        // when
        sut.Dispose();

        // then - should not throw on dispose (proper cleanup)
        var action = () => sut.Dispose();
        action.Should().NotThrow();
    }

    #endregion

    #region Helpers

    private SettingsInitializationBackgroundService _CreateSut(SettingManagementOptions options)
    {
        var optionsAccessor = Options.Create(options);
        var logger = LoggerFactory.CreateLogger<SettingsInitializationBackgroundService>();

        return new SettingsInitializationBackgroundService(
            _timeProvider,
            _serviceScopeFactory,
            optionsAccessor,
            logger
        );
    }

    #endregion
}
