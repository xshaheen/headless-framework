using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Tests.Storage;

public static class SharedConnection
{
    private const string _ConnectionString = "127.0.0.1:7006,allowAdmin=true";
    private static ConnectionMultiplexer? _muxer;

    public static ConnectionMultiplexer GetMuxer(ILoggerFactory loggerFactory)
    {
        return _muxer ??= ConnectionMultiplexer.Connect(_ConnectionString, o => o.LoggerFactory = loggerFactory);
    }
}
