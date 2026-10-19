// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.RegularExpressions;
using Tests.Capabilities;

namespace Tests.Guarantees;

/// <summary>Where a bound test runs, which decides whether it can prove a real-system behavior.</summary>
[PublicAPI]
public enum GuaranteeEvidenceTier
{
    /// <summary>A shared harness contract suite, run by every provider leaf that derives it.</summary>
    SharedSuite,

    /// <summary>An in-process fast-tier test (simulator or fakes) that cannot show real broker or database behavior.</summary>
    Simulator,
}

/// <summary>One <see cref="GuaranteeAttribute"/> found on a test method.</summary>
/// <param name="GuaranteeId">The ID the attribute names.</param>
/// <param name="TestClass">The class the method runs on: the declaring suite, or the provider leaf that inherits it.</param>
/// <param name="Method">The method as resolved on <paramref name="TestClass"/>.</param>
/// <param name="Tier">Where the test runs.</param>
[PublicAPI]
public sealed record GuaranteeBinding(
    string GuaranteeId,
    Type TestClass,
    MethodInfo Method,
    GuaranteeEvidenceTier Tier
);

/// <summary>Finds guarantee bindings and validates them against the catalogue.</summary>
/// <remarks>
/// One assembly cannot see the provider leaves, so the check is split. The catalogue test sees the harness suites and
/// the fast tier and proves each guarantee is bound; each provider's evidence test checks its own leaves and proves
/// every guarantee it inherits actually runs there.
/// </remarks>
[PublicAPI]
public static partial class GuaranteeBindings
{
    private const BindingFlags _AllMethods =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Returns the bindings declared on methods of <paramref name="types"/>.</summary>
    public static IReadOnlyList<GuaranteeBinding> DiscoverDeclared(IEnumerable<Type> types, GuaranteeEvidenceTier tier)
    {
        return types
            .SelectMany(type =>
                type.GetMethods(_AllMethods | BindingFlags.DeclaredOnly).Select(method => (type, method))
            )
            .SelectMany(pair =>
                pair.method.GetCustomAttributes<GuaranteeAttribute>(inherit: false)
                    .Select(attribute => new GuaranteeBinding(attribute.Id, pair.type, pair.method, tier))
            )
            .ToList();
    }

    /// <summary>Returns the bindings declared in <paramref name="assembly"/>.</summary>
    public static IReadOnlyList<GuaranteeBinding> DiscoverDeclared(Assembly assembly, GuaranteeEvidenceTier tier) =>
        DiscoverDeclared(assembly.GetTypes(), tier);

    /// <summary>
    /// Returns every guarantee each concrete class in <paramref name="types"/> runs or should run, including those it
    /// inherits from a harness suite, resolved to the method that executes on that class.
    /// </summary>
    public static IReadOnlyList<GuaranteeBinding> DiscoverLeaves(IEnumerable<Type> types, GuaranteeEvidenceTier tier)
    {
        var bindings = new List<GuaranteeBinding>();

        foreach (
            var leaf in types.Where(type =>
                type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
            )
        )
        {
            foreach (var method in leaf.GetMethods(_AllMethods))
            {
                // inherit: true walks the override chain, so an attribute on a harness base method reaches the leaf
                // override that xUnit actually runs.
                foreach (
                    var attribute in Attribute.GetCustomAttributes(method, typeof(GuaranteeAttribute), inherit: true)
                )
                {
                    bindings.Add(new GuaranteeBinding(((GuaranteeAttribute)attribute).Id, leaf, method, tier));
                }
            }
        }

        return bindings;
    }

    /// <summary>
    /// Validates the catalogue against the bindings the catalogue test can see and against the transport manifest.
    /// </summary>
    public static IReadOnlyList<string> GetCatalogueErrors(
        IReadOnlyCollection<MessagingGuarantee> catalogue,
        IEnumerable<GuaranteeBinding> bindings,
        IReadOnlyDictionary<string, TransportConformanceProfile> manifest
    )
    {
        var errors = new List<string>();
        var materialized = bindings.ToList();
        var bindingsById = materialized.ToLookup(binding => binding.GuaranteeId, StringComparer.Ordinal);

        foreach (
            var duplicate in catalogue
                .GroupBy(guarantee => guarantee.Id, StringComparer.Ordinal)
                .Where(g => g.Skip(1).Any())
        )
        {
            errors.Add($"{duplicate.Key} is catalogued {duplicate.Count()} times; IDs must be unique.");
        }

        foreach (var guarantee in catalogue)
        {
            _ValidateEntry(guarantee, bindingsById[guarantee.Id].ToList(), manifest, errors);
        }

        foreach (var binding in materialized)
        {
            _ValidateDeclaredBinding(catalogue, binding, errors);
        }

        return errors;
    }

    /// <summary>Validates the committed catalogue against the given bindings and the committed transport manifest.</summary>
    public static IReadOnlyList<string> GetCatalogueErrors(IEnumerable<GuaranteeBinding> bindings) =>
        GetCatalogueErrors(MessagingGuarantees.All.ToList(), bindings, TransportConformanceManifest.Providers);

    /// <summary>
    /// Validates that every guarantee a leaf class carries names a catalogued ID and runs as an xUnit test on that leaf.
    /// </summary>
    public static IReadOnlyList<string> GetLeafErrors(
        IReadOnlyCollection<MessagingGuarantee> catalogue,
        IEnumerable<GuaranteeBinding> leafBindings
    )
    {
        var errors = new List<string>();

        foreach (var binding in leafBindings)
        {
            var target = $"{binding.TestClass.FullName}.{binding.Method.Name}";

            if (MessagingGuarantees.Find(catalogue, binding.GuaranteeId) is null)
            {
                errors.Add($"{target} carries [Guarantee(\"{binding.GuaranteeId}\")], which is not in the catalogue.");
            }

            foreach (var problem in TestMethodExecutability.GetProblems(binding.Method, allowTheoryData: true))
            {
                errors.Add(
                    $"{target} carries {binding.GuaranteeId} but {problem} on this leaf; "
                        + "put [Fact] on the harness method or override it with [Fact]."
                );
            }
        }

        return errors;
    }

    /// <summary>Validates every leaf class in <paramref name="assembly"/> against the committed catalogue.</summary>
    public static IReadOnlyList<string> GetLeafErrors(Assembly assembly) =>
        GetLeafErrors(
            MessagingGuarantees.All.ToList(),
            DiscoverLeaves(assembly.GetTypes(), GuaranteeEvidenceTier.SharedSuite)
        );

    private static void _ValidateEntry(
        MessagingGuarantee guarantee,
        IReadOnlyList<GuaranteeBinding> bindings,
        IReadOnlyDictionary<string, TransportConformanceProfile> manifest,
        List<string> errors
    )
    {
        var id = guarantee.Id;

        if (!_IdPattern.IsMatch(id))
        {
            errors.Add($"{id} does not match the MSG-AREA-NN ID shape.");
        }

        if (string.IsNullOrWhiteSpace(guarantee.Statement))
        {
            errors.Add($"{id} has no statement.");
        }

        switch (guarantee.Status)
        {
            case GuaranteeStatus.Bound:
                _ValidateBoundEntry(guarantee, bindings, manifest, errors);
                break;

            case GuaranteeStatus.NoTestYet:
                if (!ConformanceSupport.IsValidIssueUrl(guarantee.IssueUrl))
                {
                    errors.Add(
                        $"{id} has no test yet and needs an absolute HTTP(S) issue URL that tracks writing one."
                    );
                }

                if (bindings.Count != 0 || guarantee.ManifestScenarios.Count != 0)
                {
                    errors.Add($"{id} is marked as having no test yet but has evidence; mark it Bound.");
                }

                break;

            case GuaranteeStatus.Unsupported:
                if (string.IsNullOrWhiteSpace(guarantee.Rationale))
                {
                    errors.Add($"{id} is Unsupported and needs a rationale.");
                }

                if (!ConformanceSupport.IsValidIssueUrl(guarantee.IssueUrl))
                {
                    errors.Add($"{id} is Unsupported and needs an absolute HTTP(S) issue URL.");
                }

                if (bindings.Count == 0)
                {
                    errors.Add(
                        $"{id} is Unsupported but no test is bound to it; bind the test that skips through RequireSupport."
                    );
                }

                break;

            default:
                errors.Add($"{id} has unknown status {guarantee.Status}.");
                break;
        }
    }

    private static void _ValidateBoundEntry(
        MessagingGuarantee guarantee,
        IReadOnlyList<GuaranteeBinding> bindings,
        IReadOnlyDictionary<string, TransportConformanceProfile> manifest,
        List<string> errors
    )
    {
        var id = guarantee.Id;

        foreach (var scenario in guarantee.ManifestScenarios)
        {
            var proven = manifest.Values.Any(profile =>
                (profile.IsRealBrokerLeafEnabled || !guarantee.RequiresRealSystem)
                && profile.Scenarios.TryGetValue(scenario, out var support)
                && support.Status == ConformanceStatus.Supported
            );

            if (!proven)
            {
                errors.Add(
                    guarantee.RequiresRealSystem
                        ? $"{id} relies on manifest scenario {scenario}, which no real broker leaf supports."
                        : $"{id} relies on manifest scenario {scenario}, which no provider supports."
                );
            }
        }

        if (bindings.Count == 0 && guarantee.ManifestScenarios.Count == 0)
        {
            errors.Add(
                $"{id} has no bound test and no 'no test yet' entry; "
                    + $"add [Guarantee(\"{id}\")] to a test or mark it NoTestYet with an issue."
            );
            return;
        }

        if (
            guarantee.RequiresRealSystem
            && guarantee.ManifestScenarios.Count == 0
            && bindings.All(binding => binding.Tier == GuaranteeEvidenceTier.Simulator)
        )
        {
            errors.Add(
                $"{id} needs a real broker or database but is bound only to simulator tests; "
                    + "bind a harness suite that real leaves run."
            );
        }
    }

    private static void _ValidateDeclaredBinding(
        IReadOnlyCollection<MessagingGuarantee> catalogue,
        GuaranteeBinding binding,
        List<string> errors
    )
    {
        var method = binding.Method;
        var target = $"{binding.TestClass.FullName}.{method.Name}";

        if (MessagingGuarantees.Find(catalogue, binding.GuaranteeId) is null)
        {
            errors.Add($"{target} carries [Guarantee(\"{binding.GuaranteeId}\")], which is not in the catalogue.");
        }

        // A suite method without its own [Fact] is legal when leaves override it; each provider's evidence test then
        // proves its leaves do. Anything else must already be a test that runs.
        var leafMustOverride =
            binding.TestClass.IsAbstract
            && method is { IsVirtual: true, IsFinal: false }
            && !TestMethodExecutability.IsTest(method);

        if (leafMustOverride)
        {
            return;
        }

        foreach (var problem in TestMethodExecutability.GetProblems(method, allowTheoryData: true))
        {
            errors.Add($"{target} carries {binding.GuaranteeId} but {problem}.");
        }
    }

    [GeneratedRegex("^MSG-[A-Z]+-[0-9]{2}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex _IdPattern { get; }
}
