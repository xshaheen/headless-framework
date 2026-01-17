using Microsoft.Extensions.Logging;

namespace Tests.Helpers;

public class TestLoggingProvider(ITestOutputHelper outputHelper) : ILoggerProvider
{
    public void Dispose() { }

    public ILogger CreateLogger(string categoryName)
    {
        return new TestLogger(outputHelper, categoryName);
    }
}
