// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Order;

[PublicAPI]
public sealed class AlphaTestsOrderer : ITestCaseOrderer
{
    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        return testCases.OrderBy(tc => tc.TestMethod?.MethodName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
