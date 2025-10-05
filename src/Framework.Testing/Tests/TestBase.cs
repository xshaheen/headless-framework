// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Bogus;
using Framework.Testing.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

[assembly: CaptureConsole]

namespace Framework.Testing.Tests;

// IAsyncLifetime -> to perform per-test initialization/cleanup work
// IClassFixture<TFixture> -> to perform per-class initialization/cleanup work
// ICollectionFixture & IAsyncLifetime -> to perform per-collection initialization/cleanup work
// AssemblyFixtureAttribute -> to perform some per-assembly initialization/cleanup work
// BeforeAfterTestAttribute -> to perform per-test initialization/cleanup work (with access to the test method info)
// ITestPipelineStartup & TestPipelineStartupAttribute -> to perform some global initialization/cleanup work

public abstract class TestBase : IAsyncLifetime
{
    private bool _disposed;

    protected ILoggerProvider LoggerProvider { get; private set; }

    protected ILoggerFactory LoggerFactory { get; private set; }

    protected ILogger Logger { get; }

    protected static CancellationToken AbortToken => TestContext.Current.CancellationToken;

    protected virtual Faker Faker { get; set; } = new();

    /// <summary>
    /// Attempt to cancel the currently executing test, if one is executing. This will
    /// signal the <see cref="AbortToken"/> for cancellation.
    /// </summary>
    protected void AbortCurrentTests()
    {
        TestContext.Current.CancelCurrentTest();
    }

    /// <summary>Initial, sync setup.</summary>
    protected TestBase()
    {
        var (loggerProvider, loggerFactory) = TestHelpers.CreateXUnitLoggerFactory(
            TestContext.Current.TestOutputHelper
        );
        LoggerFactory = loggerFactory;
        LoggerProvider = loggerProvider;
        Logger = LoggerFactory.CreateLogger(GetType());
    }

    /// <summary>Initial, async setup and called just after the constructor</summary>
    public virtual ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>Teardown and runs after each test</summary>
    public async ValueTask DisposeAsync()
    {
        // Do not change this code. Put cleanup code in 'DisposeAsyncCore()' method.
        await DisposeAsyncCore();
        // Suppress finalization to prevent the finalizer from running.
        GC.SuppressFinalize(this);
    }

    /// <summary>The core asynchronous cleanup logic.</summary>
    protected virtual ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        LoggerFactory?.Dispose();
        LoggerFactory = null!;
        LoggerProvider?.Dispose();
        LoggerProvider = null!;
        _disposed = true;

        return ValueTask.CompletedTask;
    }
}
