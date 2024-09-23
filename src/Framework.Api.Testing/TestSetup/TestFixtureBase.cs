using Framework.Kernel.BuildingBlocks.Abstractions;
using MartinCostello.Logging.XUnit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Serilog;
using WireMock.Server;
using WireMock.Settings;
using Xunit;
using Xunit.Abstractions;

namespace Framework.Api.Testing.TestSetup;

public class TestFixtureBase<TEntryPoint> : IAsyncLifetime, IDisposable, ITestOutputHelperAccessor
    where TEntryPoint : class
{
    private bool _disposed;
    private ITestOutputHelper? _outputHelper;

    public TestAppContext<TEntryPoint> App { get; }

    public WireMockServer WireMockServer { get; }

    public ITestOutputHelper? OutputHelper
    {
        get => _outputHelper;
        set
        {
            if (_outputHelper is null && value is not null)
            {
                Log.Logger = new LoggerConfiguration()
                    .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
                    .WriteTo.TestOutput(value, formatProvider: CultureInfo.InvariantCulture)
                    .CreateLogger();
            }

            _outputHelper = value;
        }
    }

    /// <summary>This runs before all the tests run</summary>
    public TestFixtureBase()
    {
        App = new TestAppContext<TEntryPoint>(
            configureServices: (_, services) =>
            {
                services.Replace(_ =>
                {
                    var clock = Substitute.For<IClock>();

                    clock.UtcNow.Returns(DateTimeOffset.UtcNow);
                    clock.Ticks.Returns(Environment.TickCount64);

                    return clock;
                });
            }
        );

        WireMockServer = WireMockServer.Start(new WireMockServerSettings { StartAdminInterface = true });
    }

    /// <summary>This runs before all the test run and Called just after the constructor</summary>
    public Task InitializeAsync()
    {
        return App.ResetAsync();
    }

    /// <summary>This runs after all the test run and Called before Dispose()</summary>
    public Task DisposeAsync()
    {
        WireMockServer.Stop();

        return Task.CompletedTask;
    }

    /// <summary>This runs after all the tests run</summary>
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
            App.Dispose();
        }

        _disposed = true;
    }
}
