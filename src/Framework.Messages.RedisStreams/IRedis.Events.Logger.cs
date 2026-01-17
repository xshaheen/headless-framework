// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Framework.Messages.RedisStreams;

internal class RedisLogger(ILogger logger) : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override void WriteLine(string? value)
    {
        logger.LogInformation(value);
    }
}
