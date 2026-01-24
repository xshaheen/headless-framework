// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Headless.Messaging.RedisStreams;

internal class RedisLogger(ILogger logger) : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override void WriteLine(string? value)
    {
#pragma warning disable CA2254 // Do not use string interpolation for logging message templates
        logger.LogInformation(value);
#pragma warning restore CA2254
    }
}
