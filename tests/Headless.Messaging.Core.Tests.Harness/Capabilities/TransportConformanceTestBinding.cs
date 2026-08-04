// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Tests.Capabilities;

/// <summary>Strongly typed link from one manifest cell to an executable provider test.</summary>
[PublicAPI]
public sealed record TransportConformanceTestBinding(
    TransportConformanceScenario Scenario,
    Type TestClass,
    string TestMethod
);

/// <summary>Validates that every supported manifest cell is backed by a discoverable test.</summary>
[PublicAPI]
public static class TransportConformanceTestBindings
{
    public static async Task ExecuteSupportedScenariosAsync(
        TransportConformanceProfile profile,
        IEnumerable<TransportConformanceTestBinding> bindings,
        Func<Type, object> createTestClass
    )
    {
        var materialized = bindings.ToList();
        var errors = GetValidationErrors(profile, materialized);
        if (errors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        foreach (var binding in materialized.DistinctBy(binding => (binding.TestClass, binding.TestMethod)))
        {
            var method = _GetTestMethod(binding)!;
            var instance = method.IsStatic ? null : createTestClass(binding.TestClass);
            var lifetime = instance as IAsyncLifetime;

            try
            {
                if (lifetime is not null)
                {
                    await lifetime.InitializeAsync().ConfigureAwait(false);
                }

                var result = method.Invoke(instance, null);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                }
                else if (result is ValueTask valueTask)
                {
                    await valueTask.ConfigureAwait(false);
                }
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
            finally
            {
                if (lifetime is not null)
                {
                    await lifetime.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    public static IReadOnlyList<string> GetValidationErrors(
        TransportConformanceProfile profile,
        IEnumerable<TransportConformanceTestBinding> bindings
    )
    {
        var errors = new List<string>();
        var bindingsByScenario = bindings.ToLookup(binding => binding.Scenario);

        foreach (var (scenario, support) in profile.Scenarios)
        {
            if (support.Status == ConformanceStatus.Supported && !bindingsByScenario.Contains(scenario))
            {
                errors.Add($"{profile.Provider}: Supported {scenario} has no executable test binding.");
            }
        }

        foreach (var binding in bindingsByScenario.SelectMany(group => group))
        {
            _ValidateBinding(profile, binding, errors);
        }

        return errors;
    }

    private static void _ValidateBinding(
        TransportConformanceProfile profile,
        TransportConformanceTestBinding binding,
        ICollection<string> errors
    )
    {
        if (
            !profile.Scenarios.TryGetValue(binding.Scenario, out var support)
            || support.Status != ConformanceStatus.Supported
        )
        {
            errors.Add(
                $"{profile.Provider}: {binding.Scenario} binds executable evidence but is not declared Supported."
            );
            return;
        }

        var method = _GetTestMethod(binding);
        if (method is null)
        {
            errors.Add(
                $"{profile.Provider}: {binding.Scenario} evidence method "
                    + $"{binding.TestClass.FullName}.{binding.TestMethod} does not exist."
            );
            return;
        }

        var testAttribute = method
            .GetCustomAttributesData()
            .SingleOrDefault(attribute => attribute.AttributeType.Name is "FactAttribute" or "TheoryAttribute");
        if (testAttribute is null)
        {
            errors.Add(
                $"{profile.Provider}: {binding.Scenario} evidence method "
                    + $"{binding.TestClass.FullName}.{binding.TestMethod} is not an xUnit test."
            );
            return;
        }

        if (method.GetParameters().Length != 0)
        {
            errors.Add(
                $"{profile.Provider}: {binding.Scenario} evidence method "
                    + $"{binding.TestClass.FullName}.{binding.TestMethod} requires arguments and cannot be invoked."
            );
        }

        var skipReason =
            testAttribute
                .NamedArguments.SingleOrDefault(argument =>
                    string.Equals(argument.MemberName, "Skip", StringComparison.Ordinal)
                )
                .TypedValue.Value as string;
        if (!string.IsNullOrWhiteSpace(skipReason))
        {
            errors.Add(
                $"{profile.Provider}: {binding.Scenario} evidence method "
                    + $"{binding.TestClass.FullName}.{binding.TestMethod} is unconditionally skipped."
            );
        }

        var isExplicit =
            testAttribute
                .NamedArguments.SingleOrDefault(argument =>
                    string.Equals(argument.MemberName, "Explicit", StringComparison.Ordinal)
                )
                .TypedValue.Value as bool?;
        if (isExplicit == true)
        {
            errors.Add(
                $"{profile.Provider}: {binding.Scenario} evidence method "
                    + $"{binding.TestClass.FullName}.{binding.TestMethod} is explicit and will not run in CI."
            );
        }
    }

    private static MethodInfo? _GetTestMethod(TransportConformanceTestBinding binding)
    {
        return binding.TestClass.GetMethod(
            binding.TestMethod,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
        );
    }
}
