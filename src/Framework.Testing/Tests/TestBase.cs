// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Meziantou.Extensions.Logging.Xunit;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Framework.Testing.Tests;

public abstract class TestBase : IDisposable
{
    private bool _disposed;

    protected ILoggerFactory LoggerFactory { get; }

    protected ILoggerProvider LoggerProvider { get; }

    protected ILogger Logger { get; }

    protected TestBase(ITestOutputHelper output)
    {
        LoggerFactory = new LoggerFactory();

        LoggerProvider = new XUnitLoggerProvider(
            output,
            new XUnitLoggerOptions
            {
                IncludeLogLevel = true,
                IncludeScopes = true,
                IncludeCategory = true,
            }
        );

        LoggerFactory.AddProvider(LoggerProvider);

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
            LoggerFactory.Dispose();
            LoggerProvider.Dispose();
        }

        _disposed = true;
    }
}
