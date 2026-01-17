using Microsoft.Extensions.Logging;

namespace Tests.Helpers;

public static class TestLoggingExtensions
{
    public static void AddTestLogging(this ILoggingBuilder builder, ITestOutputHelper outputHelper)
    {
        builder.AddProvider(new TestLoggingProvider(outputHelper));
    }
}
