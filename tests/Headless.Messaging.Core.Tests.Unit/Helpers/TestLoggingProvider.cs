using Microsoft.Extensions.Logging;

namespace Tests.Helpers;

public sealed class TestLoggingProvider(ITestOutputHelper outputHelper) : ILoggerProvider
{
    public void Dispose() { }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestLogger(outputHelper, categoryName);
    }
}
