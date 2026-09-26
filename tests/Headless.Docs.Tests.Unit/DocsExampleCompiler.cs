// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tests;

/// <summary>
/// Compiles one documentation example against every shipped package. An example may mix top-level statements
/// with type declarations in any order, so it is parsed as a script and split into a statements file and a
/// declarations file. <c>#line</c> directives map every diagnostic back to the guide line.
/// </summary>
public static class DocsExampleCompiler
{
    private static readonly CSharpParseOptions _ScriptOptions = new(
        LanguageVersion.Preview,
        DocumentationMode.None,
        SourceCodeKind.Script
    );

    private static readonly CSharpParseOptions _RegularOptions = new(LanguageVersion.Preview, DocumentationMode.None);

    /// <summary>
    /// Packages that declare the same extension member on the same receiver, so a consumer installs only one of
    /// them. The example's section heading selects the member; otherwise the first one is referenced.
    /// </summary>
    private static readonly string[][] _ExclusiveAssemblies =
    [
        [
            "Headless.Messaging.Storage.PostgreSql.EntityFramework",
            "Headless.Messaging.Storage.SqlServer.EntityFramework",
        ],
    ];

    /// <summary>Source generators a consumer's build runs; they are analyzers, not example references.</summary>
    private static readonly HashSet<string> _GeneratorAssemblies = new(StringComparer.Ordinal)
    {
        typeof(Headless.Generator.Primitives.PrimitiveGenerator).Assembly.GetName().Name!,
        typeof(Headless.Jobs.SourceGenerator.JobsIncrementalSourceGenerator).Assembly.GetName().Name!,
    };

    private static readonly GeneratorDriver _GeneratorDriver = CSharpGeneratorDriver.Create(
        [
            new Headless.Generator.Primitives.PrimitiveGenerator().AsSourceGenerator(),
            new Headless.Jobs.SourceGenerator.JobsIncrementalSourceGenerator().AsSourceGenerator(),
        ],
        parseOptions: _RegularOptions
    );

    private static readonly Lazy<IReadOnlyDictionary<string, MetadataReference>> _References = new(_LoadReferences);

    private static readonly ConcurrentDictionary<string, ReferenceSet> _ReferenceSets = new(StringComparer.Ordinal);

    private static readonly string _PreludesDirectory = Path.Combine(
        DocsExamples.RepositoryRoot,
        "tests",
        "Headless.Docs.Tests.Unit",
        "Preludes"
    );

    private static readonly ConcurrentDictionary<string, IReadOnlyList<SyntaxTree>> _Preludes = new(
        StringComparer.Ordinal
    );

    /// <summary>Returns the example's compile errors, mapped to guide lines.</summary>
    public static IReadOnlyList<string> Compile(DocsExample example)
    {
        return Compile(example, boot: false).Errors;
    }

    /// <summary>
    /// Compiles the example. With <paramref name="boot"/>, the statements run against a real builder whose host is
    /// then started and stopped (see <c>Preludes/_boot.cs</c>), instead of against the ambient stand-ins.
    /// </summary>
    public static (IReadOnlyList<string> Errors, Compilation Compilation) Compile(DocsExample example, bool boot)
    {
        // The regular parser splits top-level statements from declarations in any order but rejects script-style
        // members; the script parser accepts those but misreads statements such as `await using var x = ...;`.
        // Whichever parses cleanly wins, preferring regular.
        var regular = CSharpSyntaxTree.ParseText(example.Code, _RegularOptions);
        var script = regular.GetDiagnostics().Any(static d => d.Severity == DiagnosticSeverity.Error)
            ? CSharpSyntaxTree.ParseText(example.Code, _ScriptOptions)
            : regular;

        if (
            !ReferenceEquals(script, regular)
            && script.GetDiagnostics().Any(static d => d.Severity == DiagnosticSeverity.Error)
        )
        {
            script = regular;
        }
        var root = script.GetCompilationUnitRoot();

        var statements = root.Members.Where(static m => _IsStatement(m)).ToList();
        var declarations = root
            .Members.Where(static m =>
                m is BaseNamespaceDeclarationSyntax or BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
            )
            .ToList();
        var members = root.Members.Except(statements).Except(declarations).ToList();

        var trees = new List<SyntaxTree>();

        if (statements.Count > 0)
        {
            var (prefix, suffix) = boot ? _BootStatements(root) : (string.Empty, string.Empty);
            trees.Add(_CreateTree(example, root, statements, members: [], "statements", prefix, suffix));
        }

        if (declarations.Count > 0 || members.Count > 0 || statements.Count == 0)
        {
            trees.Add(_CreateTree(example, root, declarations, members, "declarations", string.Empty, string.Empty));
        }

        var declaredTypes = root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Select(static t => t.Identifier.ValueText)
            .Concat(
                root.DescendantNodes().OfType<DelegateDeclarationSyntax>().Select(static d => d.Identifier.ValueText)
            )
            .ToHashSet(StringComparer.Ordinal);

        trees.AddRange(_GetPreludes(example.Guide, boot).Select(tree => _WithoutTypes(tree, declaredTypes)));

        var referenceSet = _GetReferenceSet(example);
        trees.Add(referenceSet.HeadlessUsings);

        var compilation = CSharpCompilation.Create(
            assemblyName: "DocsExample",
            syntaxTrees: trees,
            references: referenceSet.References,
            options: new CSharpCompilationOptions(
                statements.Count > 0 ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        _GeneratorDriver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var generated,
            out var generatorDiagnostics
        );

        var errors = generatorDiagnostics
            .Concat(generated.GetDiagnostics())
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d =>
            {
                var span = d.Location.GetMappedLineSpan();
                var path = span.IsValid ? Path.GetFileName(span.Path) : "<no location>";

                return $"{path}:{span.StartLinePosition.Line + 1}: {d.Id} {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return (errors, generated);
    }

    /// <summary>
    /// Declares <c>builder</c> as a real local, and <c>services</c> and <c>configuration</c> unless the example
    /// declares them itself, then starts and stops the host after the example's statements.
    /// </summary>
    private static (string Prefix, string Suffix) _BootStatements(CompilationUnitSyntax root)
    {
        var declared = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Select(static v => v.Identifier.ValueText)
            .Concat(
                root.DescendantNodes()
                    .OfType<SingleVariableDesignationSyntax>()
                    .Select(static v => v.Identifier.ValueText)
            )
            .ToHashSet(StringComparer.Ordinal);

        var prefix = new StringBuilder("#line hidden\n");

        // The boot owns the host: an example that creates its own builder or app would start a second one.
        if (declared.Contains("builder") || declared.Contains("app"))
        {
            throw new InvalidOperationException(
                "A boot example must register on the ambient `builder` and leave building and running the app to the check."
            );
        }

        prefix.Append("var builder = DocsBoot.CreateBuilder();\n");

        if (!declared.Contains("services"))
        {
            prefix.Append("var services = builder.Services;\n");
        }

        if (!declared.Contains("configuration"))
        {
            prefix.Append("var configuration = builder.Configuration;\n");
        }

        return (prefix.ToString(), "#line hidden\nawait DocsBoot.RunAsync(builder);\n");
    }

    /// <summary>
    /// A statement, or a script-level method that is also valid as a local function. Other script-level members,
    /// such as a <see langword="public"/> method or a property, are compiled inside a wrapper class instead.
    /// </summary>
    private static bool _IsStatement(MemberDeclarationSyntax member)
    {
        return member switch
        {
            // The regular parser reads a top-level `public` method as a local function; it is a class member.
            GlobalStatementSyntax { Statement: LocalFunctionStatementSyntax function } => function.Modifiers.All(
                static m => m.Kind() is SyntaxKind.StaticKeyword or SyntaxKind.AsyncKeyword or SyntaxKind.UnsafeKeyword
            ),
            GlobalStatementSyntax => true,
            // A script parses `var x = ...;` as a field; without modifiers it is a local declaration.
            FieldDeclarationSyntax field => field.Modifiers.Count == 0,
            MethodDeclarationSyntax method => method.Modifiers.All(static m =>
                m.Kind() is SyntaxKind.StaticKeyword or SyntaxKind.AsyncKeyword or SyntaxKind.UnsafeKeyword
            ),
            _ => false,
        };
    }

    private static SyntaxTree _CreateTree(
        DocsExample example,
        CompilationUnitSyntax root,
        IReadOnlyList<MemberDeclarationSyntax> nodes,
        IReadOnlyList<MemberDeclarationSyntax> members,
        string kind,
        string prefix,
        string suffix
    )
    {
        var source = new StringBuilder();

        void append(SyntaxNode node)
        {
            var line = example.Line + node.GetLocation().GetLineSpan().StartLinePosition.Line;

            source.Append("#line ").Append(line).Append(" \"").Append(example.Guide).Append("\"\n");
            source.Append(node.ToString()).Append('\n');
        }

        foreach (var node in root.Externs.Cast<SyntaxNode>().Concat(root.Usings).Concat(root.AttributeLists))
        {
            append(node);
        }

        // Consumer types live in a namespace, and the source generators emit invalid code for global-namespace
        // types, so declarations without their own namespace get one that the statements import.
        if (
            string.Equals(kind, "declarations", StringComparison.Ordinal)
            && !nodes.Any(static n => n is BaseNamespaceDeclarationSyntax)
            && (nodes.Count > 0 || members.Count > 0)
        )
        {
            source.Append("#line hidden\nnamespace DocsExample;\n");
        }

        source.Append(prefix);

        foreach (var node in nodes)
        {
            append(node);
        }

        source.Append(suffix);

        if (members.Count > 0)
        {
            source.Append("#line hidden\ninternal sealed partial class ExampleMembers\n{\n");

            foreach (var member in members)
            {
                append(member);
            }

            source.Append("#line hidden\n}\n");
        }

        return CSharpSyntaxTree.ParseText(source.ToString(), _RegularOptions, path: $"{example.Id}.{kind}.cs");
    }

    private static IReadOnlyList<SyntaxTree> _GetPreludes(string guide, bool boot)
    {
        return _Preludes.GetOrAdd(
            boot ? $"{guide}+boot" : guide,
            static (_, state) =>
            {
                string[] names = state.boot
                    ? ["_global.cs", "_boot.cs", Path.ChangeExtension(state.guide, ".cs")]
                    : ["_global.cs", Path.ChangeExtension(state.guide, ".cs")];

                return names
                    .Select(static name => Path.Combine(_PreludesDirectory, name))
                    .Where(File.Exists)
#pragma warning disable MA0045 // False positive: preludes load inside a synchronous cache factory, which cannot await.
                    .Select(static path =>
                        CSharpSyntaxTree.ParseText(File.ReadAllText(path), _RegularOptions, path: path)
                    )
#pragma warning restore MA0045
                    .ToList();
            },
            (guide, boot)
        );
    }

    /// <summary>
    /// Removes prelude stand-ins for the types an example declares itself, so the example's own shape wins.
    /// </summary>
    private static SyntaxTree _WithoutTypes(SyntaxTree prelude, HashSet<string> declaredTypes)
    {
        if (declaredTypes.Count == 0)
        {
            return prelude;
        }

        var root = prelude.GetCompilationUnitRoot();
        var shadowed = root.DescendantNodes()
            .Where(node =>
                node switch
                {
                    BaseTypeDeclarationSyntax type => declaredTypes.Contains(type.Identifier.ValueText),
                    DelegateDeclarationSyntax @delegate => declaredTypes.Contains(@delegate.Identifier.ValueText),
                    _ => false,
                }
            )
            .ToList();

        return shadowed.Count == 0
            ? prelude
            : prelude.WithRootAndOptions(
                root.RemoveNodes(shadowed, SyntaxRemoveOptions.KeepNoTrivia)!,
                prelude.Options
            );
    }

    private static ReferenceSet _GetReferenceSet(DocsExample example)
    {
        var excluded = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var group in _ExclusiveAssemblies)
        {
            var selected =
                group.FirstOrDefault(name => example.Section.Contains(name, StringComparison.Ordinal)) ?? group[0];

            excluded.UnionWith(group.Where(name => !string.Equals(name, selected, StringComparison.Ordinal)));
        }

        return _ReferenceSets.GetOrAdd(
            string.Join(';', excluded),
            static (_, excluded) =>
            {
                var references = _References
                    .Value.Where(pair => !excluded.Contains(pair.Key))
                    .Select(static pair => pair.Value)
                    .ToList();

                return new ReferenceSet(references, _CreateHeadlessUsings(references));
            },
            excluded
        );
    }

    /// <summary>
    /// Imports every namespace with a public type from a <c>Headless</c> package. The guides omit framework usings
    /// because an IDE adds them, so a missing using is not an example defect; a missing or renamed member still is.
    /// </summary>
    private static SyntaxTree _CreateHeadlessUsings(IReadOnlyList<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(
            "HeadlessNamespaces",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var namespaces = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var reference in references)
        {
            if (
                compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly
                || !assembly.Name.StartsWith("Headless", StringComparison.Ordinal)
            )
            {
                continue;
            }

            // Headless also puts extension holders in the augmented type's namespace (OpenTelemetry.Trace,
            // Microsoft.EntityFrameworkCore, ...), which an IDE imports the same way. The test-support packages'
            // foreign namespaces (Bogus) are skipped: their common type names would collide with example types.
            var includeForeign = !assembly.Name.StartsWith("Headless.Testing", StringComparison.Ordinal);
            var pending = new Stack<INamespaceSymbol>([assembly.GlobalNamespace]);

            while (pending.Count > 0)
            {
                var current = pending.Pop();

                foreach (var child in current.GetNamespaceMembers())
                {
                    pending.Push(child);
                }

                var name = current.ToDisplayString();

                if (
                    !current.IsGlobalNamespace
                    && (includeForeign || name.StartsWith("Headless", StringComparison.Ordinal))
                    && current.GetTypeMembers().Any(static t => t.DeclaredAccessibility == Accessibility.Public)
                )
                {
                    namespaces.Add(name);
                }
            }
        }

        var source = string.Concat(namespaces.Select(static n => $"global using {n};\n"));

        return CSharpSyntaxTree.ParseText(source, _RegularOptions, path: "HeadlessUsings.cs");
    }

    private static IReadOnlyDictionary<string, MetadataReference> _LoadReferences()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);

        return paths
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .GroupBy(static path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal)
            .Where(static group => !_GeneratorAssemblies.Contains(group.Key))
            .ToDictionary(
                static group => group.Key,
                static group => (MetadataReference)MetadataReference.CreateFromFile(group.First()),
                StringComparer.Ordinal
            );
    }

    private sealed record ReferenceSet(IReadOnlyList<MetadataReference> References, SyntaxTree HeadlessUsings);
}
