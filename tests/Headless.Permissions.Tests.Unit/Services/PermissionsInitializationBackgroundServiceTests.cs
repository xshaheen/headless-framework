// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Permissions.Definitions;
using Headless.Permissions.Models;
using Headless.Permissions.Seeders;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Services;

public sealed class PermissionsInitializationBackgroundServiceTests : TestBase
{
    private readonly IServiceScopeFactory _serviceScopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _serviceScope = Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();
    private readonly IDynamicPermissionDefinitionStore _store = Substitute.For<IDynamicPermissionDefinitionStore>();
    private readonly FakeTimeProvider _timeProvider = new();

    public PermissionsInitializationBackgroundServiceTests()
    {
        _serviceScopeFactory.CreateScope().Returns(_serviceScope);
        _serviceScope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(IDynamicPermissionDefinitionStore)).Returns(_store);
    }

    #region Early Exit

    [Fact]
    public async Task should_not_start_when_both_options_disabled()
    {
        // given
        var options = new PermissionManagementOptions
        {
            SaveStaticPermissionsToDatabase = false,
            IsDynamicPermissionStoreEnabled = false,
        };

        var sut = _CreateSut(options);

        // when
        await sut.StartAsync(AbortToken);

        // Allow brief time since no background task should start
        await Task.Delay(50, AbortToken);

        // then - store should never be touched
        await _store.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
        await _store.DidNotReceive().GetGroupsAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Save Static Permissions

    [Fact]
    public async Task should_save_static_permissions_when_enabled()
    {
        // given
        var options = new PermissionManagementOptions
        {
            SaveStaticPermissionsToDatabase = true,
            IsDynamicPermissionStoreEnabled = false,
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

    #region Pre-cache Dynamic Permissions

    [Fact]
    public async Task should_pre_cache_dynamic_permissions_when_enabled()
    {
        // given
        var options = new PermissionManagementOptions
        {
            SaveStaticPermissionsToDatabase = false,
            IsDynamicPermissionStoreEnabled = true,
        };

        var preCacheCalled = new TaskCompletionSource();
        _store
            .GetGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                preCacheCalled.TrySetResult();
                return Task.FromResult<IReadOnlyList<PermissionGroupDefinition>>([]);
            });

        var sut = _CreateSut(options);

        // when
        await sut.StartAsync(AbortToken);
        await preCacheCalled.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then - GetGroupsAsync is used to pre-cache
        await _store.Received(1).GetGroupsAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Retry Behavior

    [Fact]
    public async Task should_retry_on_save_failure()
    {
        // given
        var options = new PermissionManagementOptions
        {
            SaveStaticPermissionsToDatabase = true,
            IsDynamicPermissionStoreEnabled = false,
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

    [Fact]
    public async Task should_use_10_retries_max()
    {
        // given
        var options = new PermissionManagementOptions
        {
            SaveStaticPermissionsToDatabase = true,
            IsDynamicPermissionStoreEnabled = false,
        };

        var callCount = 0;

        _store
            .SaveAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;

                throw new InvalidOperationException("Always fails");
            });

        using var sut = _CreateSut(options);

        // when
        await sut.StartAsync(CancellationToken.None);

        // Advance time significantly to allow all retries
        // Total delay for 10 retries: 2+4+8+16+32+64+128+256+512+1024 = 2046 seconds ~= 34 minutes
        for (var i = 0; i < 20; i++)
        {
            _timeProvider.Advance(TimeSpan.FromMinutes(5));
            await Task.Delay(50);
        }

        // then - should have exactly 11 attempts (1 initial + 10 retries)
        callCount.Should().Be(11);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task should_cancel_on_start_token_cancellation()
    {
        // given - test that cancellation via input token works
        var options = new PermissionManagementOptions
        {
            SaveStaticPermissionsToDatabase = true,
            IsDynamicPermissionStoreEnabled = false,
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
                    // Wait for cancellation using the provided CancellationToken
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

        // Cancel the token
        await cts.CancelAsync();

        // Wait for cancellation to be observed
        var cancelledTask = await Task.WhenAny(saveCancelled.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        // then - the save operation should have been cancelled
        cancelledTask.Should().Be(saveCancelled.Task, "cancellation token should cancel the background task");

        // Wait for task completion before dispose
        await Task.WhenAny(taskCompleted.Task, Task.Delay(TimeSpan.FromSeconds(1)));
    }

    #endregion

    #region Disposal

    [Fact]
    public async Task should_dispose_cancellation_token_source()
    {
        // given
        var options = new PermissionManagementOptions
        {
            SaveStaticPermissionsToDatabase = false,
            IsDynamicPermissionStoreEnabled = false,
        };

        var sut = _CreateSut(options);
        await sut.StartAsync(AbortToken);

        // when
        sut.Dispose();

        // then - should not throw on dispose (proper cleanup)
        // Calling dispose again should also not throw (idempotent)
        var action = () => sut.Dispose();
        action.Should().NotThrow();
    }

    #endregion

    #region Error Logging

    [Fact]
    public async Task should_call_get_groups_on_pre_cache_failure()
    {
        // given
        var options = new PermissionManagementOptions
        {
            SaveStaticPermissionsToDatabase = false,
            IsDynamicPermissionStoreEnabled = true,
        };

        var getGroupsCalled = new TaskCompletionSource();

        _store
            .GetGroupsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<PermissionGroupDefinition>>(_ =>
            {
                getGroupsCalled.TrySetResult();
                throw new InvalidOperationException("Pre-cache failed");
            });

        var sut = _CreateSut(options);

        // when
        await sut.StartAsync(CancellationToken.None);
        await getGroupsCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // then
        await _store.Received(1).GetGroupsAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helpers

    private PermissionsInitializationBackgroundService _CreateSut(PermissionManagementOptions options)
    {
        var optionsAccessor = Options.Create(options);
        var logger = LoggerFactory.CreateLogger<PermissionsInitializationBackgroundService>();

        return new PermissionsInitializationBackgroundService(
            _timeProvider,
            _serviceScopeFactory,
            optionsAccessor,
            logger
        );
    }

    #endregion
}
