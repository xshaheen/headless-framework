// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Tests.Capabilities;

/// <summary>
/// Decides from metadata alone whether a method runs as a discovered xUnit test, so evidence bindings cannot point at a
/// method that compiles but never executes.
/// </summary>
internal static class TestMethodExecutability
{
    /// <summary>Returns whether <paramref name="method"/> itself carries a Fact or Theory attribute.</summary>
    public static bool IsTest(MethodInfo method) =>
        method
            .GetCustomAttributesData()
            .Any(attribute => _IsOrDerivesFrom(attribute.AttributeType, "FactAttribute", "TheoryAttribute"));

    /// <summary>Returns why <paramref name="method"/> would not run as a test; empty when it would.</summary>
    /// <param name="method">The method as reached through the test class that runs it.</param>
    /// <param name="allowTheoryData">
    /// <see langword="true"/> to accept a theory whose parameters come from data attributes; <see langword="false"/> when
    /// the caller invokes the method itself and so needs it parameterless.
    /// </param>
    public static IReadOnlyList<string> GetProblems(MethodInfo method, bool allowTheoryData)
    {
        var problems = new List<string>();
        var attributes = method.GetCustomAttributesData();
        var testAttribute = attributes.FirstOrDefault(attribute =>
            _IsOrDerivesFrom(attribute.AttributeType, "FactAttribute", "TheoryAttribute")
        );

        if (testAttribute is null)
        {
            problems.Add("is not an xUnit test");
            return problems;
        }

        if (!method.IsPublic)
        {
            problems.Add("is not public, so xUnit does not discover it");
        }

        if (method.GetParameters().Length != 0)
        {
            var hasTheoryData =
                _IsOrDerivesFrom(testAttribute.AttributeType, "TheoryAttribute")
                && attributes.Any(attribute => _IsOrDerivesFrom(attribute.AttributeType, "DataAttribute"));

            if (!allowTheoryData || !hasTheoryData)
            {
                problems.Add("requires arguments and cannot be invoked");
            }
        }

        var skipReason =
            testAttribute
                .NamedArguments.SingleOrDefault(argument =>
                    string.Equals(argument.MemberName, "Skip", StringComparison.Ordinal)
                )
                .TypedValue.Value as string;
        if (!string.IsNullOrWhiteSpace(skipReason))
        {
            problems.Add("is unconditionally skipped");
        }

        var isExplicit =
            testAttribute
                .NamedArguments.SingleOrDefault(argument =>
                    string.Equals(argument.MemberName, "Explicit", StringComparison.Ordinal)
                )
                .TypedValue.Value as bool?;
        if (isExplicit == true)
        {
            problems.Add("is explicit and will not run in CI");
        }

        return problems;
    }

    // Matched by name rather than type so a test can stand in a local attribute for xUnit's and keep its fixture
    // methods out of discovery; walking the base chain also accepts attributes derived from Fact or Theory.
    private static bool _IsOrDerivesFrom(Type? type, params string[] names)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (names.Contains(type.Name, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
