// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace Framework.Testing.Retry;

// Take from: https://github.com/xunit/samples.xunit/tree/main/v3/RetryFactExample/Extensions

/// <summary>
/// Works just like [Fact] except that failures are retried (by default, 3 times).
/// </summary>
[XunitTestCaseDiscoverer(typeof(RetryFactDiscoverer))]
public sealed class RetryFactAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1
) : FactAttribute(sourceFilePath, sourceLineNumber)
{
    public int MaxRetries { get; set; } = 3;
}
