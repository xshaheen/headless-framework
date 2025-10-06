// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit.Sdk;
using Xunit.v3;

namespace Framework.Testing.Order;

public sealed class AlfaTestsOrderer : ITestCaseOrderer
{
    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        return testCases.OrderBy(tc => tc.TestMethod?.MethodName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
