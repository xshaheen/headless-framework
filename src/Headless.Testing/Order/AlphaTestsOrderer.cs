// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Xunit.Sdk;
using Xunit.v3;

namespace Headless.Testing.Order;

/// <summary>
/// xUnit v3 <see cref="ITestCaseOrderer"/> that runs tests in case-insensitive alphabetical
/// order by method name. Apply with <c>[TestCaseOrderer(typeof(AlphaTestsOrderer))]</c> on a
/// test class to make test execution order deterministic and predictable.
/// </summary>
[PublicAPI]
public sealed class AlphaTestsOrderer : ITestCaseOrderer
{
    /// <summary>
    /// Orders <paramref name="testCases"/> alphabetically by method name using
    /// case-insensitive ordinal comparison.
    /// </summary>
    /// <param name="testCases">The test cases to order.</param>
    /// <returns>A new collection sorted by method name ascending.</returns>
    public IReadOnlyCollection<TTestCase> OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
        where TTestCase : ITestCase
    {
        return testCases.OrderBy(tc => tc.TestMethod?.MethodName, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
