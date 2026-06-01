// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;
using Headless.Testing.Tests;

namespace Tests.Initialization;

public sealed class HostedInitializerTests : TestBase
{
    [Fact]
    public async Task should_skip_initialize_when_run_on_startup_is_false_but_still_complete()
    {
        // given
        var initializer = new TrackingInitializer(runOnStartup: false);

        // when
        await initializer.StartingAsync(AbortToken);

        // then
        initializer.InitializeCalled.Should().BeFalse();
        initializer.IsInitialized.Should().BeTrue();
        // WaitForInitializationAsync must complete (not hang) even though InitializeAsync was skipped.
        await initializer.WaitForInitializationAsync(AbortToken);
    }

    [Fact]
    public async Task should_run_initialize_when_run_on_startup_is_true_by_default()
    {
        // given
        var initializer = new TrackingInitializer(runOnStartup: true);

        // when
        await initializer.StartingAsync(AbortToken);

        // then
        initializer.InitializeCalled.Should().BeTrue();
        initializer.IsInitialized.Should().BeTrue();
        await initializer.WaitForInitializationAsync(AbortToken);
    }

    private sealed class TrackingInitializer(bool runOnStartup) : HostedInitializer
    {
        public bool InitializeCalled { get; private set; }

        protected override bool RunOnStartup => runOnStartup;

        public override Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCalled = true;

            return Task.CompletedTask;
        }
    }
}
