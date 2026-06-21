// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace Headless.Testing.Retry;

// Adapted from the xUnit v3 sample: https://github.com/xunit/samples.xunit/tree/main/v3/RetryFactExample/Extensions

/// <summary>
/// Works just like <c>[Fact]</c> except that failures are retried — the test runs up to
/// <see cref="MaxRetries"/> total attempts (default 3) before its last failure is reported.
/// </summary>
[PublicAPI]
[XunitTestCaseDiscoverer(typeof(RetryFactDiscoverer))]
public sealed class RetryFactAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1
) : FactAttribute(sourceFilePath, sourceLineNumber)
{
    /// <summary>
    /// Maximum number of total execution attempts. Must be at least 1; values less than 1
    /// are treated as 3 by the runner. Defaults to 3.
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}
