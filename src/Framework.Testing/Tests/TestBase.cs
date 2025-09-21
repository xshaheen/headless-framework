// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Bogus;
using Framework.Testing.Helpers;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Framework.Testing.Tests;

public abstract class TestBase : IDisposable
{
    private bool _disposed;

    protected ILoggerProvider LoggerProvider { get; }

    protected ILoggerFactory LoggerFactory { get; }

    protected ILogger Logger { get; }

    protected virtual Faker Faker { get; set; } = new();

    protected TestBase(ITestOutputHelper output)
    {
        var (loggerProvider, loggerFactory) = TestHelpers.CreateXUnitLoggerFactory(output);

        LoggerFactory = loggerFactory;
        LoggerProvider = loggerProvider;
        Logger = LoggerFactory.CreateLogger(GetType());
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            LoggerFactory?.Dispose();
            LoggerProvider?.Dispose();
        }

        _disposed = true;
    }
}
