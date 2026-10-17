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
        var materialized = bindings.ToList();
        var bindingsByScenario = materialized.ToLookup(binding => binding.Scenario);

        // Every provider's evidence test passes its bindings through here, so this is the one place that sees which
        // test backs which cell; the scheduled run exports them to tell simulator drift from a provider failure.
        ConformanceEvidenceExport.RecordBindings(profile, materialized);

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

        // Bound methods are invoked directly by ExecuteSupportedScenariosAsync, so a theory's data cannot be supplied.
        foreach (var problem in TestMethodExecutability.GetProblems(method, allowTheoryData: false))
        {
            errors.Add(
                $"{profile.Provider}: {binding.Scenario} evidence method "
                    + $"{binding.TestClass.FullName}.{binding.TestMethod} {problem}."
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
