// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Bogus;
using Headless.Testing.Helpers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Headless.Testing.Tests;

// IAsyncLifetime -> to perform per-test initialization/cleanup work
// IClassFixture<TFixture> -> to perform per-class initialization/cleanup work
// ICollectionFixture & IAsyncLifetime -> to perform per-collection initialization/cleanup work
// AssemblyFixtureAttribute -> to perform some per-assembly initialization/cleanup work
// BeforeAfterTestAttribute -> to perform per-test initialization/cleanup work (with access to the test method info)
// ITestPipelineStartup & TestPipelineStartupAttribute -> to perform some global initialization/cleanup work

/// <summary>
/// Base class for xUnit v3 tests. Wires an xUnit-backed <see cref="ILoggerFactory"/>,
/// a <see cref="Bogus.Faker"/> instance, and a <see cref="CancellationToken"/> that xUnit
/// cancels when the test times out or the run is aborted.
/// </summary>
/// <remarks>
/// Lifetime follows xUnit v3's <see cref="IAsyncLifetime"/> contract:
/// constructor runs synchronously, <see cref="InitializeAsync"/> runs just after (override for
/// async setup), and <see cref="DisposeAsync"/> runs after the test body.
/// Override <see cref="DisposeAsyncCore"/> — not <see cref="DisposeAsync"/> — for teardown work.
/// </remarks>
[PublicAPI]
public abstract class TestBase : IAsyncLifetime
{
    private bool _disposed;

    /// <summary>The xUnit log provider wired to the current test's output.</summary>
    protected ILoggerProvider LoggerProvider { get; private set; }

    /// <summary>Logger factory backed by <see cref="LoggerProvider"/>.</summary>
    protected ILoggerFactory LoggerFactory { get; private set; }

    /// <summary>Logger scoped to the concrete test class.</summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// xUnit cancellation token for the running test. Pass this to every async call so
    /// the test respects framework-level timeouts and abort signals.
    /// </summary>
    protected static CancellationToken AbortToken => TestContext.Current.CancellationToken;

    /// <summary>Bogus data faker for generating random test data.</summary>
    protected virtual Faker Faker { get; set; } = new();

    /// <summary>
    /// Requests cancellation of the currently running test by signalling <see cref="AbortToken"/>.
    /// </summary>
    protected static void AbortCurrentTests()
    {
        TestContext.Current.CancelCurrentTest();
    }

    /// <summary>
    /// Initializes the logger factory and per-class logger. Called by xUnit before
    /// <see cref="InitializeAsync"/>.
    /// </summary>
    protected TestBase()
    {
        var (loggerProvider, loggerFactory) = TestHelpers.CreateXUnitLoggerFactory(
            TestContext.Current.TestOutputHelper
        );
        LoggerFactory = loggerFactory;
        LoggerProvider = loggerProvider;
        Logger = LoggerFactory.CreateLogger(GetType());
    }

    /// <summary>
    /// Async setup called by xUnit immediately after the constructor. Override to perform
    /// async initialization work before the test body executes.
    /// </summary>
    public virtual ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Disposes the test instance. Delegates to <see cref="DisposeAsyncCore"/> — put cleanup
    /// logic there, not here.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Do not change this code. Put cleanup code in 'DisposeAsyncCore()' method.
        await DisposeAsyncCore();
        // Suppress finalization to prevent the finalizer from running.
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Core async teardown called by <see cref="DisposeAsync"/>. Override in derived classes
    /// to release test resources. Always call <c>await base.DisposeAsyncCore()</c> at the end
    /// so the base class disposes the logger factory. Idempotent — safe to call more than once.
    /// </summary>
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
