// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit.Internal;
using Xunit.Sdk;
using Xunit.v3;

namespace Framework.Testing.Retry;

public sealed class RetryFactDiscoverer : IXunitTestCaseDiscoverer
{
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
            details.SkipExceptions,
            details.SkipReason,
            details.SkipType,
            details.SkipUnless,
            details.SkipWhen,
            testMethod.Traits.ToReadWrite(StringComparer.OrdinalIgnoreCase),
            timeout: details.Timeout
        );
#pragma warning restore CA2000

        return new([testCase]);
    }
}
