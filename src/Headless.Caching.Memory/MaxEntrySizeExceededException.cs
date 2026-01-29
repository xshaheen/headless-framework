// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Caching;

/// <summary>Exception thrown when a cache entry exceeds the configured maximum entry size.</summary>
[PublicAPI]
public sealed class MaxEntrySizeExceededException : Exception
{
    public MaxEntrySizeExceededException()
    {
    }

    public MaxEntrySizeExceededException(string message) : base(message)
    {
    }

    public MaxEntrySizeExceededException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
