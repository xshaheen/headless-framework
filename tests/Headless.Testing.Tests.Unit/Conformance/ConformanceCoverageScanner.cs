// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tests.Conformance;

/// <summary>A harness case that a concrete provider test class inherits but never runs.</summary>
internal sealed record ConformanceGap(string Project, string ProviderClass, string Case, string DeclaringBase);

/// <summary>A harness base class that declares cases and has no concrete provider class at all.</summary>
internal sealed record OrphanHarnessBase(string Project, string BaseClass, int CaseCount);

internal sealed record ConformanceScanResult(
    IReadOnlyList<ConformanceGap> Gaps,
    IReadOnlyList<OrphanHarnessBase> OrphanBases,
    IReadOnlyDictionary<string, int> CaseCountByBase,
    IReadOnlyDictionary<(string Project, string ProviderClass, string Base), int> CoveredByProviderAndBase
);

/// <summary>
/// Scans test sources for cross-provider conformance cases that a provider class silently never runs.
/// </summary>
/// <remarks>
/// A harness declares each case as a <c>public virtual</c> method with no test attribute, so xUnit only discovers it
/// when a provider class overrides it with <c>[Fact]</c> or <c>[Theory]</c>. A forgotten override is therefore
/// invisible: no failure, no skip, just a case that never runs. This reads syntax rather than loading assemblies
/// because the provider classes live in Docker-backed integration projects that CI never builds into a unit run,
/// and the source is the only artifact every CI leg has.
/// </remarks>
internal static class ConformanceCoverageScanner
{
    private const string _HarnessSuffix = ".Tests.Harness";
    private const string _TestRootType = "TestBase";

    public static async Task<ConformanceScanResult> ScanAsync(
        string testsDirectory,
        CancellationToken cancellationToken
    )
    {
        var classes = await _ParseClassesAsync(testsDirectory, cancellationToken);
        var byProjectAndName = classes.ToDictionary(c => (c.Project, c.Name));
        var byName = classes.ToLookup(c => c.Name, StringComparer.Ordinal);

        ClassModel? resolveBase(ClassModel model)
        {
            if (model.BaseName is null)
            {
                return null;
            }

            if (byProjectAndName.TryGetValue((model.Project, model.BaseName), out var local))
            {
                return local;
            }

            var candidates = byName[model.BaseName].ToList();
            var harness = candidates.Where(c => c.IsHarness).ToList();

            return harness.Count == 1 ? harness[0]
                : candidates.Count == 1 ? candidates[0]
                : null;
        }

        List<ClassModel> chainOf(ClassModel model)
        {
            var chain = new List<ClassModel>();

            for (var current = model; current is not null && !chain.Contains(current); current = resolveBase(current))
            {
                chain.Add(current);
            }

            return chain;
        }

        bool isTestClass(List<ClassModel> chain)
        {
            return string.Equals(chain[^1].BaseName, _TestRootType, StringComparison.Ordinal);
        }

        var concreteChains = classes.Where(c => !c.IsAbstract).Select(chainOf).ToList();

        // A harness class is a test base when it derives TestBase or when a concrete test class derives from it;
        // the second form catches bases that skip TestBase while leaving out drivers and fixtures whose public
        // virtual hooks are not cases.
        bool isTestBase(ClassModel model)
        {
            return isTestClass(chainOf(model))
                || concreteChains.Exists(chain => chain.Contains(model) && chain.Exists(c => c.HasTestMethods));
        }

        var casesByBase = classes
            .Where(c => c.IsHarness && c.Cases.Count > 0 && isTestBase(c))
            .ToDictionary(c => c, c => c.Cases);

        var gaps = new List<ConformanceGap>();
        var coveredCounts = new Dictionary<(string, string, string), int>();
        var consumedBases = new HashSet<ClassModel>();

        foreach (var concrete in classes.Where(c => !c.IsAbstract))
        {
            var chain = chainOf(concrete);

            for (var level = 0; level < chain.Count; level++)
            {
                if (!casesByBase.TryGetValue(chain[level], out var cases))
                {
                    continue;
                }

                consumedBases.Add(chain[level]);
                var below = chain.Take(level).ToList();
                var covered = 0;

                foreach (var @case in cases)
                {
                    if (below.Exists(c => c.RunnableOverrides.Contains(@case)))
                    {
                        covered++;
                        continue;
                    }

                    gaps.Add(new ConformanceGap(concrete.Project, concrete.Name, @case, chain[level].Name));
                }

                coveredCounts[(concrete.Project, concrete.Name, chain[level].Name)] = covered;
            }
        }

        var orphans = casesByBase
            .Keys.Where(b => !consumedBases.Contains(b))
            .Select(b => new OrphanHarnessBase(b.Project, b.Name, b.Cases.Count))
            .ToList();

        return new ConformanceScanResult(
            gaps,
            orphans,
            casesByBase.ToDictionary(p => p.Key.Name, p => p.Value.Count, StringComparer.Ordinal),
            coveredCounts
        );
    }

    private static async Task<List<ClassModel>> _ParseClassesAsync(
        string testsDirectory,
        CancellationToken cancellationToken
    )
    {
        var models = new Dictionary<(string Project, string Name), ClassModel>();

        foreach (var file in Directory.EnumerateFiles(testsDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(testsDirectory, file);
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (segments.Length < 2 || segments.Any(s => s is "bin" or "obj"))
            {
                continue;
            }

            var project = segments[0];
            var source = await File.ReadAllTextAsync(file, cancellationToken);
            var root = CSharpSyntaxTree
                .ParseText(source, cancellationToken: cancellationToken)
                .GetCompilationUnitRoot(cancellationToken);

            foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var key = (project, declaration.Identifier.ValueText);

                if (!models.TryGetValue(key, out var model))
                {
                    model = new ClassModel(project, declaration.Identifier.ValueText);
                    models[key] = model;
                }

                model.Merge(declaration);
            }
        }

        return [.. models.Values];
    }

    private sealed class ClassModel(string project, string name)
    {
        public string Project { get; } = project;

        public string Name { get; } = name;

        public bool IsHarness { get; } = project.EndsWith(_HarnessSuffix, StringComparison.Ordinal);

        public string? BaseName { get; private set; }

        public bool IsAbstract { get; private set; }

        public HashSet<string> Cases { get; } = new(StringComparer.Ordinal);

        public HashSet<string> RunnableOverrides { get; } = new(StringComparer.Ordinal);

        public bool HasTestMethods { get; private set; }

        public void Merge(ClassDeclarationSyntax declaration)
        {
            IsAbstract |= declaration.Modifiers.Any(SyntaxKind.AbstractKeyword);

            // Partial parts may repeat or omit the base list; the first part that names one wins.
            if (BaseName is null && declaration.BaseList?.Types.FirstOrDefault() is { } first)
            {
                BaseName = _SimpleName(first.Type);
            }

            foreach (var method in declaration.Members.OfType<MethodDeclarationSyntax>())
            {
                var modifiers = method.Modifiers;
                var methodName = method.Identifier.ValueText;

                if (!modifiers.Any(SyntaxKind.PublicKeyword) || !_ReturnsTestShape(method.ReturnType))
                {
                    continue;
                }

                var testAttribute = _FindTestAttribute(method);
                HasTestMethods |= testAttribute is not null;

                if (modifiers.Any(SyntaxKind.VirtualKeyword) && testAttribute is null)
                {
                    Cases.Add(methodName);
                }
                else if (
                    modifiers.Any(SyntaxKind.OverrideKeyword)
                    && testAttribute is not null
                    && !_IsUnconditionallySkipped(testAttribute)
                )
                {
                    RunnableOverrides.Add(methodName);
                }
            }
        }
    }

    private static string _SimpleName(TypeSyntax type)
    {
        return type switch
        {
            QualifiedNameSyntax qualified => _SimpleName(qualified.Right),
            AliasQualifiedNameSyntax alias => _SimpleName(alias.Name),
            GenericNameSyntax generic => generic.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => type.ToString(),
        };
    }

    private static bool _ReturnsTestShape(TypeSyntax returnType)
    {
        return returnType is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword }
            || _SimpleName(returnType) is "Task" or "ValueTask";
    }

    private static AttributeSyntax? _FindTestAttribute(MethodDeclarationSyntax method)
    {
        foreach (var attribute in method.AttributeLists.SelectMany(list => list.Attributes))
        {
            var name = _SimpleName(attribute.Name);

            if (name.EndsWith("Attribute", StringComparison.Ordinal))
            {
                name = name[..^"Attribute".Length];
            }

            // Covers Fact, Theory and the repository's RetryFact / RetryTheory variants.
            if (name.EndsWith("Fact", StringComparison.Ordinal) || name.EndsWith("Theory", StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    private static bool _IsUnconditionallySkipped(AttributeSyntax attribute)
    {
        var named = attribute.ArgumentList?.Arguments.Where(a => a.NameEquals is not null).ToList() ?? [];

        var skip = named.FirstOrDefault(a => a.NameEquals!.Name.Identifier.ValueText is "Skip");

        if (skip?.Expression.IsKind(SyntaxKind.NullLiteralExpression) != false)
        {
            return false;
        }

        // SkipUnless / SkipWhen make the skip depend on a runtime condition, so the case still runs where it can.
        return !named.Exists(a => a.NameEquals!.Name.Identifier.ValueText is "SkipUnless" or "SkipWhen");
    }
}
