// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace Headless.Testing.Retry;

/// <summary>
/// Works just like [Theory] except that failures are retried (by default, 3 times).
/// </summary>
[XunitTestCaseDiscoverer(typeof(RetryTheoryDiscoverer))]
public sealed class RetryTheoryAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1
) : TheoryAttribute(sourceFilePath, sourceLineNumber)
{
    public int MaxRetries { get; set; } = 3;
}
