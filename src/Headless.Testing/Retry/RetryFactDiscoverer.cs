// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Xunit.Internal;
using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Retry;

/// <summary>
/// xUnit v3 test-case discoverer for <see cref="RetryFactAttribute"/>. Produces a single
/// <see cref="RetryTestCase"/> per decorated method.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RetryFactDiscoverer : IXunitTestCaseDiscoverer
{
    /// <summary>
    /// Creates a <see cref="RetryTestCase"/> for the decorated fact method.
    /// </summary>
    /// <param name="discoveryOptions">Framework discovery options.</param>
    /// <param name="testMethod">The test method being discovered.</param>
    /// <param name="factAttribute">The <see cref="RetryFactAttribute"/> instance.</param>
    /// <returns>A single-element collection containing the retry test case.</returns>
    public ValueTask<IReadOnlyCollection<IXunitTestCase>> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute
    )
    {
        var maxRetries = (factAttribute as RetryFactAttribute)?.MaxRetries ?? 3;
        var details = TestIntrospectionHelper.GetTestCaseDetails(discoveryOptions, testMethod, factAttribute);

#pragma warning disable CA2000 // testCase is returned to caller, who will dispose it
        var testCase = new RetryTestCase(
            maxRetries,
            details.ResolvedTestMethod,
            details.TestCaseDisplayName,
            details.UniqueID,
            details.Explicit,
            skipExceptions: details.SkipExceptions,
            skipReason: details.SkipReason,
            skipType: details.SkipType,
            skipUnless: details.SkipUnless,
            skipWhen: details.SkipWhen,
            traits: testMethod.Traits.ToReadWrite(StringComparer.OrdinalIgnoreCase),
            sourceFilePath: details.SourceFilePath,
            sourceLineNumber: details.SourceLineNumber,
            timeout: details.Timeout
        );
#pragma warning restore CA2000

        return new([testCase]);
    }
}
