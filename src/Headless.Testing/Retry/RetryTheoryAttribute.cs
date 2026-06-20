// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using Xunit;
using Xunit.v3;

namespace Headless.Testing.Retry;

/// <summary>
/// Works just like <c>[Theory]</c> except that failures are retried — each data row runs up to
/// <see cref="MaxRetries"/> total attempts (default 3) before its last failure is reported.
/// </summary>
[PublicAPI]
[XunitTestCaseDiscoverer(typeof(RetryTheoryDiscoverer))]
public sealed class RetryTheoryAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1
) : TheoryAttribute(sourceFilePath, sourceLineNumber)
{
    public int MaxRetries { get; set; } = 3;
}
